using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    public class EnemyCorpse : NetworkBehaviour, IInteractable
    {
        public NetworkVariable<FixedString64Bytes> EnemyName = new("Enemy");
        public NetworkVariable<bool> IsEmpty = new(false);

        [SerializeField] private CorpseInventory corpseInventory;
        [SerializeField] private float despawnWithItems = 120f;
        [SerializeField] private float despawnWhenEmpty = 30f;

        private float spawnedAt;
        private float emptySince = -1f;

        public override void OnNetworkSpawn()
        {
            if (corpseInventory == null)
                corpseInventory = GetComponent<CorpseInventory>();

            spawnedAt = Time.time;
        }

        private void Update()
        {
            if (!IsServer) return;

            // 비었으면 빠른 소멸
            if (!IsEmpty.Value && corpseInventory != null && corpseInventory.SlotCount == 0)
            {
                IsEmpty.Value = true;
                emptySince = Time.time;
            }

            if (IsEmpty.Value && emptySince > 0 && Time.time - emptySince > despawnWhenEmpty)
            {
                NetworkObject?.Despawn(destroy: true);
                return;
            }

            // 아이템이 남아있어도 최대 시간 지나면 소멸
            if (!IsEmpty.Value && Time.time - spawnedAt > despawnWithItems)
            {
                NetworkObject?.Despawn(destroy: true);
            }
        }

        // ───────── IInteractable ─────────

        public string GetPromptText()
        {
            if (IsEmpty.Value) return "";
            return $"[F] {EnemyName.Value} 시체 루팅";
        }

        public void OnInteract(ulong clientId)
        {
            if (IsEmpty.Value) return;
            corpseInventory?.RequestOpenServerRpc(clientId);
        }

        // ───────── 서버 초기화 (EnemyStats.OnDeath에서 호출) ─────────

        /// <summary>
        /// 시체에 장비 + 추가 루팅 아이템을 채운다. 서버에서만 호출.
        /// </summary>
        public void InitializeServer(EnemyStatsSO statsSO)
        {
            if (!IsServer || statsSO == null) return;

            EnemyName.Value = statsSO.displayName;
            despawnWithItems = statsSO.corpseDespawnTime;
            despawnWhenEmpty = statsSO.corpseDespawnWhenEmpty;

            if (corpseInventory == null) return;

            // 1. 장착 장비 드랍
            if (statsSO.dropEquippedGear)
            {
                AddItemToCorpse(statsSO.defaultWeapon);
                AddItemToCorpse(statsSO.defaultAmmo);
                AddItemToCorpse(statsSO.defaultArmor);
            }

            // 2. 추가 루팅 테이블
            if (statsSO.extraLootTable != null && statsSO.extraLootCount > 0)
            {
                for (int i = 0; i < statsSO.extraLootCount; i++)
                {
                    var rolledItem = statsSO.extraLootTable.RollOne();
                    AddItemToCorpse(rolledItem);
                }
            }
        }

        private void AddItemToCorpse(ItemDataSO itemSO)
        {
            if (itemSO == null || corpseInventory == null) return;

            corpseInventory.Slots.Add(new ItemSlotData
            {
                itemId = new FixedString64Bytes(itemSO.itemID),
                gridX = 0,
                gridY = 0,
                rotated = false,
                stackCount = 1,
                currentDurability = 0,
                currentAmmo = 0,
            });
        }
    }
}
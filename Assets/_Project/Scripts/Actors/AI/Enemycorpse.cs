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

        [Header("장비 드랍")]
        [Tooltip("적 장착 장비가 시체에 들어갈 때 남길 내구도 비율 범위입니다.")]
        [SerializeField] private Vector2 durabilityRatioRange = new(0.3f, 0.8f);

        [Tooltip("적 기본 탄약이 시체에 들어갈 때 드랍할 총 탄 수 범위입니다.")]
        [SerializeField] private Vector2Int ammoDropRange = new(30, 60);

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
            corpseInventory?.OpenLootingUI();
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
                AddEquipmentToCorpse(statsSO.defaultWeapon);
                AddEquipmentToCorpse(statsSO.defaultArmor);
                AddAmmoToCorpse(statsSO.defaultAmmo);
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

        private void AddEquipmentToCorpse(ItemDataSO itemSO)
        {
            if (itemSO == null || corpseInventory == null) return;

            float maxDurability = GetMaxDurability(itemSO);
            float currentDurability = maxDurability > 0f
                ? Mathf.Round(maxDurability * Random.Range(GetDurabilityMin(), GetDurabilityMax()))
                : 0f;

            corpseInventory.Slots.Add(new ItemSlotData
            {
                itemId = new FixedString64Bytes(itemSO.itemID),
                gridX = 0,
                gridY = 0,
                rotated = false,
                stackCount = 1,
                currentDurability = currentDurability,
                currentAmmo = 0,
            });
        }

        private void AddAmmoToCorpse(AmmoDataSO ammoSO)
        {
            if (ammoSO == null || corpseInventory == null) return;

            int minAmmo = Mathf.Max(0, Mathf.Min(ammoDropRange.x, ammoDropRange.y));
            int maxAmmo = Mathf.Max(minAmmo, Mathf.Max(ammoDropRange.x, ammoDropRange.y));
            int remainingAmmo = Random.Range(minAmmo, maxAmmo + 1);
            int maxStackSize = Mathf.Max(1, ammoSO.maxStackSize);

            while (remainingAmmo > 0)
            {
                int stackAmount = Mathf.Min(maxStackSize, remainingAmmo);
                remainingAmmo -= stackAmount;

                corpseInventory.Slots.Add(new ItemSlotData
                {
                    itemId = new FixedString64Bytes(ammoSO.itemID),
                    gridX = 0,
                    gridY = 0,
                    rotated = false,
                    stackCount = (ushort)Mathf.Clamp(stackAmount, 1, ushort.MaxValue),
                    currentDurability = 0,
                    currentAmmo = 0,
                });
            }
        }

        private float GetDurabilityMin()
        {
            return Mathf.Clamp01(Mathf.Min(durabilityRatioRange.x, durabilityRatioRange.y));
        }

        private float GetDurabilityMax()
        {
            return Mathf.Clamp01(Mathf.Max(durabilityRatioRange.x, durabilityRatioRange.y));
        }

        private static float GetMaxDurability(ItemDataSO itemSO)
        {
            return itemSO switch
            {
                WeaponDataSO weapon => weapon.maxDurability,
                ArmorDataSO armor => armor.maxDurability,
                HelmetDataSO helmet => helmet.maxDurability,
                _ => 0f,
            };
        }
    }
}

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 사망 후 생성되는 공용 시체 상호작용 컴포넌트입니다.
    /// 장착 장비, 탄약, 추가 루팅을 CorpseInventory에 채워 루팅 UI와 연결합니다.
    /// </summary>
    public class EnemyCorpse : NetworkBehaviour, IInteractable
    {
        [Header("시체 상태")]
        [Tooltip("루팅 UI와 상호작용 프롬프트에 표시할 적 이름입니다.")]
        public NetworkVariable<FixedString64Bytes> EnemyName = new("Enemy");

        [Tooltip("시체 인벤토리가 비었는지 여부입니다.")]
        public NetworkVariable<bool> IsEmpty = new(false);

        [Header("시체 인벤토리")]
        [Tooltip("시체가 보유한 서버 권위 루팅 인벤토리입니다.")]
        [SerializeField] private CorpseInventory corpseInventory;

        [Tooltip("아이템이 남아 있는 시체가 유지되는 시간입니다.")]
        [SerializeField] private float despawnWithItems = 120f;

        [Tooltip("아이템이 모두 비워진 시체가 유지되는 시간입니다.")]
        [SerializeField] private float despawnWhenEmpty = 30f;

        [Header("장비 드랍")]
        [Tooltip("적 장착 장비가 시체에 들어갈 때 소모된 내구도 비율 범위입니다.")]
        [SerializeField] private Vector2 durabilityRatioRange = new(0.3f, 0.8f);

        private float spawnedAt;
        private float emptySince = -1f;
        private int remainingAmmoDropBudget;
        private const string InteractableLayerName = "ItemBox";
        private const int NormalEnemyAmmoDropLimit = 30;
        private const int BossEnemyAmmoDropLimit = 60;

        /// <summary>
        /// 네트워크 스폰 시 시체 인벤토리 참조와 생성 시간을 초기화합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            EnsureInteractionSurface();

            if (corpseInventory == null)
                corpseInventory = GetComponent<CorpseInventory>();

            spawnedAt = Time.time;
        }

        private void EnsureInteractionSurface()
        {
            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
            if (interactableLayer >= 0)
                gameObject.layer = interactableLayer;

            BoxCollider interactionCollider = GetComponent<BoxCollider>();
            if (interactionCollider == null)
                interactionCollider = gameObject.AddComponent<BoxCollider>();

            interactionCollider.isTrigger = true;
            if (interactionCollider.size == Vector3.zero)
            {
                interactionCollider.size = new Vector3(1.2f, 0.8f, 1.2f);
                interactionCollider.center = new Vector3(0f, 0.4f, 0f);
            }
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

        /// <summary>
        /// 플레이어가 시체를 바라볼 때 표시할 상호작용 문구를 반환합니다.
        /// </summary>
        /// <returns>시체가 비어 있지 않으면 루팅 프롬프트를 반환합니다.</returns>
        public string GetPromptText()
        {
            if (IsEmpty.Value) return "";
            return $"[F] {EnemyName.Value} 시체 루팅";
        }

        /// <summary>
        /// 상호작용한 클라이언트에게 시체 루팅 UI를 엽니다.
        /// </summary>
        /// <param name="clientId">상호작용한 클라이언트 ID입니다.</param>
        public void OnInteract(ulong clientId)
        {
            if (IsEmpty.Value) return;
            corpseInventory?.RequestOpenServerRpc(clientId);
        }

        // ───────── 서버 초기화 (EnemyStats.OnDeath에서 호출) ─────────

        /// <summary>
        /// 시체에 장비 + 추가 루팅 아이템을 채운다. 서버에서만 호출.
        /// </summary>
        /// <param name="statsSO">시체 루팅 기준으로 사용할 적 스탯 SO입니다.</param>
        /// <param name="sourceStats">사망 직전 실제 적 상태입니다. null이면 SO 기준으로만 드랍합니다.</param>
        public void InitializeServer(EnemyStatsSO statsSO, EnemyStats sourceStats = null)
        {
            if (!IsServer || statsSO == null) return;

            EnemyName.Value = statsSO.displayName;
            despawnWithItems = statsSO.corpseDespawnTime;
            despawnWhenEmpty = statsSO.corpseDespawnWhenEmpty;
            remainingAmmoDropBudget = statsSO.isBoss
                ? BossEnemyAmmoDropLimit
                : NormalEnemyAmmoDropLimit;

            if (corpseInventory == null) return;

            // 1. 장착 장비 드랍
            if (statsSO.dropEquippedGear)
            {
                AddEquipmentToCorpse(statsSO.defaultWeapon, sourceStats);
                AddEquipmentToCorpse(statsSO.defaultArmor, sourceStats);
                AddAmmoToCorpse(statsSO.defaultAmmo);
            }

            // 2. 추가 루팅 테이블
            if (statsSO.extraLootTable != null && statsSO.extraLootCount > 0)
            {
                for (int i = 0; i < statsSO.extraLootCount; i++)
                {
                    if (LootRollUtility.TryRollOne(statsSO.extraLootTable, out ItemDataSO rolledItem, out int amount))
                        AddItemToCorpse(rolledItem, amount);
                }
            }
        }

        private void AddItemToCorpse(ItemDataSO itemSO, int amount = 1)
        {
            if (itemSO == null || corpseInventory == null) return;

            int remaining = Mathf.Max(1, amount);
            if (itemSO is AmmoDataSO)
            {
                remaining = ReserveAmmoDropAmount(remaining);
                if (remaining <= 0)
                    return;
            }

            int maxStackSize = Mathf.Max(1, itemSO.maxStackSize);

            while (remaining > 0)
            {
                int stackAmount = Mathf.Min(maxStackSize, remaining);
                remaining -= stackAmount;

                corpseInventory.Slots.Add(new ItemSlotData
                {
                    itemId = new FixedString64Bytes(itemSO.itemID),
                    gridX = 0,
                    gridY = 0,
                    rotated = false,
                    stackCount = (ushort)Mathf.Clamp(stackAmount, 1, ushort.MaxValue),
                    currentDurability = stackAmount == 1 ? BuildDroppedDurability(itemSO, null, GetMaxDurability(itemSO)) : 0f,
                    currentAmmo = 0,
                });
            }
        }

        private void AddEquipmentToCorpse(ItemDataSO itemSO, EnemyStats sourceStats)
        {
            if (itemSO == null || corpseInventory == null) return;

            float maxDurability = GetMaxDurability(itemSO);
            float currentDurability = BuildDroppedDurability(itemSO, sourceStats, maxDurability);

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

            int remainingAmmo = ReserveAmmoDropAmount(remainingAmmoDropBudget);
            if (remainingAmmo <= 0)
                return;

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

        private int ReserveAmmoDropAmount(int requestedAmount)
        {
            if (requestedAmount <= 0 || remainingAmmoDropBudget <= 0)
                return 0;

            int reservedAmount = Mathf.Min(requestedAmount, remainingAmmoDropBudget);
            remainingAmmoDropBudget -= reservedAmount;
            return reservedAmount;
        }

        private float GetDurabilityMin()
        {
            return Mathf.Clamp01(Mathf.Min(durabilityRatioRange.x, durabilityRatioRange.y));
        }

        private float GetDurabilityMax()
        {
            return Mathf.Clamp01(Mathf.Max(durabilityRatioRange.x, durabilityRatioRange.y));
        }

        private float BuildDroppedDurability(ItemDataSO itemSO, EnemyStats sourceStats, float maxDurability)
        {
            if (maxDurability <= 0f)
            {
                return 0f;
            }

            float consumedRatio = Random.Range(GetDurabilityMin(), GetDurabilityMax());
            float generatedDurability = Mathf.Round(maxDurability * (1f - consumedRatio));

            if (itemSO is ArmorDataSO && sourceStats != null)
            {
                generatedDurability = Mathf.Min(generatedDurability, sourceStats.GetArmorDurability());
            }

            return Mathf.Clamp(generatedDurability, 0f, maxDurability);
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

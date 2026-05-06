using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    // 플레이어의 현재 소지 무게와 최대 소지 무게를 계산
    // GridInventory의 ServerGrid를 읽고, ItemDataSO.weightKg와 stackCount를 기준으로 현재 무게를 계산
    // Gym 하우징 보너스는 최대 소지 무게에 더함
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridInventory))]
    public sealed class PlayerCarryWeightSystem : NetworkBehaviour
    {
        [Header("참조")]
        [SerializeField]
        [Tooltip("플레이어 GridInventory입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private GridInventory gridInventory;

        [Header("기본 소지 무게")]
        [SerializeField]
        [Min(1f)]
        [Tooltip("기본 최대 소지 무게입니다. GameSystem 기준 기본값은 60kg입니다.")]
        private float baseMaxCarryWeightKg = 60f;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("하우징 효과로 증가한 최대 소지 무게 보너스입니다.")]
        private float housingCarryWeightBonusKg;

        [SerializeField]
        [Tooltip("현재 인벤토리에 들어 있는 아이템 총 무게입니다.")]
        private float currentWeightKg;

        [SerializeField]
        [Tooltip("현재 최종 최대 소지 무게입니다.")]
        private float currentMaxCarryWeightKg = 60f;

        [SerializeField]
        [Tooltip("현재 소지 무게가 최대 소지 무게를 초과했는지 여부입니다.")]
        private bool isOverWeight;

        [Header("로그")]
        [SerializeField]
        [Tooltip("무게 변경 시 Console에 로그를 출력합니다.")]
        private bool logWeightChanged = true;

        private IItemDatabase itemDatabase;
        private bool subscribedToInventory;
        private bool hasWarnedMissingItemDatabase;

        public float BaseMaxCarryWeightKg => baseMaxCarryWeightKg;
        public float HousingCarryWeightBonusKg => housingCarryWeightBonusKg;
        public float CurrentWeightKg => currentWeightKg;
        public float MaxCarryWeightKg => currentMaxCarryWeightKg;
        public bool IsOverWeight => isOverWeight;

        public float WeightRatio
        {
            get
            {
                if (currentMaxCarryWeightKg <= 0f)
                    return 0f;

                return Mathf.Clamp01(currentWeightKg / currentMaxCarryWeightKg);
            }
        }

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            RefreshMaxCarryWeight(false);
        }

        private void OnValidate()
        {
            if (baseMaxCarryWeightKg < 1f)
                baseMaxCarryWeightKg = 1f;

            if (housingCarryWeightBonusKg < 0f)
                housingCarryWeightBonusKg = 0f;

            FindRequiredComponents();
            RefreshMaxCarryWeight(false);
        }

        private void OnEnable()
        {
            TrySubscribeInventory();
        }

        private void OnDisable()
        {
            UnsubscribeInventory();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            TrySubscribeInventory();
            RecalculateCurrentWeight(true);
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeInventory();
            base.OnNetworkDespawn();
        }

        private void FindRequiredComponents()
        {
            if (gridInventory == null)
                gridInventory = GetComponent<GridInventory>();
        }

        private bool ResolveItemDatabase(bool logWarning)
        {
            if (itemDatabase != null)
                return true;

            itemDatabase = ServiceLocator.Get<IItemDatabase>();

            if (itemDatabase != null)
            {
                hasWarnedMissingItemDatabase = false;
                return true;
            }

            if (logWarning && logWeightChanged && !hasWarnedMissingItemDatabase)
            {
                hasWarnedMissingItemDatabase = true;

                Debug.LogWarning(
                    "[PlayerCarryWeightSystem] IItemDatabase를 찾지 못했습니다. " +
                    "현재 씬의 ItemDatabase 오브젝트 활성화 여부와 Awake 등록 여부를 확인하세요.",
                    this
                );
            }

            return false;
        }

        private void TrySubscribeInventory()
        {
            if (subscribedToInventory)
                return;

            if (!ShouldTrackThisPlayer())
                return;

            if (gridInventory == null || gridInventory.ServerGrid == null)
                return;

            gridInventory.ServerGrid.OnListChanged += HandleInventoryChanged;
            subscribedToInventory = true;
        }

        private void UnsubscribeInventory()
        {
            if (!subscribedToInventory)
                return;

            if (gridInventory != null && gridInventory.ServerGrid != null)
                gridInventory.ServerGrid.OnListChanged -= HandleInventoryChanged;

            subscribedToInventory = false;
        }

        private void HandleInventoryChanged(NetworkListEvent<ItemSlotData> changeEvent)
        {
            RecalculateCurrentWeight(false);
        }

        /// <summary>
        /// Gym 하우징 보너스를 최대 소지 무게에 적용합니다.
        /// </summary>
        public void ApplyHousingCarryWeightBonus(float bonusKg)
        {
            if (!ShouldTrackThisPlayer())
                return;

            float nextBonus = Mathf.Max(0f, bonusKg);

            if (Mathf.Approximately(housingCarryWeightBonusKg, nextBonus))
                return;

            float oldWeight = currentWeightKg;
            float oldMax = currentMaxCarryWeightKg;

            housingCarryWeightBonusKg = nextBonus;
            RefreshMaxCarryWeight(false);
            RefreshOverWeightState();

            PublishCarryWeightChanged(oldWeight, currentWeightKg, oldMax, currentMaxCarryWeightKg);

            if (logWeightChanged)
            {
                Debug.Log(
                    $"[PlayerCarryWeightSystem] 하우징 소지 무게 보너스 적용\n" +
                    $"기본 최대 소지 무게: {baseMaxCarryWeightKg:0.##}kg\n" +
                    $"하우징 보너스: +{housingCarryWeightBonusKg:0.##}kg\n" +
                    $"최종 최대 소지 무게: {currentMaxCarryWeightKg:0.##}kg",
                    this
                );
            }
        }

        /// <summary>
        /// 현재 GridInventory의 아이템 총 무게를 다시 계산합니다.
        /// </summary>
        public void RecalculateCurrentWeight(bool forceNotify)
        {
            if (!ShouldTrackThisPlayer())
                return;

            if (!ResolveItemDatabase(true))
                return;

            float oldWeight = currentWeightKg;
            float oldMax = currentMaxCarryWeightKg;

            currentWeightKg = CalculateInventoryWeight();
            RefreshMaxCarryWeight(false);
            RefreshOverWeightState();

            bool changed =
                forceNotify ||
                !Mathf.Approximately(oldWeight, currentWeightKg) ||
                !Mathf.Approximately(oldMax, currentMaxCarryWeightKg);

            if (!changed)
                return;

            PublishCarryWeightChanged(oldWeight, currentWeightKg, oldMax, currentMaxCarryWeightKg);

            if (logWeightChanged)
            {
                Debug.Log(
                    $"[PlayerCarryWeightSystem] 현재 소지 무게 갱신\n" +
                    $"현재 무게: {currentWeightKg:0.##}kg\n" +
                    $"최대 무게: {currentMaxCarryWeightKg:0.##}kg\n" +
                    $"비율: {WeightRatio * 100f:0.#}%\n" +
                    $"과중 여부: {isOverWeight}",
                    this
                );
            }
        }

        private float CalculateInventoryWeight()
        {
            if (gridInventory == null || gridInventory.ServerGrid == null)
                return 0f;

            if (!ResolveItemDatabase(true))
                return 0f;

            float totalWeight = 0f;

            for (int i = 0; i < gridInventory.ServerGrid.Count; i++)
            {
                ItemSlotData slot = gridInventory.ServerGrid[i];
                string itemId = slot.itemId.ToString();

                ItemDataSO itemData = itemDatabase.GetById(itemId);

                if (itemData == null)
                {
                    if (logWeightChanged)
                        Debug.LogWarning($"[PlayerCarryWeightSystem] ItemDataSO를 찾지 못했습니다. itemID: {itemId}", this);

                    continue;
                }

                int stackCount = Mathf.Max(1, slot.stackCount);
                totalWeight += itemData.weightKg * stackCount;
            }

            return totalWeight;
        }

        private void RefreshMaxCarryWeight(bool publishEvent)
        {
            float oldWeight = currentWeightKg;
            float oldMax = currentMaxCarryWeightKg;

            currentMaxCarryWeightKg = Mathf.Max(1f, baseMaxCarryWeightKg + housingCarryWeightBonusKg);
            RefreshOverWeightState();

            if (publishEvent)
                PublishCarryWeightChanged(oldWeight, currentWeightKg, oldMax, currentMaxCarryWeightKg);
        }

        private void RefreshOverWeightState()
        {
            isOverWeight = currentWeightKg > currentMaxCarryWeightKg;
        }

        private bool ShouldTrackThisPlayer()
        {
            if (!IsSpawned)
                return true;

            return IsServer || IsOwner;
        }

        private void PublishCarryWeightChanged(float oldWeight, float newWeight, float oldMax, float newMax)
        {
            EventBus.Publish(new PlayerCarryWeightChangedEvent
            {
                clientId = OwnerClientId,

                oldCurrentWeightKg = oldWeight,
                newCurrentWeightKg = newWeight,

                oldMaxCarryWeightKg = oldMax,
                newMaxCarryWeightKg = newMax,

                housingCarryWeightBonusKg = housingCarryWeightBonusKg,

                isOverWeight = isOverWeight,
                weightRatio = WeightRatio,
            });
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 현재 소지 무게 다시 계산")]
        private void DebugRecalculateWeight()
        {
            RecalculateCurrentWeight(true);
        }

        [ContextMenu("디버그 Gym 보너스 +22.5kg 적용")]
        private void DebugApplyMaxGymBonus()
        {
            ApplyHousingCarryWeightBonus(22.5f);
        }

        [ContextMenu("디버그 Gym 보너스 초기화")]
        private void DebugResetGymBonus()
        {
            ApplyHousingCarryWeightBonus(0f);
        }
#endif
    }
}
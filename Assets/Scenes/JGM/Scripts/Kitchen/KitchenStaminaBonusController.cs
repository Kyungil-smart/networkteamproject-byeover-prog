using System;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 주방 레벨에 따른 최대 스태미너 보너스를 계산하고 이벤트로 알립니다.
    /// PlayerStats나 UI를 직접 참조하지 않고, 추후 연동은 EventBus 구독으로 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public sealed class KitchenStaminaBonusController : MonoBehaviour
    {
        [Header("주방 시설")]
        [SerializeField]
        [Tooltip("스태미너 보너스를 계산할 주방 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private FacilityBase kitchenFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("최대 스태미너 보너스가 시작되는 시설 레벨입니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        [Tooltip("보너스 레벨 1단계마다 증가하는 최대 스태미너입니다. 기획 기준은 5입니다.")]
        private int staminaBonusPerLevel = 5;

        [Header("오프라인 테스트")]
        [SerializeField]
        [Tooltip("NetworkVariable을 직접 변경하지 않고 테스트용 레벨로 보너스를 계산할지 여부입니다.")]
        private bool useOfflineTestLevel;

        [SerializeField]
        [Range(1, 4)]
        [Tooltip("오프라인 테스트에서 사용할 주방 레벨입니다.")]
        private int offlineTestLevel = 1;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("현재 주방 레벨입니다. 런타임 확인용입니다.")]
        private int currentKitchenLevel = 1;

        [SerializeField]
        [Tooltip("현재 적용된 최대 스태미너 보너스입니다. 런타임 확인용입니다.")]
        private int currentMaxStaminaBonus;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스가 변경될 때 Console 로그를 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public int CurrentKitchenLevel => currentKitchenLevel;
        public int CurrentMaxStaminaBonus => currentMaxStaminaBonus;

        public event Action<int, int> OnStaminaBonusChanged;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnEnable()
        {
            SubscribeFacilityLevelChanged();
            RefreshBonus(true);
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
        }

        private void OnValidate()
        {
            if (bonusStartLevel < 2)
                bonusStartLevel = 2;

            if (staminaBonusPerLevel < 0)
                staminaBonusPerLevel = 0;

            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (kitchenFacility == null)
                kitchenFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (kitchenFacility == null)
                return;

            kitchenFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            kitchenFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (kitchenFacility == null)
                return;

            kitchenFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void HandleFacilityLevelChanged(int previousLevel, int newLevel)
        {
            RefreshBonus(false);
        }

        public void RefreshBonus()
        {
            RefreshBonus(false);
        }

        public void RefreshBonus(bool forceNotify)
        {
            if (!IsValidKitchenFacility())
                return;

            int nextLevel = GetCurrentKitchenLevel();
            int nextBonus = CalculateMaxStaminaBonus(nextLevel);
            bool changed = currentKitchenLevel != nextLevel || currentMaxStaminaBonus != nextBonus;

            currentKitchenLevel = nextLevel;
            currentMaxStaminaBonus = nextBonus;

            if (!changed && !forceNotify)
                return;

            OnStaminaBonusChanged?.Invoke(currentKitchenLevel, currentMaxStaminaBonus);

            EventBus.Publish(new KitchenStaminaBonusChangedEvent
            {
                level = currentKitchenLevel,
                maxStaminaBonus = currentMaxStaminaBonus,
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[KitchenStaminaBonusController] 주방 Lv.{currentKitchenLevel} 효과 적용\n" +
                    $"최대 스태미너 보너스: +{currentMaxStaminaBonus}",
                    this
                );
            }
        }

        public int GetMaxStaminaBonus()
        {
            return currentMaxStaminaBonus;
        }

        public int GetMaxStaminaBonusForLevel(int kitchenLevel)
        {
            return CalculateMaxStaminaBonus(kitchenLevel);
        }

        public void SetOfflineTestLevel(int level)
        {
            useOfflineTestLevel = true;
            offlineTestLevel = Mathf.Clamp(level, 1, 4);
            RefreshBonus(true);
        }

        public void ClearOfflineTestLevel()
        {
            useOfflineTestLevel = false;
            RefreshBonus(true);
        }

        private int GetCurrentKitchenLevel()
        {
            if (useOfflineTestLevel)
                return Mathf.Clamp(offlineTestLevel, 1, 4);

            if (kitchenFacility == null)
                return 1;

            return Mathf.Max(1, kitchenFacility.CurrentLevel.Value);
        }

        private int CalculateMaxStaminaBonus(int kitchenLevel)
        {
            if (kitchenLevel < bonusStartLevel)
                return 0;

            int bonusLevelCount = kitchenLevel - bonusStartLevel + 1;
            return bonusLevelCount * staminaBonusPerLevel;
        }

        private bool IsValidKitchenFacility()
        {
            if (kitchenFacility == null)
            {
                Debug.LogWarning("[KitchenStaminaBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (kitchenFacility.Type != FacilityType.Kitchen)
            {
                Debug.LogWarning($"[KitchenStaminaBonusController] 연결된 시설 타입이 Kitchen이 아닙니다. 현재 타입: {kitchenFacility.Type}", this);
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("스태미너 보너스 다시 계산")]
        private void DebugRefreshBonus()
        {
            RefreshBonus(true);
        }

        [ContextMenu("오프라인 테스트 레벨 해제")]
        private void DebugClearOfflineTestLevel()
        {
            ClearOfflineTestLevel();
        }
#endif
    }
}

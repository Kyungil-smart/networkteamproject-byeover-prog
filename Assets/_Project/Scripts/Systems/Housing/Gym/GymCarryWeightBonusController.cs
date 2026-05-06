using System;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 헬스장 레벨에 따른 소지 무게 보너스를 계산하고 이벤트로 알림
    // PlayerStats나 Inventory를 직접 참조하지 않고, 추후 연동은 EventBus 구독으로 처리
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public sealed class GymCarryWeightBonusController : MonoBehaviour
    {
        [Header("헬스장 시설")]
        [SerializeField]
        [Tooltip("소지 무게 보너스를 계산할 헬스장 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private FacilityBase gymFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("소지 무게 보너스가 시작되는 시설 레벨입니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0f)]
        [Tooltip("보너스 레벨 1단계마다 증가하는 소지 무게입니다. 기획 기준은 7.5kg입니다.")]
        private float carryWeightBonusPerLevel = 7.5f;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("현재 헬스장 레벨입니다. 런타임 확인용입니다.")]
        private int currentGymLevel = 1;

        [SerializeField]
        [Tooltip("현재 적용된 소지 무게 보너스입니다. 런타임 확인용입니다.")]
        private float currentCarryWeightBonusKg;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스가 변경될 때 Console 로그를 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public int CurrentGymLevel => currentGymLevel;
        public float CurrentCarryWeightBonusKg => currentCarryWeightBonusKg;

        public event Action<int, float> OnCarryWeightBonusChanged;

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

            if (carryWeightBonusPerLevel < 0f)
                carryWeightBonusPerLevel = 0f;

            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (gymFacility == null)
                gymFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (gymFacility == null)
                return;

            gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            gymFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (gymFacility == null)
                return;

            gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
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
            if (!IsValidGymFacility())
                return;

            int nextLevel = Mathf.Max(1, gymFacility.CurrentLevel.Value);
            float nextBonus = CalculateCarryWeightBonus(nextLevel);
            bool changed = currentGymLevel != nextLevel || !Mathf.Approximately(currentCarryWeightBonusKg, nextBonus);

            currentGymLevel = nextLevel;
            currentCarryWeightBonusKg = nextBonus;

            if (!changed && !forceNotify)
                return;

            OnCarryWeightBonusChanged?.Invoke(currentGymLevel, currentCarryWeightBonusKg);

            EventBus.Publish(new GymCarryWeightBonusChangedEvent
            {
                level = currentGymLevel,
                carryWeightBonusKg = currentCarryWeightBonusKg,
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[GymCarryWeightBonusController] 헬스장 Lv.{currentGymLevel} 효과 적용\n" +
                    $"소지 무게 보너스: +{currentCarryWeightBonusKg:0.##}kg",
                    this
                );
            }
        }

        public float GetCarryWeightBonus()
        {
            return currentCarryWeightBonusKg;
        }

        public float GetCarryWeightBonusForLevel(int gymLevel)
        {
            return CalculateCarryWeightBonus(gymLevel);
        }

        private float CalculateCarryWeightBonus(int gymLevel)
        {
            if (gymLevel < bonusStartLevel)
                return 0f;

            int bonusLevelCount = gymLevel - bonusStartLevel + 1;
            return bonusLevelCount * carryWeightBonusPerLevel;
        }

        private bool IsValidGymFacility()
        {
            if (gymFacility == null)
            {
                Debug.LogWarning("[GymCarryWeightBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (gymFacility.Type != FacilityType.Gym)
            {
                Debug.LogWarning($"[GymCarryWeightBonusController] 연결된 시설 타입이 Gym이 아닙니다. 현재 타입: {gymFacility.Type}", this);
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("소지 무게 보너스 다시 계산")]
        private void DebugRefreshBonus()
        {
            RefreshBonus(true);
        }
#endif
    }
}

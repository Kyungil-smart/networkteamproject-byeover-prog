using System;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 침대 레벨에 따른 최대 스태미너 보너스를 계산하고 이벤트로 알립니다.
    /// PlayerStats나 UI를 직접 참조하지 않고, 추후 연동은 EventBus 구독으로 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public sealed class BedStaminaBonusController : MonoBehaviour
    {
        [Header("침대 시설")]
        [SerializeField]
        [Tooltip("스태미너 보너스를 계산할 침대 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private FacilityBase bedFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("최대 스태미너 보너스가 시작되는 시설 레벨입니다. 기획 기준은 Lv2입니다.")]
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
        [Tooltip("오프라인 테스트에서 사용할 침대 레벨입니다.")]
        private int offlineTestLevel = 1;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("현재 침대 레벨입니다. 런타임 확인용입니다.")]
        private int currentBedLevel = 1;

        [SerializeField]
        [Tooltip("현재 적용된 최대 스태미너 보너스입니다. 런타임 확인용입니다.")]
        private int currentMaxStaminaBonus;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스가 변경될 때 Console 로그를 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public int CurrentBedLevel => currentBedLevel;
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

            offlineTestLevel = Mathf.Clamp(offlineTestLevel, 1, 4);
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (bedFacility == null)
                bedFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (bedFacility == null)
                return;

            bedFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            bedFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (bedFacility == null)
                return;

            bedFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
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
            if (!IsValidBedFacility())
                return;

            int nextLevel = GetCurrentBedLevel();
            int nextBonus = CalculateMaxStaminaBonus(nextLevel);
            bool changed = currentBedLevel != nextLevel || currentMaxStaminaBonus != nextBonus;

            currentBedLevel = nextLevel;
            currentMaxStaminaBonus = nextBonus;

            if (!changed && !forceNotify)
                return;

            OnStaminaBonusChanged?.Invoke(currentBedLevel, currentMaxStaminaBonus);

            EventBus.Publish(new BedStaminaBonusChangedEvent
            {
                level = currentBedLevel,
                maxStaminaBonus = currentMaxStaminaBonus,
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[BedStaminaBonusController] 침대 Lv.{currentBedLevel} 효과 적용\n" +
                    $"최대 스태미너 보너스: +{currentMaxStaminaBonus}",
                    this
                );
            }
        }

        public int GetMaxStaminaBonus()
        {
            return currentMaxStaminaBonus;
        }

        public int GetMaxStaminaBonusForLevel(int bedLevel)
        {
            return CalculateMaxStaminaBonus(bedLevel);
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

        private int GetCurrentBedLevel()
        {
            if (useOfflineTestLevel)
                return Mathf.Clamp(offlineTestLevel, 1, 4);

            if (bedFacility == null)
                return 1;

            return Mathf.Max(1, bedFacility.CurrentLevel.Value);
        }

        private int CalculateMaxStaminaBonus(int bedLevel)
        {
            if (bedLevel < bonusStartLevel)
                return 0;

            int bonusLevelCount = bedLevel - bonusStartLevel + 1;
            return bonusLevelCount * staminaBonusPerLevel;
        }

        private bool IsValidBedFacility()
        {
            if (bedFacility == null)
            {
                Debug.LogWarning("[BedStaminaBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (bedFacility.Type != FacilityType.Bed)
            {
                Debug.LogWarning($"[BedStaminaBonusController] 연결된 시설 타입이 Bed가 아닙니다. 현재 타입: {bedFacility.Type}", this);
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

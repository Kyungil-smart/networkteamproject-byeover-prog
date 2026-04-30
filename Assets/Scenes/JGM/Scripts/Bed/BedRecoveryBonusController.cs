using System;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    public enum BedRaidStartRecoveryType
    {
        None,
        MinorHeal,
        FullHp
    }

    /// <summary>
    /// 침대 시설 레벨에 따른 오프라인 HP 회복 보너스와 레이드 시작 회복 효과를 계산합니다.
    /// PlayerStats, UI, 인벤토리는 직접 참조하지 않고 계산 결과만 제공합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public class BedRecoveryBonusController : MonoBehaviour
    {
        [Header("침대 시설")]
        [SerializeField]
        [Tooltip("침대 효과를 계산할 시설입니다. 비워두면 같은 오브젝트의 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase bedFacility;

        [Header("오프라인 회복 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("오프라인 HP 회복 보너스가 시작되는 침대 레벨입니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        [Tooltip("침대 레벨이 1 증가할 때마다 늘어나는 오프라인 HP 회복 보너스 퍼센트입니다.")]
        private int offlineHpRecoveryBonusPerLevel = 20;

        [Header("오프라인 테스트")]
        [SerializeField]
        [Tooltip("NetworkVariable을 직접 바꾸지 않고 테스트용 레벨로 침대 효과를 계산할지 여부입니다.")]
        private bool useOfflineTestLevel;

        [SerializeField]
        [Range(1, 4)]
        [Tooltip("오프라인 테스트에서 사용할 침대 레벨입니다.")]
        private int offlineTestLevel = 1;

        [Header("현재 효과 확인")]
        [SerializeField]
        [Tooltip("현재 침대 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentBedLevel = 1;

        [SerializeField]
        [Tooltip("현재 침대 레벨로 적용되는 오프라인 HP 회복 보너스 퍼센트입니다.")]
        private int currentOfflineHpRecoveryBonusPercent;

        [SerializeField]
        [Tooltip("현재 침대 레벨로 적용되는 레이드 시작 회복 효과입니다.")]
        private BedRaidStartRecoveryType currentRaidStartRecoveryType = BedRaidStartRecoveryType.None;

        [Header("로그")]
        [SerializeField]
        [Tooltip("침대 효과 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public int CurrentBedLevel => currentBedLevel;
        public int CurrentOfflineHpRecoveryBonusPercent => currentOfflineHpRecoveryBonusPercent;
        public BedRaidStartRecoveryType CurrentRaidStartRecoveryType => currentRaidStartRecoveryType;

        public event Action<int, int, BedRaidStartRecoveryType> OnBedRecoveryBonusChanged;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            RefreshBonus();
        }

        private void OnEnable()
        {
            SubscribeFacilityLevelChanged();
            RefreshBonus();
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
        }

        private void OnValidate()
        {
            if (bonusStartLevel < 2)
                bonusStartLevel = 2;

            if (offlineHpRecoveryBonusPerLevel < 0)
                offlineHpRecoveryBonusPerLevel = 0;

            offlineTestLevel = Mathf.Clamp(offlineTestLevel, 1, 4);

            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                currentBedLevel = useOfflineTestLevel ? offlineTestLevel : 1;
                currentOfflineHpRecoveryBonusPercent = CalculateOfflineHpRecoveryBonusPercent(currentBedLevel);
                currentRaidStartRecoveryType = GetRaidStartRecoveryTypeByLevel(currentBedLevel);
            }
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
            if (useOfflineTestLevel)
                return;

            RefreshBonus();
        }

        public void RefreshBonus()
        {
            RefreshBonus(false);
        }

        public void RefreshBonus(bool forceLog)
        {
            if (!IsValidBedFacility())
                return;

            int previousLevel = currentBedLevel;
            int previousBonus = currentOfflineHpRecoveryBonusPercent;
            BedRaidStartRecoveryType previousRecoveryType = currentRaidStartRecoveryType;

            currentBedLevel = GetCurrentBedLevel();
            currentOfflineHpRecoveryBonusPercent = CalculateOfflineHpRecoveryBonusPercent(currentBedLevel);
            currentRaidStartRecoveryType = GetRaidStartRecoveryTypeByLevel(currentBedLevel);

            bool changed =
                previousLevel != currentBedLevel ||
                previousBonus != currentOfflineHpRecoveryBonusPercent ||
                previousRecoveryType != currentRaidStartRecoveryType;

            if (changed)
            {
                OnBedRecoveryBonusChanged?.Invoke(
                    currentBedLevel,
                    currentOfflineHpRecoveryBonusPercent,
                    currentRaidStartRecoveryType
                );
            }

            if (logBonusChanged && (changed || forceLog))
            {
                Debug.Log(
                    $"[BedRecoveryBonusController] 침대 Lv.{currentBedLevel} / 오프라인 HP 회복 +{currentOfflineHpRecoveryBonusPercent}% / 레이드 시작 효과: {currentRaidStartRecoveryType}",
                    this
                );
            }
        }

        public int GetOfflineHpRecoveryBonusPercent()
        {
            RefreshBonus();
            return currentOfflineHpRecoveryBonusPercent;
        }

        public BedRaidStartRecoveryType GetRaidStartRecoveryType()
        {
            RefreshBonus();
            return currentRaidStartRecoveryType;
        }

        public int GetOfflineHpRecoveryBonusPercentForLevel(int bedLevel)
        {
            return CalculateOfflineHpRecoveryBonusPercent(bedLevel);
        }

        public BedRaidStartRecoveryType GetRaidStartRecoveryTypeForLevel(int bedLevel)
        {
            return GetRaidStartRecoveryTypeByLevel(bedLevel);
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

        private int CalculateOfflineHpRecoveryBonusPercent(int bedLevel)
        {
            if (bedLevel < bonusStartLevel)
                return 0;

            int bonusLevelCount = bedLevel - bonusStartLevel + 1;
            return bonusLevelCount * offlineHpRecoveryBonusPerLevel;
        }

        private BedRaidStartRecoveryType GetRaidStartRecoveryTypeByLevel(int bedLevel)
        {
            if (bedLevel >= 4)
                return BedRaidStartRecoveryType.FullHp;

            if (bedLevel >= 3)
                return BedRaidStartRecoveryType.MinorHeal;

            return BedRaidStartRecoveryType.None;
        }

        private bool IsValidBedFacility()
        {
            if (bedFacility == null)
            {
                Debug.LogWarning("[BedRecoveryBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (bedFacility.Type != FacilityType.Bed)
            {
                Debug.LogWarning(
                    $"[BedRecoveryBonusController] 연결된 시설 타입이 Bed가 아닙니다. 현재 타입: {bedFacility.Type}",
                    this
                );
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("침대 효과 다시 계산")]
        private void DebugRefreshBonus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[BedRecoveryBonusController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            RefreshBonus(true);
        }

        [ContextMenu("오프라인 테스트 레벨 해제")]
        private void DebugClearOfflineTestLevel()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[BedRecoveryBonusController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            ClearOfflineTestLevel();
        }
#endif
    }
}
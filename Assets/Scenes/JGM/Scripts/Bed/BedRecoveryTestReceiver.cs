using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 침대 효과가 실제 플레이어 체력 회복에 어떻게 반영될지 확인하는 테스트용 리시버입니다.
    /// 실제 PlayerStats가 완성되면 이 스크립트는 제거하고 PlayerStats 또는 RaidStartSystem에서 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class BedRecoveryTestReceiver : MonoBehaviour
    {
        [Header("침대 효과")]
        [SerializeField]
        [Tooltip("침대 레벨에 따른 회복 효과를 계산하는 컨트롤러입니다.")]
        private BedRecoveryBonusController recoveryBonusController;

        [Header("테스트 기본값")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트용 플레이어 최대 체력입니다.")]
        private int baseMaxHealth = 100;

        [SerializeField]
        [Min(1)]
        [Tooltip("기본 오프라인 HP 회복 속도입니다. 100이면 기본 속도입니다.")]
        private int baseOfflineRecoveryRatePercent = 100;

        [SerializeField]
        [Range(0, 100)]
        [Tooltip("레이드 시작 전 테스트용 현재 체력 퍼센트입니다.")]
        private int testHealthPercentBeforeRaid = 40;

        [SerializeField]
        [Range(1, 100)]
        [Tooltip("경미 치료 효과가 적용될 때 최소로 보정할 체력 퍼센트입니다.")]
        private int minorHealTargetPercent = 50;

        [Header("적용 결과 확인")]
        [SerializeField]
        [Tooltip("현재 침대 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentBedLevel = 1;

        [SerializeField]
        [Tooltip("침대 레벨로 적용된 오프라인 HP 회복 보너스 퍼센트입니다.")]
        private int currentOfflineRecoveryBonusPercent;

        [SerializeField]
        [Tooltip("기본 회복 속도와 침대 보너스를 더한 최종 오프라인 HP 회복 속도입니다.")]
        private int currentFinalOfflineRecoveryRatePercent;

        [SerializeField]
        [Tooltip("침대 레벨로 적용된 레이드 시작 회복 타입입니다.")]
        private BedRaidStartRecoveryType currentRaidStartRecoveryType = BedRaidStartRecoveryType.None;

        [SerializeField]
        [Tooltip("레이드 시작 전 테스트용 현재 체력입니다.")]
        private int previewHealthBeforeRaid;

        [SerializeField]
        [Tooltip("침대 효과 적용 후 레이드 시작 시 예상 체력입니다.")]
        private int previewHealthAfterRaidStart;

        [Header("로그")]
        [SerializeField]
        [Tooltip("침대 효과 적용 결과를 Console에 출력할지 여부입니다.")]
        private bool logRecoveryResult = true;

        public int CurrentBedLevel => currentBedLevel;
        public int CurrentOfflineRecoveryBonusPercent => currentOfflineRecoveryBonusPercent;
        public int CurrentFinalOfflineRecoveryRatePercent => currentFinalOfflineRecoveryRatePercent;
        public BedRaidStartRecoveryType CurrentRaidStartRecoveryType => currentRaidStartRecoveryType;
        public int PreviewHealthBeforeRaid => previewHealthBeforeRaid;
        public int PreviewHealthAfterRaidStart => previewHealthAfterRaidStart;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            ApplyBedRecoveryBonus();
        }

        private void OnEnable()
        {
            SubscribeBedRecoveryChanged();
            ApplyBedRecoveryBonus();
        }

        private void OnDisable()
        {
            UnsubscribeBedRecoveryChanged();
        }

        private void OnValidate()
        {
            if (baseMaxHealth < 1)
                baseMaxHealth = 1;

            if (baseOfflineRecoveryRatePercent < 1)
                baseOfflineRecoveryRatePercent = 1;

            testHealthPercentBeforeRaid = Mathf.Clamp(testHealthPercentBeforeRaid, 0, 100);
            minorHealTargetPercent = Mathf.Clamp(minorHealTargetPercent, 1, 100);

            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                previewHealthBeforeRaid = CalculateHealthByPercent(testHealthPercentBeforeRaid);
                previewHealthAfterRaidStart = previewHealthBeforeRaid;
                currentFinalOfflineRecoveryRatePercent = baseOfflineRecoveryRatePercent;
            }
        }

        private void FindRequiredComponents()
        {
            if (recoveryBonusController == null)
                recoveryBonusController = GetComponent<BedRecoveryBonusController>();
        }

        private void SubscribeBedRecoveryChanged()
        {
            if (recoveryBonusController == null)
                return;

            recoveryBonusController.OnBedRecoveryBonusChanged -= HandleBedRecoveryBonusChanged;
            recoveryBonusController.OnBedRecoveryBonusChanged += HandleBedRecoveryBonusChanged;
        }

        private void UnsubscribeBedRecoveryChanged()
        {
            if (recoveryBonusController == null)
                return;

            recoveryBonusController.OnBedRecoveryBonusChanged -= HandleBedRecoveryBonusChanged;
        }

        private void HandleBedRecoveryBonusChanged(
            int bedLevel,
            int offlineHpRecoveryBonusPercent,
            BedRaidStartRecoveryType raidStartRecoveryType
        )
        {
            ApplyBedRecoveryBonus();
        }

        public void ApplyBedRecoveryBonus()
        {
            if (recoveryBonusController == null)
            {
                Debug.LogWarning("[BedRecoveryTestReceiver] BedRecoveryBonusController가 연결되어 있지 않습니다.", this);
                return;
            }

            currentBedLevel = recoveryBonusController.CurrentBedLevel;
            currentOfflineRecoveryBonusPercent = recoveryBonusController.GetOfflineHpRecoveryBonusPercent();
            currentRaidStartRecoveryType = recoveryBonusController.GetRaidStartRecoveryType();

            currentFinalOfflineRecoveryRatePercent =
                baseOfflineRecoveryRatePercent + currentOfflineRecoveryBonusPercent;

            previewHealthBeforeRaid = CalculateHealthByPercent(testHealthPercentBeforeRaid);
            previewHealthAfterRaidStart = CalculateRaidStartHealth(
                previewHealthBeforeRaid,
                currentRaidStartRecoveryType
            );

            if (logRecoveryResult)
            {
                Debug.Log(
                    $"[BedRecoveryTestReceiver] 침대 Lv.{currentBedLevel} / 오프라인 회복 속도 {currentFinalOfflineRecoveryRatePercent}% / 레이드 시작 효과 {currentRaidStartRecoveryType} / 시작 전 HP {previewHealthBeforeRaid} / 적용 후 HP {previewHealthAfterRaidStart}",
                    this
                );
            }
        }

        private int CalculateHealthByPercent(int percent)
        {
            return Mathf.RoundToInt(baseMaxHealth * (percent / 100f));
        }

        private int CalculateRaidStartHealth(int currentHealth, BedRaidStartRecoveryType recoveryType)
        {
            currentHealth = Mathf.Clamp(currentHealth, 0, baseMaxHealth);

            switch (recoveryType)
            {
                case BedRaidStartRecoveryType.MinorHeal:
                    int minorHealTargetHealth = CalculateHealthByPercent(minorHealTargetPercent);
                    return Mathf.Max(currentHealth, minorHealTargetHealth);

                case BedRaidStartRecoveryType.FullHp:
                    return baseMaxHealth;

                default:
                    return currentHealth;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("침대 회복 효과 다시 적용")]
        private void DebugApplyBedRecoveryBonus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[BedRecoveryTestReceiver] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            ApplyBedRecoveryBonus();
        }
#endif
    }
}
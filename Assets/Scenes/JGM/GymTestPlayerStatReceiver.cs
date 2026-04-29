using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// 아직 실제 PlayerStats가 없기 때문에 헬스장 보너스 적용 결과를 확인하기 위한 테스트용 스탯 리시버이다.
    /// 기본 스태미나와 기본 소지무게에 GymStatBonusController가 계산한 보너스를 더해서 최종 값을 보여준다.
    /// </summary>
    public sealed class GymTestPlayerStatReceiver : MonoBehaviour
    {
        [Header("헬스장 보너스 계산기")]
        [SerializeField]
        [Tooltip("헬스장 레벨 보너스를 계산하는 컨트롤러입니다.")]
        private GymStatBonusController bonusController;

        [Header("테스트 기본 능력치")]
        [SerializeField]
        [Tooltip("테스트용 기본 최대 스태미나입니다.")]
        private float baseMaxStamina = 100f;

        [SerializeField]
        [Tooltip("테스트용 기본 소지무게입니다.")]
        private float baseCarryWeight = 40f;

        [Header("계산된 최종 능력치")]
        [SerializeField]
        [Tooltip("헬스장 보너스가 적용된 최종 최대 스태미나입니다.")]
        private float finalMaxStamina;

        [SerializeField]
        [Tooltip("헬스장 보너스가 적용된 최종 소지무게입니다.")]
        private float finalCarryWeight;

        public float BaseMaxStamina => baseMaxStamina;
        public float BaseCarryWeight => baseCarryWeight;
        public float FinalMaxStamina => finalMaxStamina;
        public float FinalCarryWeight => finalCarryWeight;

        private void Reset()
        {
            bonusController = GetComponent<GymStatBonusController>();
        }

        private void Awake()
        {
            if (bonusController == null)
                bonusController = GetComponent<GymStatBonusController>();

            ApplyGymBonus();
        }

        [ContextMenu("헬스장 보너스 적용")]
        public void ApplyGymBonus()
        {
            if (bonusController == null)
            {
                Debug.LogWarning("[GymTestPlayerStatReceiver] GymStatBonusController가 연결되어 있지 않습니다.", this);
                return;
            }

            GymStatBonus bonus = bonusController.GetCurrentBonus();

            finalMaxStamina = baseMaxStamina + bonus.staminaBonus;
            finalCarryWeight = baseCarryWeight + bonus.carryWeightBonus;

            Debug.Log(
                $"[GymTestPlayerStatReceiver] 헬스장 Lv.{bonus.gymLevel} 보너스 적용 완료 / " +
                $"최대 스태미나 {baseMaxStamina} → {finalMaxStamina}, " +
                $"소지무게 {baseCarryWeight} → {finalCarryWeight}",
                this);
        }
    }
}
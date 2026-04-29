using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// GymFacility의 현재 레벨을 읽고, 레벨에 맞는 스태미나와 소지무게 보너스를 계산한다.
    /// 실제 PlayerStats 적용은 아직 플레이어 시스템이 없으므로 여기서 직접 처리하지 않는다.
    /// </summary>
    public sealed class GymStatBonusController : MonoBehaviour
    {
        private const int MinGymLevel = 1;
        private const int MaxGymLevel = 4;

        [Header("헬스장 시설 참조")]
        [SerializeField]
        [Tooltip("현재 레벨을 읽어올 GymFacility 컴포넌트입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private GymFacility gymFacility;

        [Header("레벨당 증가량")]
        [SerializeField]
        [Tooltip("헬스장 레벨이 1 증가할 때마다 추가되는 최대 스태미나 값입니다.")]
        private float staminaPerLevel = 5f;

        [SerializeField]
        [Tooltip("헬스장 레벨이 1 증가할 때마다 추가되는 소지무게 값입니다.")]
        private float carryWeightPerLevel = 5f;

        public int CurrentGymLevel
        {
            get
            {
                if (gymFacility == null)
                    return MinGymLevel;

                return Mathf.Clamp(gymFacility.CurrentLevelValue, MinGymLevel, MaxGymLevel);
            }
        }

        public float CurrentStaminaBonus => CurrentGymLevel * staminaPerLevel;

        public float CurrentCarryWeightBonus => CurrentGymLevel * carryWeightPerLevel;

        private void Reset()
        {
            gymFacility = GetComponent<GymFacility>();
        }

        private void Awake()
        {
            if (gymFacility == null)
                gymFacility = GetComponent<GymFacility>();
        }

        public GymStatBonus GetCurrentBonus()
        {
            int level = CurrentGymLevel;

            return new GymStatBonus
            {
                gymLevel = level,
                staminaBonus = level * staminaPerLevel,
                carryWeightBonus = level * carryWeightPerLevel
            };
        }

        [ContextMenu("현재 헬스장 보너스 출력")]
        private void DebugPrintCurrentBonus()
        {
            GymStatBonus bonus = GetCurrentBonus();

            Debug.Log(
                $"[GymStatBonusController] Gym Lv.{bonus.gymLevel} / " +
                $"Stamina +{bonus.staminaBonus} / CarryWeight +{bonus.carryWeightBonus}",
                this);
        }
    }

    public struct GymStatBonus
    {
        public int gymLevel;
        public float staminaBonus;
        public float carryWeightBonus;
    }
}
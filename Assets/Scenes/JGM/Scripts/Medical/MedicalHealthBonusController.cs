using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 의료시설 레벨에 따른 최대 체력 보너스를 계산하고 이벤트로 알립니다.
    /// PlayerStats를 직접 참조하지 않기 때문에 추후 플레이어/UI 시스템과 안전하게 연결할 수 있습니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MedicalFacility))]
    public class MedicalHealthBonusController : NetworkBehaviour
    {
        [Header("의료시설")]
        [SerializeField]
        [Tooltip("효과를 계산할 의료시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private MedicalFacility medicalFacility;

        [Header("체력 보너스 규칙")]
        [SerializeField]
        [Min(1)]
        [Tooltip("보너스가 적용되기 시작하는 레벨입니다. 의료시설은 Lv2부터 적용합니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        [Tooltip("레벨이 오를 때마다 증가하는 최대 체력 보너스입니다.")]
        private int healthBonusPerLevel = 5;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스 변경 결과를 Console에 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        private int currentHealthBonus;

        public int CurrentHealthBonus => currentHealthBonus;

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
            FindRequiredComponents();

            if (medicalFacility == null)
                return;

            medicalFacility.CurrentLevel.OnValueChanged += HandleMedicalLevelChanged;
            ApplyBonus(medicalFacility.CurrentLevel.Value);
        }

        private void OnDisable()
        {
            if (medicalFacility == null)
                return;

            medicalFacility.CurrentLevel.OnValueChanged -= HandleMedicalLevelChanged;
        }

        private void FindRequiredComponents()
        {
            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();
        }

        private void HandleMedicalLevelChanged(int oldLevel, int newLevel)
        {
            ApplyBonus(newLevel);
        }

        public int CalculateHealthBonus(int level)
        {
            if (level < bonusStartLevel)
                return 0;

            int bonusStep = level - bonusStartLevel + 1;
            return bonusStep * healthBonusPerLevel;
        }

        [ContextMenu("의료시설 효과 다시 계산")]
        public void RecalculateBonusForTest()
        {
            if (medicalFacility == null)
            {
                Debug.LogWarning("[MedicalHealthBonusController] 의료시설이 연결되어 있지 않습니다.", this);
                return;
            }

            ApplyBonus(medicalFacility.CurrentLevel.Value);
        }

        private void ApplyBonus(int level)
        {
            currentHealthBonus = CalculateHealthBonus(level);

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[MedicalHealthBonusController] 의료시설 Lv.{level} 효과 적용\n" +
                    $"최대 체력 보너스: +{currentHealthBonus}",
                    this
                );
            }

            EventBus.Publish(new MedicalHealthBonusChangedEvent
            {
                facilityType = FacilityType.Medical,
                level = level,
                maxHealthBonus = currentHealthBonus,
                source = "MedicalFacility"
            });
        }
    }
}

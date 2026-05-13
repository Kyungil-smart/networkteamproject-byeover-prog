using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    // 의료시설 레벨에 따라 플레이어 최대 체력 보너스를 계산하고 이벤트로 전달
    // 실제 PlayerHealthSystem 적용은 PlayerHousingBonusReceiver가 담당
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MedicalFacility))]
    public sealed class MedicalHealthBonusController : NetworkBehaviour
    {
        [Header("대상 의료시설")]
        [SerializeField]
        private MedicalFacility medicalFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(1)]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        private int healthBonusPerLevel = 5;

        [Header("런타임 확인")]
        [SerializeField]
        private int currentHealthBonus;

        [Header("로그")]
        [SerializeField]
        private bool logBonusChanged = true;

        public int CurrentHealthBonus => currentHealthBonus;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnValidate()
        {
            FindRequiredComponents();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            FindRequiredComponents();

            if (medicalFacility == null)
            {
                Debug.LogWarning("[MedicalHealthBonusController] MedicalFacility가 없어 의료시설 효과를 적용할 수 없습니다.", this);
                return;
            }

            medicalFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            medicalFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;

            RecalculateAndPublish();
        }

        public override void OnNetworkDespawn()
        {
            if (medicalFacility != null)
                medicalFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void FindRequiredComponents()
        {
            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();
        }

        private void HandleFacilityLevelChanged(int previousLevel, int currentLevel)
        {
            RecalculateAndPublish();
        }

        public void RecalculateAndPublish()
        {
            if (!IsServer)
                return;

            if (medicalFacility == null)
                return;

            int currentLevel = medicalFacility.GetCurrentLevel();

            if (currentLevel < bonusStartLevel)
            {
                currentHealthBonus = 0;
            }
            else
            {
                int bonusLevelCount = currentLevel - bonusStartLevel + 1;
                currentHealthBonus = Mathf.Max(0, bonusLevelCount * healthBonusPerLevel);
            }

            EventBus.Publish(new MedicalHealthBonusChangedEvent
            {
                level = currentLevel,
                maxHealthBonus = currentHealthBonus
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[MedicalHealthBonusController] 의료시설 Lv.{currentLevel} 효과 적용\n" +
                    $"최대 체력 보너스: +{currentHealthBonus}",
                    this
                );
            }
        }

#if UNITY_EDITOR
        [ContextMenu("의료시설 효과 다시 계산")]
        private void DebugRecalculate()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[MedicalHealthBonusController] 플레이 중에만 다시 계산할 수 있습니다.", this);
                return;
            }

            RecalculateAndPublish();
        }
#endif
    }
}
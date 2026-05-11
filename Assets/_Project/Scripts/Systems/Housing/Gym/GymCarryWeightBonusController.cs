using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GymFacility))]
    public sealed class GymCarryWeightBonusController : NetworkBehaviour
    {
        [Header("대상 헬스장")]
        [SerializeField]
        private GymFacility gymFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(1)]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0f)]
        private float carryWeightBonusPerLevel = 7.5f;

        [Header("런타임 확인")]
        [SerializeField]
        private float currentCarryWeightBonusKg;

        [Header("로그")]
        [SerializeField]
        private bool logBonusChanged = true;

        public float CurrentCarryWeightBonusKg => currentCarryWeightBonusKg;

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

            if (gymFacility == null)
            {
                Debug.LogWarning("[GymCarryWeightBonusController] GymFacility가 없어 헬스장 효과를 적용할 수 없습니다.", this);
                return;
            }

            gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            gymFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;

            RecalculateAndPublish();
        }

        public override void OnNetworkDespawn()
        {
            if (gymFacility != null)
                gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void FindRequiredComponents()
        {
            if (gymFacility == null)
                gymFacility = GetComponent<GymFacility>();
        }

        private void HandleFacilityLevelChanged(int previousLevel, int currentLevel)
        {
            RecalculateAndPublish();
        }

        public void RecalculateAndPublish()
        {
            if (!IsServer)
                return;

            if (gymFacility == null)
                return;

            int currentLevel = gymFacility.GetCurrentLevel();

            if (currentLevel < bonusStartLevel)
            {
                currentCarryWeightBonusKg = 0f;
            }
            else
            {
                int bonusLevelCount = currentLevel - bonusStartLevel + 1;
                currentCarryWeightBonusKg = Mathf.Max(0f, bonusLevelCount * carryWeightBonusPerLevel);
            }

            EventBus.Publish(new GymCarryWeightBonusChangedEvent
            {
                level = currentLevel,
                carryWeightBonusKg = currentCarryWeightBonusKg
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[GymCarryWeightBonusController] 헬스장 Lv.{currentLevel} 효과 적용\n" +
                    $"최대 소지 무게 보너스: +{currentCarryWeightBonusKg:0.##}kg",
                    this
                );
            }
        }

#if UNITY_EDITOR
        [ContextMenu("헬스장 효과 다시 계산")]
        private void DebugRecalculate()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GymCarryWeightBonusController] 플레이 중에만 다시 계산할 수 있습니다.", this);
                return;
            }

            RecalculateAndPublish();
        }
#endif
    }
}
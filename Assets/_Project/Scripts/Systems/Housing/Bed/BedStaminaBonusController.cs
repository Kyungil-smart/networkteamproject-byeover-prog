using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BedRoomFacility))]
    public sealed class BedStaminaBonusController : NetworkBehaviour
    {
        [Header("대상 침대")]
        [SerializeField]
        private BedRoomFacility bedFacility;

        [Header("보너스 설정")]
        [SerializeField]
        [Min(1)]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        private int staminaBonusPerLevel = 5;

        [Header("런타임 확인")]
        [SerializeField]
        private int currentMaxStaminaBonus;

        [Header("로그")]
        [SerializeField]
        private bool logBonusChanged = true;

        public int CurrentMaxStaminaBonus => currentMaxStaminaBonus;

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

            if (bedFacility == null)
            {
                Debug.LogWarning("[BedStaminaBonusController] BedRoomFacility가 없어 침대 효과를 적용할 수 없습니다.", this);
                return;
            }

            bedFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            bedFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;

            RecalculateAndPublish();
        }

        public override void OnNetworkDespawn()
        {
            if (bedFacility != null)
                bedFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void FindRequiredComponents()
        {
            if (bedFacility == null)
                bedFacility = GetComponent<BedRoomFacility>();
        }

        private void HandleFacilityLevelChanged(int previousLevel, int currentLevel)
        {
            RecalculateAndPublish();
        }

        public void RecalculateAndPublish()
        {
            if (!IsServer)
                return;

            if (bedFacility == null)
                return;

            int currentLevel = bedFacility.GetCurrentLevel();

            if (currentLevel < bonusStartLevel)
            {
                currentMaxStaminaBonus = 0;
            }
            else
            {
                int bonusLevelCount = currentLevel - bonusStartLevel + 1;
                currentMaxStaminaBonus = Mathf.Max(0, bonusLevelCount * staminaBonusPerLevel);
            }

            EventBus.Publish(new BedStaminaBonusChangedEvent
            {
                level = currentLevel,
                maxStaminaBonus = currentMaxStaminaBonus
            });

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[BedStaminaBonusController] 침대 Lv.{currentLevel} 효과 적용\n" +
                    $"최대 스태미너 보너스: +{currentMaxStaminaBonus}",
                    this
                );
            }
        }

#if UNITY_EDITOR
        [ContextMenu("침대 효과 다시 계산")]
        private void DebugRecalculate()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[BedStaminaBonusController] 플레이 중에만 다시 계산할 수 있습니다.", this);
                return;
            }

            RecalculateAndPublish();
        }
#endif
    }
}
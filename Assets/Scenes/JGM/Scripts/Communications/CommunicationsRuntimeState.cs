using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 통신장비 레벨에 따른 런타임 보너스 상태를 관리합니다.
    /// PlayerStats, UI, Trader를 직접 참조하지 않고 EventBus로 변경 사실만 알립니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CommunicationsFacility))]
    public class CommunicationsRuntimeState : NetworkBehaviour
    {
        [Header("통신장비")]
        [SerializeField]
        [Tooltip("보너스 계산에 사용할 통신장비 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private CommunicationsFacility communicationsFacility;

        [SerializeField]
        [Tooltip("통신장비 레벨별 퀘스트 해금 및 보너스 설정 SO입니다.")]
        private CommunicationsLevelConfigSO levelConfig;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public NetworkVariable<int> CurrentCommunicationLevel = new(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> ExperienceBonusPercent = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> DetectionResistancePercent = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> TraderDiscountPercent = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

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
            SubscribeNetworkValueChanged();
            RefreshRuntimeState(true);
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
            UnsubscribeNetworkValueChanged();
        }

        public override void OnNetworkSpawn()
        {
            RefreshRuntimeState(true);
        }

        private void OnValidate()
        {
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (communicationsFacility == null)
                communicationsFacility = GetComponent<CommunicationsFacility>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (communicationsFacility == null)
                return;

            communicationsFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            communicationsFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (communicationsFacility == null)
                return;

            communicationsFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void SubscribeNetworkValueChanged()
        {
            CurrentCommunicationLevel.OnValueChanged -= HandleNetworkBonusChanged;
            ExperienceBonusPercent.OnValueChanged -= HandleNetworkBonusChanged;
            DetectionResistancePercent.OnValueChanged -= HandleNetworkBonusChanged;
            TraderDiscountPercent.OnValueChanged -= HandleNetworkBonusChanged;

            CurrentCommunicationLevel.OnValueChanged += HandleNetworkBonusChanged;
            ExperienceBonusPercent.OnValueChanged += HandleNetworkBonusChanged;
            DetectionResistancePercent.OnValueChanged += HandleNetworkBonusChanged;
            TraderDiscountPercent.OnValueChanged += HandleNetworkBonusChanged;
        }

        private void UnsubscribeNetworkValueChanged()
        {
            CurrentCommunicationLevel.OnValueChanged -= HandleNetworkBonusChanged;
            ExperienceBonusPercent.OnValueChanged -= HandleNetworkBonusChanged;
            DetectionResistancePercent.OnValueChanged -= HandleNetworkBonusChanged;
            TraderDiscountPercent.OnValueChanged -= HandleNetworkBonusChanged;
        }

        private void HandleFacilityLevelChanged(int previousLevel, int newLevel)
        {
            RefreshRuntimeState(false);
        }

        private void HandleNetworkBonusChanged(int previousValue, int newValue)
        {
            PublishCurrentBonus(false);
        }

        public void RefreshRuntimeState()
        {
            RefreshRuntimeState(false);
        }

        public void RefreshRuntimeState(bool forceLog)
        {
            if (!IsValidSetup())
                return;

            int facilityLevel = Mathf.Max(1, communicationsFacility.CurrentLevel.Value);
            CommunicationsLevelConfig config = levelConfig.GetLevel(facilityLevel);

            if (config == null)
            {
                Debug.LogWarning($"[CommunicationsRuntimeState] Lv.{facilityLevel} 통신장비 설정이 없습니다.", this);
                return;
            }

            bool changed =
                CurrentCommunicationLevel.Value != config.Level ||
                ExperienceBonusPercent.Value != config.ExperienceBonusPercent ||
                DetectionResistancePercent.Value != config.DetectionResistancePercent ||
                TraderDiscountPercent.Value != config.TraderDiscountPercent;

            if (CanWriteNetworkState())
            {
                CurrentCommunicationLevel.Value = config.Level;
                ExperienceBonusPercent.Value = config.ExperienceBonusPercent;
                DetectionResistancePercent.Value = config.DetectionResistancePercent;
                TraderDiscountPercent.Value = config.TraderDiscountPercent;
            }

            if (changed || forceLog)
                PublishBonus(config, forceLog);
        }

        private void PublishCurrentBonus(bool forceLog)
        {
            if (levelConfig == null)
                return;

            CommunicationsLevelConfig config = levelConfig.GetLevel(CurrentCommunicationLevel.Value);

            if (config == null)
                return;

            PublishBonus(config, forceLog);
        }

        private void PublishBonus(CommunicationsLevelConfig config, bool forceLog)
        {
            if (config == null)
                return;

            EventBus.Publish(new CommunicationsBonusChangedEvent
            {
                level = config.Level,
                unlockedQuestStartId = ToFixedString64(config.UnlockedQuestStartId),
                unlockedQuestEndId = ToFixedString64(config.UnlockedQuestEndId),
                experienceBonusPercent = ExperienceBonusPercent.Value,
                detectionResistancePercent = DetectionResistancePercent.Value,
                traderDiscountPercent = TraderDiscountPercent.Value
            });

            if (logBonusChanged || forceLog)
            {
                Debug.Log(
                    $"[CommunicationsRuntimeState] Lv.{config.Level} 적용 완료\n" +
                    $"해금 퀘스트: {config.GetQuestRangeText()}\n" +
                    $"경험치 보너스: +{ExperienceBonusPercent.Value}%\n" +
                    $"감지 저항: +{DetectionResistancePercent.Value}%\n" +
                    $"트레이더 할인: {TraderDiscountPercent.Value}%",
                    this
                );
            }
        }

        private bool IsValidSetup()
        {
            if (communicationsFacility == null)
            {
                Debug.LogWarning("[CommunicationsRuntimeState] CommunicationsFacility가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (communicationsFacility.Type != FacilityType.CommStation)
            {
                Debug.LogWarning(
                    $"[CommunicationsRuntimeState] 연결된 시설 타입이 CommStation이 아닙니다. 현재 타입: {communicationsFacility.Type}",
                    this
                );
                return false;
            }

            if (levelConfig == null)
            {
                Debug.LogWarning("[CommunicationsRuntimeState] CommunicationsLevelConfigSO가 연결되어 있지 않습니다.", this);
                return false;
            }

            return true;
        }

        private bool CanWriteNetworkState()
        {
            if (IsServer)
                return true;

#if UNITY_EDITOR
            if (NetworkManager.Singleton == null)
                return true;

            return !NetworkManager.Singleton.IsListening;
#else
            return false;
#endif
        }

        private static FixedString64Bytes ToFixedString64(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? default
                : new FixedString64Bytes(value);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 통신장비 보너스 다시 계산")]
        private void DebugRefreshRuntimeState()
        {
            RefreshRuntimeState(true);
        }
#endif
    }
}

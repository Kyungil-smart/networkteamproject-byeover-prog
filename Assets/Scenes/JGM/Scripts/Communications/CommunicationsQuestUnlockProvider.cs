using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 통신장비 레벨과 완료 퀘스트 상태를 기준으로 퀘스트 해금 여부를 판단합니다.
    /// QuestManager, UI, Player를 직접 제어하지 않고 조건 판단만 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CommunicationsFacility))]
    public class CommunicationsQuestUnlockProvider : MonoBehaviour, IQuestAvailabilityProvider
    {
        [Header("통신장비")]
        [SerializeField]
        [Tooltip("퀘스트 해금 조건에 사용할 통신장비 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private CommunicationsFacility communicationsFacility;

        [SerializeField]
        [Tooltip("통신장비 레벨별 퀘스트 해금 및 보너스 설정 SO입니다.")]
        private CommunicationsLevelConfigSO levelConfig;

        [Header("퀘스트 완료 상태")]
        [SerializeField]
        [Tooltip("IQuestCompletionReader를 구현한 컴포넌트를 넣습니다. 테스트 단계에서는 CommunicationsQuestTestState를 넣습니다.")]
        private MonoBehaviour questCompletionReaderBehaviour;

        [Header("로그")]
        [SerializeField]
        [Tooltip("퀘스트 해금 검사 결과를 Console에 출력할지 여부입니다.")]
        private bool logResult = true;

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

        private void FindRequiredComponents()
        {
            if (communicationsFacility == null)
                communicationsFacility = GetComponent<CommunicationsFacility>();

            if (questCompletionReaderBehaviour == null)
                questCompletionReaderBehaviour = GetComponent<CommunicationsQuestTestState>();
        }

        public bool CanAcceptQuest(string questId, out string failReason)
        {
            failReason = string.Empty;

            if (string.IsNullOrWhiteSpace(questId))
            {
                failReason = "검사할 퀘스트 ID가 비어 있습니다.";
                return false;
            }

            if (!IsValidSetup(out failReason))
                return false;

            if (!levelConfig.TryGetLevelByQuest(questId, out CommunicationsLevelConfig requiredLevelConfig))
            {
                failReason = $"{questId}는 통신장비 해금 목록에 없습니다.";
                return false;
            }

            int currentLevel = GetCurrentLevel();

            if (currentLevel < requiredLevelConfig.Level)
            {
                failReason = $"{questId} 수락 불가: 통신장비 Lv.{requiredLevelConfig.Level} 필요 / 현재 Lv.{currentLevel}";
                return false;
            }

            if (!IsRequiredQuestCompleted(requiredLevelConfig, out failReason))
                return false;

            failReason = string.Empty;
            return true;
        }

        public bool CanUpgradeToNextLevel(out int nextLevel, out string failReason)
        {
            nextLevel = 0;
            failReason = string.Empty;

            if (!IsValidSetup(out failReason))
                return false;

            int currentLevel = GetCurrentLevel();
            nextLevel = currentLevel + 1;

            CommunicationsLevelConfig nextLevelConfig = levelConfig.GetLevel(nextLevel);

            if (nextLevelConfig == null)
            {
                failReason = "통신장비가 이미 최대 레벨이거나 다음 레벨 설정이 없습니다.";
                return false;
            }

            if (!IsRequiredQuestCompleted(nextLevelConfig, out failReason))
                return false;

            return true;
        }

        public CommunicationsLevelConfig GetCurrentLevelConfig()
        {
            if (levelConfig == null)
                return null;

            return levelConfig.GetLevel(GetCurrentLevel());
        }

        public int GetCurrentLevel()
        {
            if (communicationsFacility == null)
                return 1;

            return Mathf.Max(1, communicationsFacility.CurrentLevel.Value);
        }

        private bool IsRequiredQuestCompleted(CommunicationsLevelConfig config, out string failReason)
        {
            failReason = string.Empty;

            if (config == null)
            {
                failReason = "통신장비 레벨 설정이 없습니다.";
                return false;
            }

            if (!config.HasRequiredCompletedQuest)
                return true;

            string requiredQuestId = config.RequiredCompletedQuestId;

            if (TryReadQuestCompleted(requiredQuestId, out bool isCompleted))
            {
                if (isCompleted)
                    return true;

                failReason = $"통신장비 Lv.{config.Level} 조건 미충족: {requiredQuestId} 완료 필요";
                return false;
            }

            failReason = $"{requiredQuestId} 완료 여부를 확인할 IQuestCompletionReader가 없습니다.";
            return false;
        }

        private bool TryReadQuestCompleted(string questId, out bool isCompleted)
        {
            isCompleted = false;

            if (questCompletionReaderBehaviour is IQuestCompletionReader reader)
            {
                isCompleted = reader.IsQuestCompleted(questId);
                return true;
            }

            QuestManager questManager = ServiceLocator.Get<QuestManager>();

            if (questManager != null)
            {
                isCompleted = questManager.IsQuestCompleted(questId);
                return true;
            }

            return false;
        }

        private bool IsValidSetup(out string failReason)
        {
            failReason = string.Empty;

            if (communicationsFacility == null)
            {
                failReason = "CommunicationsFacility가 연결되어 있지 않습니다.";
                return false;
            }

            if (communicationsFacility.Type != FacilityType.CommStation)
            {
                failReason = $"연결된 시설 타입이 CommStation이 아닙니다. 현재 타입: {communicationsFacility.Type}";
                return false;
            }

            if (levelConfig == null)
            {
                failReason = "CommunicationsLevelConfigSO가 연결되어 있지 않습니다.";
                return false;
            }

            return true;
        }

        private void Log(string message)
        {
            if (!logResult)
                return;

            Debug.Log($"[CommunicationsQuestUnlockProvider] {message}", this);
        }

        private void LogWarning(string message)
        {
            if (!logResult)
                return;

            Debug.LogWarning($"[CommunicationsQuestUnlockProvider] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 현재 해금 상태 출력")]
        private void DebugPrintUnlockState()
        {
            if (!IsValidSetup(out string failReason))
            {
                LogWarning(failReason);
                return;
            }

            CommunicationsLevelConfig currentConfig = GetCurrentLevelConfig();

            if (currentConfig == null)
            {
                LogWarning($"현재 통신장비 Lv.{GetCurrentLevel()} 설정이 없습니다.");
                return;
            }

            Log($"현재 Lv.{GetCurrentLevel()} / 해금 퀘스트: {currentConfig.GetQuestRangeText()}");
        }

        [ContextMenu("디버그 Q3 수락 가능 여부 확인")]
        private void DebugCanAcceptQ3()
        {
            bool canAccept = CanAcceptQuest("Q3", out string failReason);
            Log(canAccept ? "Q3 수락 가능" : failReason);
        }

        [ContextMenu("디버그 다음 레벨 업그레이드 조건 확인")]
        private void DebugCanUpgradeNextLevel()
        {
            bool canUpgrade = CanUpgradeToNextLevel(out int nextLevel, out string failReason);
            Log(canUpgrade ? $"Lv.{nextLevel} 업그레이드 조건 충족" : failReason);
        }
#endif
    }
}

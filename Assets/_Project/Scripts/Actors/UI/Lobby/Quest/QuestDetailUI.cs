using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors.UI.Lobby
{
    public class QuestDetailUI : MonoBehaviour
    {
        [Header("오른쪽 상세 텍스트")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text objectivesText;
        [SerializeField] private TMP_Text rewardText;

        [Header("수락 버튼")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private TMP_Text acceptButtonText;

        private QuestDataSO currentQuest;
        private Action<QuestDataSO> onAcceptClicked;

        private void Awake()
        {
            if (acceptButton != null)
            {
                acceptButton.onClick.RemoveListener(HandleAcceptClicked);
                acceptButton.onClick.AddListener(HandleAcceptClicked);
            }
        }

        public void Show(
            QuestDataSO quest,
            QuestViewState state,
            string progressText,
            Action<QuestDataSO> acceptCallback)
        {
            currentQuest = quest;
            onAcceptClicked = acceptCallback;

            if (quest == null)
            {
                Clear();
                return;
            }

            if (titleText != null)
                titleText.text = quest.questName;

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrWhiteSpace(quest.description)
                    ? "퀘스트 설명이 없습니다."
                    : quest.description;

            if (objectivesText != null)
                objectivesText.text = BuildObjectivesText(quest, progressText);

            if (rewardText != null)
                rewardText.text = BuildRewardsText(quest);

            RefreshButton(state);
        }

        public void Clear()
        {
            currentQuest = null;
            onAcceptClicked = null;

            if (titleText != null)
                titleText.text = "퀘스트 선택";

            if (descriptionText != null)
                descriptionText.text = "왼쪽 목록에서 퀘스트를 선택하세요.";

            if (objectivesText != null)
                objectivesText.text = string.Empty;

            if (rewardText != null)
                rewardText.text = string.Empty;

            if (acceptButton != null)
                acceptButton.interactable = false;

            if (acceptButtonText != null)
                acceptButtonText.text = "-";
        }

        private void RefreshButton(QuestViewState state)
        {
            if (acceptButtonText != null)
                acceptButtonText.text = GetButtonText(state);

            if (acceptButton != null)
                acceptButton.interactable = state == QuestViewState.Available;
        }

        private void HandleAcceptClicked()
        {
            if (currentQuest == null) return;
            onAcceptClicked?.Invoke(currentQuest);
        }

        private static string BuildObjectivesText(QuestDataSO quest, string progressText)
        {
            if (quest.objectives == null || quest.objectives.Length == 0)
                return "목표\n- 목표 데이터 없음";

            StringBuilder sb = new();
            sb.AppendLine("목표");

            string[] progressLines = string.IsNullOrWhiteSpace(progressText)
                ? Array.Empty<string>()
                : progressText.Split('\n');

            for (int i = 0; i < quest.objectives.Length; i++)
            {
                QuestObjectiveData objective = quest.objectives[i];
                string label = ToObjectiveText(objective);

                if (i < progressLines.Length && !string.IsNullOrWhiteSpace(progressLines[i]))
                    sb.AppendLine($"- {label} ({progressLines[i]})");
                else
                    sb.AppendLine($"- {label}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildRewardsText(QuestDataSO quest)
        {
            if (quest.rewards == null || quest.rewards.Length == 0)
                return "- 보상 없음";

            StringBuilder sb = new();

            foreach (QuestReward reward in quest.rewards)
            {
                string rewardText = reward.type switch
                {
                    RewardType.Credits => $"{reward.amount} Cr",
                    RewardType.Item => $"{reward.itemID} x{reward.amount}",
                    RewardType.FacilityMaterial => $"{reward.itemID} x{reward.amount}",
                    _ => $"{reward.itemID} x{reward.amount}"
                };

                sb.AppendLine($"- {rewardText}");
            }

            if (!string.IsNullOrWhiteSpace(quest.unlockZoneID))
                sb.AppendLine($"- 해금: {quest.unlockZoneID}");

            return sb.ToString().TrimEnd();
        }

        private static string ToObjectiveText(QuestObjectiveData objective)
        {
            string target = string.IsNullOrWhiteSpace(objective.targetID)
                ? "대상"
                : objective.targetID;

            string location = string.IsNullOrWhiteSpace(objective.location)
                ? string.Empty
                : $" / {objective.location}";

            return objective.type switch
            {
                ObjectiveType.Kill => $"{target} 처치 {objective.requiredCount}회{location}",
                ObjectiveType.Collect => $"{target} 수집 {objective.requiredCount}개{location}",
                ObjectiveType.Reach => $"{target} 도달{location}",
                _ => $"{target} {objective.requiredCount}회{location}"
            };
        }

        private static string GetButtonText(QuestViewState state)
        {
            return state switch
            {
                QuestViewState.Locked => "잠김",
                QuestViewState.Available => "수락",
                QuestViewState.Active => "진행중",
                QuestViewState.Completed => "완료",
                _ => "-"
            };
        }

        private void OnDestroy()
        {
            if (acceptButton != null)
                acceptButton.onClick.RemoveListener(HandleAcceptClicked);
        }
    }
}
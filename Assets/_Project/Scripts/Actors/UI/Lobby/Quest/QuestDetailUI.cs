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
        [Header("상세 텍스트")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text objectivesText;
        [SerializeField] private TMP_Text rewardText;

        [Header("액션 버튼")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private TMP_Text acceptButtonText;

        private QuestDataSO currentQuest;
        private Action<QuestDataSO> onAcceptClicked;
        private Action<QuestDataSO> onClaimRewardClicked;
        private bool canClaimReward;

        private void Awake()
        {
            if (acceptButton != null)
            {
                acceptButton.onClick.RemoveListener(HandleActionClicked);
                acceptButton.onClick.AddListener(HandleActionClicked);
            }
        }

        public void Show(
            QuestDataSO quest,
            QuestViewState state,
            string progressText,
            Action<QuestDataSO> acceptCallback,
            Action<QuestDataSO> claimRewardCallback = null,
            bool canClaimReward = false,
            bool rewardClaimed = false)
        {
            currentQuest = quest;
            onAcceptClicked = acceptCallback;
            onClaimRewardClicked = claimRewardCallback;
            this.canClaimReward = canClaimReward;

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

            RefreshButton(state, canClaimReward, rewardClaimed);
        }

        public void Clear()
        {
            currentQuest = null;
            onAcceptClicked = null;
            onClaimRewardClicked = null;
            canClaimReward = false;

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

        private void RefreshButton(QuestViewState state, bool canClaimReward, bool rewardClaimed)
        {
            if (acceptButtonText != null)
                acceptButtonText.text = GetButtonText(state, canClaimReward, rewardClaimed);

            if (acceptButton != null)
                acceptButton.interactable = state == QuestViewState.Available || canClaimReward;
        }

        private void HandleActionClicked()
        {
            if (currentQuest == null)
                return;

            if (canClaimReward)
            {
                canClaimReward = false;
                if (acceptButton != null)
                    acceptButton.interactable = false;

                onClaimRewardClicked?.Invoke(currentQuest);
            }
            else
            {
                onAcceptClicked?.Invoke(currentQuest);
            }
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
                string text = reward.type switch
                {
                    RewardType.Credits => $"{reward.amount} Cr",
                    RewardType.Item => $"{reward.itemID} x{reward.amount}",
                    RewardType.FacilityMaterial => $"{reward.itemID} x{reward.amount}",
                    _ => $"{reward.itemID} x{reward.amount}"
                };

                sb.AppendLine($"- {text}");
            }

            if (!string.IsNullOrWhiteSpace(quest.unlockZoneID))
                sb.AppendLine($"- 해금: {quest.unlockZoneID}");

            return sb.ToString().TrimEnd();
        }

        private static string ToObjectiveText(QuestObjectiveData objective)
        {
            string target = GetObjectiveTargetDisplayName(objective.targetID);
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

        private static string GetObjectiveTargetDisplayName(string targetID)
        {
            if (string.IsNullOrWhiteSpace(targetID))
                return "대상";

            return targetID switch
            {
                "Enemy_Any" => "아무 지역의 적",
                "Enemy_Zone1_Any" => "아무 지역의 적",
                "Boss_PowerPlant" => "발전 시설 보스",
                "Boss_Warehouse" => "창고 지역 보스",
                "Boss_MilitaryBase" => "군사 지역 보스",
                "Boss_Forest_Sniper" => "숲 지역 보스",
                "Boss_Sawmill" => "제재소 지역 보스",
                "Boss_S2_01" => "제재소 지역 보스",
                "Boss_Stage2_All" => "엔딩 지역 보스",
                "Truck" => "Ending 지역",
                _ => targetID
            };
        }

        private static string GetButtonText(QuestViewState state, bool canClaimReward, bool rewardClaimed)
        {
            if (rewardClaimed)
                return "수령 완료";

            if (canClaimReward)
                return "보상 받기";

            return state switch
            {
                QuestViewState.Locked => "잠김",
                QuestViewState.Available => "수락",
                QuestViewState.Active => "진행 중",
                QuestViewState.Completed => "보상 받기",
                QuestViewState.Claimed => "수령 완료",
                _ => "-"
            };
        }

        private void OnDestroy()
        {
            if (acceptButton != null)
                acceptButton.onClick.RemoveListener(HandleActionClicked);
        }
    }
}

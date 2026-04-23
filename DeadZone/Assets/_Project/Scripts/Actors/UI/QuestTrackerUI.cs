using TMPro;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 좌상단 현재 퀘스트 목표 표시. Canvas_RaidHUD > QuestTracker_Panel에 부착된다.
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text questNameText;
        [SerializeField] private TMP_Text progressText;

        private void OnEnable()
        {
            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnQuestAccepted(QuestAcceptedEvent e)
        {
            if (questNameText != null) questNameText.text = e.questId.ToString();
            if (progressText != null) progressText.text = "0 / ?";
        }

        private void OnQuestProgress(QuestProgressEvent e)
        {
            if (progressText != null) progressText.text = $"{e.currentCount} / {e.requiredCount}";
        }

        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (questNameText != null) questNameText.text = $"Completed: {e.questId}";
        }
    }
}

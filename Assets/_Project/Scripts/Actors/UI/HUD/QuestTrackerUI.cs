using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Quests;

namespace DeadZone.Actors
{
    public class QuestTrackerUI : MonoBehaviour
    {
        private const ulong StandaloneClientId = 0;
        private const string PendingExtractionMessage = "탈출 지점으로 가서 탈출 하세요";

        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text questNameText;

        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text progressText;

        [BoxGroup("참조")]
        [SerializeField] private GameObject panelRoot;

        [BoxGroup("설정")]
        [PropertyRange(0.5f, 0.99f), SerializeField] private float nearCompleteThreshold = 0.8f;

        [BoxGroup("설정")]
        [SerializeField] private string defaultText = "퀘스트";

        [FoldoutGroup("피드백")]
        [SerializeField] private MMF_Player onQuestAcceptedFeedback;

        [FoldoutGroup("피드백")]
        [SerializeField] private MMF_Player onQuestProgressFeedback;

        [FoldoutGroup("피드백")]
        [SerializeField] private MMF_Player onNearCompleteFeedback;

        [FoldoutGroup("피드백")]
        [SerializeField] private MMF_Player onQuestCompletedFeedback;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private int currentCount;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private int requiredCount;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool nearCompleteTriggered;

        private CanvasGroup panelCanvasGroup;
        private IQuestQuery questQuery;

        private void Awake()
        {
            if (panelRoot == null)
                panelRoot = gameObject;

            panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();

            HidePanel();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Subscribe<QuestTrackerSnapshotEvent>(OnQuestTrackerSnapshot);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestTrackerSnapshotEvent>(OnQuestTrackerSnapshot);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnQuestAccepted(QuestAcceptedEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            currentCount = 0;
            requiredCount = 0;
            nearCompleteTriggered = false;

            RenderQuestById(e.questId.ToString(), e.clientId, true);
            UIFeedbackTester.Play(onQuestAcceptedFeedback, this, "퀘스트 수락");
        }

        private void OnQuestProgress(QuestProgressEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            RenderQuestTitle(e.questId.ToString());

            currentCount = e.currentCount;
            requiredCount = e.requiredCount;
            if (progressText != null)
                progressText.text = $"{e.currentCount} / {e.requiredCount}";

            UIFeedbackTester.Play(onQuestProgressFeedback, this, "퀘스트 진행");

            if (!nearCompleteTriggered && requiredCount > 0)
            {
                float ratio = (float)currentCount / requiredCount;
                if (ratio >= nearCompleteThreshold && ratio < 1f)
                {
                    UIFeedbackTester.Play(onNearCompleteFeedback, this, "퀘스트 거의 완료");
                    nearCompleteTriggered = true;
                }
            }
        }

        private void OnQuestTrackerSnapshot(QuestTrackerSnapshotEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            RenderQuestTitle(e.questId.ToString());
            currentCount = e.currentCount;
            requiredCount = e.requiredCount;
            nearCompleteTriggered = requiredCount > 0 &&
                                    (float)currentCount / requiredCount >= nearCompleteThreshold;

            if (progressText != null)
                progressText.text = e.isPendingCompletion
                    ? PendingExtractionMessage
                    : $"{e.currentCount} / {e.requiredCount}";
        }

        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            if (questNameText != null)
                questNameText.text = $"완료: {GetQuestDisplayName(e.questId.ToString())}";

            if (progressText != null)
                progressText.text = PendingExtractionMessage;

            UIFeedbackTester.Play(onQuestCompletedFeedback, this, "퀘스트 완료");
            ShowPanel();
        }

        private void RenderQuestById(string questId, ulong clientId, bool useFirstObjectiveProgress)
        {
            RenderQuestTitle(questId);

            if (!useFirstObjectiveProgress ||
                !TryResolveQuestData(questId, out QuestDataSO questData) ||
                questData.objectives == null ||
                questData.objectives.Length == 0)
            {
                if (progressText != null)
                    progressText.text = "0 / ?";

                ShowPanel();
                return;
            }

            QuestObjectiveData objective = questData.objectives[0];
            int current = ResolveQuestQuery()?.GetObjectiveProgress(clientId, questId, objective.targetID) ?? 0;

            currentCount = current;
            requiredCount = objective.requiredCount;

            if (progressText != null)
                progressText.text = $"{current} / {objective.requiredCount}";

            ShowPanel();
        }

        private void RenderQuestTitle(string questId)
        {
            if (questNameText != null)
                questNameText.text = GetQuestDisplayName(questId);

            ShowPanel();
        }

        private string GetQuestDisplayName(string questId)
        {
            if (TryResolveQuestData(questId, out QuestDataSO questData) &&
                !string.IsNullOrWhiteSpace(questData.questName))
            {
                return questData.questName;
            }

            return string.IsNullOrWhiteSpace(questId) ? defaultText : questId;
        }

        private bool TryResolveQuestData(string questId, out QuestDataSO questData)
        {
            IQuestQuery query = ResolveQuestQuery();
            if (query == null)
            {
                questData = null;
                return false;
            }

            return query.TryGetQuestData(questId, out questData);
        }

        private IQuestQuery ResolveQuestQuery()
        {
            questQuery ??= ServiceLocator.Get<IQuestQuery>();
            return questQuery;
        }

        private static bool IsLocalQuestEvent(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return clientId == StandaloneClientId;

            return clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void ShowPanel()
        {
            if (panelRoot != null && !panelRoot.activeSelf)
                panelRoot.SetActive(true);

            if (panelCanvasGroup == null) return;
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        private void HidePanel()
        {
            if (panelCanvasGroup == null) return;
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("퀘스트 수락 피드백"), GUIColor(0.7f, 0.9f, 1f)]
        private void TestAccepted() => UIFeedbackTester.Play(onQuestAcceptedFeedback, this, "퀘스트 수락");

        [TitleGroup("디버그")]
        [Button("퀘스트 진행 피드백")]
        private void TestProgress() => UIFeedbackTester.Play(onQuestProgressFeedback, this, "퀘스트 진행");

        [TitleGroup("디버그")]
        [Button("거의 완료 피드백"), GUIColor(1f, 0.9f, 0.5f)]
        private void TestNearComplete() => UIFeedbackTester.Play(onNearCompleteFeedback, this, "거의 완료");

        [TitleGroup("디버그")]
        [Button("퀘스트 완료 피드백"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestCompleted() => UIFeedbackTester.Play(onQuestCompletedFeedback, this, "퀘스트 완료");
#endif
    }
}

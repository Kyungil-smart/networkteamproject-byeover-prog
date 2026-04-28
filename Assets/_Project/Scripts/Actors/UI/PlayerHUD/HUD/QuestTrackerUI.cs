using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 현재 진행중인 퀘스트 목표 표시 UI
// 퀘스트 이름과 진행도(현재/필요) 표시, 진행 이벤트마다 피드백 재생
// EventBus로 QuestAccepted / QuestProgress / QuestCompleted 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// 좌상단 현재 퀘스트 목표 표시
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour
    {
        // UI 레퍼런스
        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text questNameText;// 퀘스트 이름 텍스트

        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text progressText;// 진행도 텍스트 (예: "3 / 10")

        [BoxGroup("References")]
        [Tooltip("퀘스트가 없을 때 숨길 루트, 비워두면 항상 보임")]
        [SerializeField] private GameObject panelRoot;

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("이 비율 이상 진행 시 '거의 완료' 피드백이 1회 재생됨 (0.5 ~ 0.99)")]
        [PropertyRange(0.5f, 0.99f), SerializeField] private float nearCompleteThreshold = 0.8f;

        // Feel 피드백
        [FoldoutGroup("Feedbacks")]
        [Tooltip("퀘스트 신규 수락 시 재생")]
        [SerializeField] private MMF_Player onQuestAcceptedFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("카운트가 1 올라갈 때마다 재생")]
        [SerializeField] private MMF_Player onQuestProgressFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("진행률이 nearCompleteThreshold를 처음 넘었을 때 1회 재생")]
        [SerializeField] private MMF_Player onNearCompleteFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("퀘스트 완료 시 재생")]
        [SerializeField] private MMF_Player onQuestCompletedFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private int currentCount;// 현재 진행 카운트
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private int requiredCount;// 필요 카운트
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool nearCompleteTriggered;// 거의완료 피드백 중복 방지
        private CanvasGroup panelCanvasGroup;

        private void Awake()
        {
            if (panelRoot == null)
                panelRoot = gameObject;

            panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();

            HidePanel();
        }

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        // 퀘스트 수락 시 텍스트 초기화 + 패널 활성화
        private void OnQuestAccepted(QuestAcceptedEvent e)
        {
            Debug.Log($"[QuestTrackerUI] QuestAccepted id={e.questId}", this);

            if (questNameText != null) questNameText.text = e.questId.ToString();
            if (progressText != null)  progressText.text = "0 / ?";
            ShowPanel();

            // 새 퀘스트 사이클이므로 상태값 리셋
            currentCount = 0;
            requiredCount = 0;
            nearCompleteTriggered = false;

            UIFeedbackTester.Play(onQuestAcceptedFeedback, this, "퀘스트 수락");
        }

        // 퀘스트 진행도 갱신 + 진행 피드백 재생
        private void OnQuestProgress(QuestProgressEvent e)
        {
            Debug.Log($"[QuestTrackerUI] QuestProgress id={e.questId}, count={e.currentCount}/{e.requiredCount}", this);

            currentCount = e.currentCount;
            requiredCount = e.requiredCount;

            if (progressText != null)
                progressText.text = $"{e.currentCount} / {e.requiredCount}";

            UIFeedbackTester.Play(onQuestProgressFeedback, this, "퀘스트 진행");

            // 거의완료 구간 진입 순간 1회만 피드백 재생 (edge trigger)
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

        // 퀘스트 완료 시 텍스트 변경 + 완료 피드백 재생
        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            Debug.Log($"[QuestTrackerUI] QuestCompleted id={e.questId}", this);

            if (questNameText != null) questNameText.text = $"Completed: {e.questId}";
            UIFeedbackTester.Play(onQuestCompletedFeedback, this, "퀘스트 완료");
            HidePanel();
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

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("퀘스트 수락 피드백"), GUIColor(0.7f, 0.9f, 1f)]
        private void TestAccepted() => UIFeedbackTester.Play(onQuestAcceptedFeedback, this, "퀘스트 수락");

        [TitleGroup("Debug")]
        [Button("퀘스트 진행 피드백")]
        private void TestProgress() => UIFeedbackTester.Play(onQuestProgressFeedback, this, "퀘스트 진행");

        [TitleGroup("Debug")]
        [Button("거의 완료 피드백"), GUIColor(1f, 0.9f, 0.5f)]
        private void TestNearComplete() => UIFeedbackTester.Play(onNearCompleteFeedback, this, "거의 완료");

        [TitleGroup("Debug")]
        [Button("퀘스트 완료 피드백"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestCompleted() => UIFeedbackTester.Play(onQuestCompletedFeedback, this, "퀘스트 완료");
#endif
    }
}

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
            if (questNameText != null) questNameText.text = e.questId.ToString();
            if (progressText != null)  progressText.text = "0 / ?";
            if (panelRoot != null)     panelRoot.SetActive(true);

            // 새 퀘스트 사이클이므로 상태값 리셋
            currentCount = 0;
            requiredCount = 0;
            nearCompleteTriggered = false;

            onQuestAcceptedFeedback?.PlayFeedbacks();
        }

        // 퀘스트 진행도 갱신 + 진행 피드백 재생
        private void OnQuestProgress(QuestProgressEvent e)
        {
            currentCount = e.currentCount;
            requiredCount = e.requiredCount;

            if (progressText != null)
                progressText.text = $"{e.currentCount} / {e.requiredCount}";

            onQuestProgressFeedback?.PlayFeedbacks();

            // 거의완료 구간 진입 순간 1회만 피드백 재생 (edge trigger)
            if (!nearCompleteTriggered && requiredCount > 0)
            {
                float ratio = (float)currentCount / requiredCount;
                if (ratio >= nearCompleteThreshold && ratio < 1f)
                {
                    onNearCompleteFeedback?.PlayFeedbacks();
                    nearCompleteTriggered = true;
                }
            }
        }

        // 퀘스트 완료 시 텍스트 변경 + 완료 피드백 재생
        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (questNameText != null) questNameText.text = $"Completed: {e.questId}";
            onQuestCompletedFeedback?.PlayFeedbacks();
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(0.7f, 0.9f, 1f)]
        private void TestAccepted() => onQuestAcceptedFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium)]
        private void TestProgress() => onQuestProgressFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.9f, 0.5f)]
        private void TestNearComplete() => onNearCompleteFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(0.6f, 1f, 0.6f)]
        private void TestCompleted() => onQuestCompletedFeedback?.PlayFeedbacks();
#endif
    }
}
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Quests;

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
        private const ulong StandaloneClientId = 0;

        // UI 레퍼런스
        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text questNameText;// 퀘스트 이름 텍스트

        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text progressText;// 진행도 텍스트 (예: "3 / 10")

        [BoxGroup("참조")]
        [Tooltip("퀘스트가 없을 때 숨길 루트, 비워두면 항상 보임")]
        [SerializeField] private GameObject panelRoot;

        // 설정값
        [BoxGroup("설정")]
        [Tooltip("이 비율 이상 진행 시 '거의 완료' 피드백이 1회 재생됨 (0.5 ~ 0.99)")]
        [PropertyRange(0.5f, 0.99f), SerializeField] private float nearCompleteThreshold = 0.8f;
        [Tooltip("퀘스트 정보가 없을 때 표시할 디폴트 텍스트")] 
        [SerializeField] private string defaultText = "퀘스트";

        // Feel 피드백
        [FoldoutGroup("피드백")]
        [Tooltip("퀘스트 신규 수락 시 재생")]
        [SerializeField] private MMF_Player onQuestAcceptedFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("카운트가 1 올라갈 때마다 재생")]
        [SerializeField] private MMF_Player onQuestProgressFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("진행률이 거의 완료 임계치를 처음 넘었을 때 1회 재생")]
        [SerializeField] private MMF_Player onNearCompleteFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("퀘스트 완료 시 재생")]
        [SerializeField] private MMF_Player onQuestCompletedFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private int currentCount;// 현재 진행 카운트
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private int requiredCount;// 필요 카운트
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool nearCompleteTriggered;// 거의완료 피드백 중복 방지
        private CanvasGroup panelCanvasGroup;
        private IQuestQuery questQuery; // 이벤트로 ID만 보내고 내용을 보내주지 않아서 퀘스트 데이터 조회를 찾아오기 위한 quest 캐시

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
            EventBus.Subscribe<QuestTrackerSnapshotEvent>(OnQuestTrackerSnapshot);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestTrackerSnapshotEvent>(OnQuestTrackerSnapshot);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        // 퀘스트 수락 시 텍스트 초기화 + 패널 활성화
        private void OnQuestAccepted(QuestAcceptedEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            Debug.Log($"[QuestTrackerUI] QuestAccepted id={e.questId}", this);

            // 새 퀘스트 사이클이므로 상태값 리셋
            currentCount = 0;
            requiredCount = 0;
            nearCompleteTriggered = false;

            RenderQuestById(e.questId.ToString(), e.clientId, true);
            UIFeedbackTester.Play(onQuestAcceptedFeedback, this, "퀘스트 수락");
        }

        // 퀘스트 진행도 갱신 + 진행 피드백 재생
        private void OnQuestProgress(QuestProgressEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            Debug.Log($"[QuestTrackerUI] QuestProgress id={e.questId}, count={e.currentCount}/{e.requiredCount}", this);

            // 퀘스트 타이틀
            RenderQuestTitle(e.questId.ToString());
            // 진행도
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

        /// <summary>
        /// QuestManager가 저장된 퀘스트 상태를 복원한 뒤 보내는 현재 HUD 표시용 스냅샷을 반영합니다.
        /// 실제 진행 변화가 아니므로 진행 피드백은 재생하지 않고 텍스트와 내부 카운트만 맞춥니다.
        /// </summary>
        private void OnQuestTrackerSnapshot(QuestTrackerSnapshotEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            Debug.Log($"[QuestTrackerUI] QuestSnapshot id={e.questId}, count={e.currentCount}/{e.requiredCount}", this);

            RenderQuestTitle(e.questId.ToString());
            currentCount = e.currentCount;
            requiredCount = e.requiredCount;
            nearCompleteTriggered = requiredCount > 0 &&
                                    (float)currentCount / requiredCount >= nearCompleteThreshold;

            if (progressText != null)
                progressText.text = e.isPendingCompletion
                    ? $"{e.currentCount} / {e.requiredCount} 완료 대기"
                    : $"{e.currentCount} / {e.requiredCount}";
        }

        // 퀘스트 완료 시 텍스트 변경 + 완료 피드백 재생
        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (!IsLocalQuestEvent(e.clientId))
                return;

            Debug.Log($"[QuestTrackerUI] QuestCompleted id={e.questId}", this);

            if (questNameText != null) questNameText.text = $"Completed: {GetQuestDisplayName(e.questId.ToString())}";
            UIFeedbackTester.Play(onQuestCompletedFeedback, this, "퀘스트 완료");
            HidePanel();
        }

        /// <summary>
        /// 이벤트로 전달된 questId를 기준으로 QuestManager에 등록된 퀘스트 데이터를 조회하고 HUD 텍스트를 구성합니다.
        /// 게임 스테이지 씬에 QuestManager가 ServiceLocator에 등록되어 있다는 전제에서 동작합니다.
        /// </summary>
        private void RenderQuestById(string questId, ulong clientId, bool useFirstObjectiveProgress)
        {
            RenderQuestTitle(questId);

            if (!useFirstObjectiveProgress || !TryResolveQuestData(questId, out QuestDataSO questData) ||
                questData.objectives == null || questData.objectives.Length == 0)
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

        /// <summary>
        /// questId를 표시용 이름으로 변환해 퀘스트 제목 텍스트에 반영합니다.
        /// 진행도 이벤트처럼 현재 카운트만 갱신되는 상황에서도 제목이 ID가 아닌 이름으로 유지되도록 사용합니다.
        /// </summary>
        private void RenderQuestTitle(string questId)
        {
            if (questNameText != null)
                questNameText.text = GetQuestDisplayName(questId);

            ShowPanel();
        }

        /// <summary>
        /// QuestManager의 조회 서비스를 통해 questId에 대응하는 QuestDataSO를 찾고, 표시 이름을 반환합니다.
        /// 데이터 조회에 실패하면 UI가 비어 보이지 않도록 questId 또는 기본 텍스트를 fallback으로 사용합니다.
        /// </summary>
        private string GetQuestDisplayName(string questId)
        {
            if (TryResolveQuestData(questId, out QuestDataSO questData) &&
                !string.IsNullOrWhiteSpace(questData.questName))
            {
                return questData.questName;
            }

            return string.IsNullOrWhiteSpace(questId) ? defaultText : questId;
        }

        /// <summary>
        /// ServiceLocator에 등록된 IQuestQuery를 통해 questId 기반 퀘스트 데이터를 조회합니다.
        /// 게임 스테이지 씬에 QuestManager가 등록되어 있지 않거나 questId가 잘못된 경우 false를 반환합니다.
        /// </summary>
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

        /// <summary>
        /// 퀘스트 조회 서비스 참조를 지연 획득합니다.
        /// UI가 QuestManager보다 먼저 활성화될 수 있으므로 최초 실패 후에도 다음 이벤트에서 다시 조회할 수 있게 유지합니다.
        /// </summary>
        private IQuestQuery ResolveQuestQuery()
        {
            questQuery ??= ServiceLocator.Get<IQuestQuery>();
            return questQuery;
        }

        /// <summary>
        /// 멀티플레이 환경에서 다른 플레이어의 퀘스트 이벤트가 현재 HUD에 표시되지 않도록 로컬 플레이어 이벤트만 통과시킵니다.
        /// 네트워크 세션이 없는 테스트 씬에서는 StandaloneClientId만 로컬 이벤트로 간주합니다.
        /// </summary>
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

        // 에디터 전용 테스트 버튼
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

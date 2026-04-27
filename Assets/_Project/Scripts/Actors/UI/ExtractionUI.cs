using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 탈출 존 진입 시 카운트다운 UI
// 탈출까지 남은 시간 표시, 진행도 Fill, 초 단위 틱 피드백
// EventBus로 ExtractionStarted / ExtractionCompleted 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// 탈출 존 진입 시 카운트다운과 진행도를 표시
    /// </summary>
    public class ExtractionUI : MonoBehaviour
    {
        // UI 레퍼런스
        [BoxGroup("References")]
        [Required, SerializeField] private GameObject panelRoot;// 탈출 UI 루트 (진입 시 활성화)

        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text countdownText;// 남은 시간 텍스트

        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text extractionNameText;// 탈출 지점 이름

        [BoxGroup("References")]
        [Required, SerializeField] private Image progressFill;// 진행도 Fill (0 -> 1)

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("이 시간 이하 구간에서 강한 틱 피드백이 매초 재생됨")]
        [MinValue(1), SerializeField] private int finalCountdownSeconds = 3;

        // Feel 피드백
        [FoldoutGroup("Feedbacks")]
        [Tooltip("탈출 카운트다운 시작 시 재생")]
        [SerializeField] private MMF_Player onStartFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("남은 초가 1 줄어들 때마다 재생 (일반 구간)")]
        [SerializeField] private MMF_Player onTickFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("finalCountdownSeconds 이하 구간에서 매초 재생 (긴장 구간)")]
        [SerializeField] private MMF_Player onFinalCountdownTickFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("탈출 완료 시 재생")]
        [SerializeField] private MMF_Player onCompletedFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool active;// 카운트다운 진행 여부
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float timeRemaining;// 현재 남은 시간
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float totalTime;// 총 카운트다운 시간
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private int lastDisplayedSecond = -1;// 직전 프레임에 표시한 초 (틱 감지용)

        // 시작 시 패널 숨기기
        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Subscribe<ExtractionCompletedEvent>(OnCompleted);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Unsubscribe<ExtractionCompletedEvent>(OnCompleted);
        }

        // 탈출 존 진입 시 카운트다운 시작
        private void OnStarted(ExtractionStartedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;

            active = true;
            timeRemaining = e.countdownSeconds;
            totalTime = e.countdownSeconds;
            lastDisplayedSecond = -1;// 첫 프레임에 강제로 갱신 & 틱 판정되도록 초기화

            if (panelRoot != null) panelRoot.SetActive(true);
            if (extractionNameText != null) extractionNameText.text = e.extractionId.ToString();

            onStartFeedback?.PlayFeedbacks();
        }

        // 탈출 완료 시 UI 종료
        private void OnCompleted(ExtractionCompletedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;

            active = false;
            onCompletedFeedback?.PlayFeedbacks();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // 매 프레임 타이머 감소 + UI 갱신 + 초 단위 틱 피드백
        private void Update()
        {
            if (!active) return;

            timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);

            // 텍스트는 소수점 1자리 표시 (3.9, 3.8, ...)
            if (countdownText != null) countdownText.text = timeRemaining.ToString("F1");

            if (progressFill != null && totalTime > 0f)
                progressFill.fillAmount = 1f - (timeRemaining / totalTime);

            // 정수 초가 바뀌는 순간에만 틱 피드백 재생 (매 프레임 재생 방지)
            int displaySecond = Mathf.CeilToInt(timeRemaining);
            if (displaySecond != lastDisplayedSecond && displaySecond > 0)
            {
                lastDisplayedSecond = displaySecond;

                // 파이널 구간은 강한 틱, 일반 구간은 약한 틱
                if (displaySecond <= finalCountdownSeconds)
                    onFinalCountdownTickFeedback?.PlayFeedbacks();
                else
                    onTickFeedback?.PlayFeedbacks();
            }
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.9f, 0.5f)]
        private void TestStart() => onStartFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium)]
        private void TestTick() => onTickFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.5f)]
        private void TestFinalTick() => onFinalCountdownTickFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(0.6f, 1f, 0.6f)]
        private void TestCompleted() => onCompletedFeedback?.PlayFeedbacks();

        // 이벤트 없이 10초 카운트다운 시뮬레이션
        [TitleGroup("Debug")]
        [Button("Simulate 10s Countdown"), GUIColor(0.8f, 0.8f, 1f)]
        private void SimulateCountdown()
        {
            if (!Application.isPlaying) return;
            active = true;
            timeRemaining = 10f;
            totalTime = 10f;
            lastDisplayedSecond = -1;
            if (panelRoot != null) panelRoot.SetActive(true);
            onStartFeedback?.PlayFeedbacks();
        }
#endif
    }
}
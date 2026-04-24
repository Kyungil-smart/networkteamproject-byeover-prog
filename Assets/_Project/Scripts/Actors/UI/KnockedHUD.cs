using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 행동불능 상태 HUD
// 출혈 타이머와 부활 진행 바를 표시
// EventBus로 PlayerKnocked / PlayerStateChanged / ReviveStarted / ReviveProgress / ReviveEnded 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// 로컬 플레이어의 기절 상태 UI
    /// 출혈 카운트다운과 동료 부활 진행 바 담당
    /// </summary>
    public class KnockedHUD : MonoBehaviour
    {
        // 블리드아웃 UI
        [BoxGroup("Bleedout")]
        [Required, SerializeField] private TMP_Text bleedoutText;// 남은 시간 표시

        [BoxGroup("Bleedout")]
        [Required, SerializeField] private Image bleedoutFill;// 남은 비율 Fill 이미지

        // 부활 UI
        [BoxGroup("Revive")]
        [Required, SerializeField] private GameObject revivePanel;// 부활 중에만 켜지는 서브 패널

        [BoxGroup("Revive")]
        [Required, SerializeField] private Image reviveProgressFill;// 부활 진행도 Fill

        [BoxGroup("Revive")]
        [Required, SerializeField] private TMP_Text reviveStatusText;// 문구

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("플레이어가 기절 상태일 때 설정값 이하가 되는 순간부터 강조")]
        [MinValue(1), SerializeField] private int criticalBleedoutSeconds = 3;

        [BoxGroup("Config")]
        [Tooltip("부활 시작 시 표시 문구")]
        [SerializeField] private string reviveStatusLabel = "응급 처치중...";

        // Feel 피드백 - 블리드아웃
        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("기절시 1회 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("기절중 루프로 재생되는 심박음, MMF Looper 피드백 사용")]
        [SerializeField] private MMF_Player heartbeatLoopFeedback;

        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("남은 시간이 3초 이하로 떨어지는 순간 1회 재생")]
        [SerializeField] private MMF_Player onCriticalBleedoutFeedback;

        // Feel 피드백 - 부활
        [FoldoutGroup("Feedbacks/Revive")]
        [Tooltip("동료가 응급처치 시작했을 때 재생")]
        [SerializeField] private MMF_Player onReviveStartedFeedback;

        [FoldoutGroup("Feedbacks/Revive")]
        [Tooltip("응급처치가 종료됐을 때 재생(성공/취소 공통)")]
        [SerializeField] private MMF_Player onReviveEndedFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float bleedoutTotal;// 서버가 알려준 총 기절 시간
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float bleedoutRemaining;// 현재 남은 시간
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool bleedoutActive;// 타이머 구동 여부
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool criticalTriggered;// 크리티컬 피드백 중복 방지

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Subscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Subscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Subscribe<ReviveEndedEvent>(OnReviveEnded);

            // 부활 패널은 ReviveStartedEvent를 받기 전까지 숨김
            if (revivePanel != null) revivePanel.SetActive(false);
        }

        // 컴포넌트 비활성화 시 구독 해제 + 루프 피드백 강제 정지
        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Unsubscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Unsubscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Unsubscribe<ReviveEndedEvent>(OnReviveEnded);
            
            StopHeartbeatLoop();
        }

        // 매 프레임 블리드아웃 타이머 감소 + UI 갱신
        private void Update()
        {
            if (!bleedoutActive) return;

            // 프레임 델타만큼 감소, 음수 방지
            bleedoutRemaining = Mathf.Max(0f, bleedoutRemaining - Time.deltaTime);

            // 올림으로 표시 — 0.3초 남았을 때 0초가 잠깐 보이는 현상 방지
            if (bleedoutText != null) bleedoutText.text = $"{Mathf.CeilToInt(bleedoutRemaining)}s";

            // 비율 Fill 갱신 (0 나누기 방지)
            if (bleedoutFill != null && bleedoutTotal > 0f)
                bleedoutFill.fillAmount = bleedoutRemaining / bleedoutTotal;

            // 크리티컬 구간 진입 순간 1회만 피드백 재생 (edge trigger)
            if (!criticalTriggered && bleedoutRemaining <= criticalBleedoutSeconds && bleedoutRemaining > 0f)
            {
                onCriticalBleedoutFeedback?.PlayFeedbacks();
                criticalTriggered = true;
            }
        }

        // 해당 clientId가 로컬 플레이어인지 판별 (모든 이벤트 핸들러 공용)
        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId;
        }

        // 로컬 플레이어가 쓰러졌을 때 타이머 시작
        private void OnKnocked(PlayerKnockedEvent e)
        {
            if (!IsLocalClient(e.victimClientId)) return;

            bleedoutTotal = e.bleedoutSeconds;
            bleedoutRemaining = e.bleedoutSeconds;
            bleedoutActive = true;

            // 새 사이클이므로 크리티컬 플래그 리셋 (두 번째 기절중에도 경고 울리게)
            criticalTriggered = false;

            onKnockedFeedback?.PlayFeedbacks();
            StartHeartbeatLoop();
        }

        // 기절 상태에서 벗어났을 때 정리
        private void OnStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            if (e.newState != PlayerState.Knocked)
            {
                bleedoutActive = false;
                if (revivePanel != null) revivePanel.SetActive(false);
                StopHeartbeatLoop();
            }
        }

        // 동료가 응급처치시 부활 패널 활성화
        private void OnReviveStarted(ReviveStartedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            if (revivePanel != null) revivePanel.SetActive(true);
            if (reviveStatusText != null) reviveStatusText.text = reviveStatusLabel;

            // 이전 사이클 값이 남아있을 수 있으므로 0으로 초기화
            if (reviveProgressFill != null) reviveProgressFill.fillAmount = 0f;

            onReviveStartedFeedback?.PlayFeedbacks();
        }

        // 응급처치 진행도 갱신
        private void OnReviveProgress(ReviveProgressEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;
            if (reviveProgressFill != null) reviveProgressFill.fillAmount = e.progress01;
        }

        // 부활 종료 (성공/취소 공통)
        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            if (revivePanel != null) revivePanel.SetActive(false);
            onReviveEndedFeedback?.PlayFeedbacks();
        }

        // 심박음 루프 시작 (중복 재생 방지)
        private void StartHeartbeatLoop()
        {
            if (heartbeatLoopFeedback == null) return;
            if (!heartbeatLoopFeedback.IsPlaying)
                heartbeatLoopFeedback.PlayFeedbacks();
        }

        // 심박음 루프 정지
        private void StopHeartbeatLoop()
        {
            if (heartbeatLoopFeedback == null) return;
            if (heartbeatLoopFeedback.IsPlaying)
                heartbeatLoopFeedback.StopFeedbacks();
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.5f)]
        private void TestKnocked() => onKnockedFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button("Start Heartbeat Loop")]
        private void TestStartHeartbeat() => StartHeartbeatLoop();

        [TitleGroup("Debug")]
        [Button("Stop Heartbeat Loop")]
        private void TestStopHeartbeat() => StopHeartbeatLoop();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.3f, 0.3f)]
        private void TestCritical() => onCriticalBleedoutFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(0.6f, 1f, 0.6f)]
        private void TestReviveStarted() => onReviveStartedFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium)]
        private void TestReviveEnded() => onReviveEndedFeedback?.PlayFeedbacks();

        // 이벤트 없이 10초 블리드아웃 시뮬레이션
        [TitleGroup("Debug")]
        [Button("Simulate 10s Bleedout"), GUIColor(0.8f, 0.8f, 1f)]
        private void SimulateBleedout()
        {
            if (!Application.isPlaying) return;
            bleedoutTotal = 10f;
            bleedoutRemaining = 10f;
            bleedoutActive = true;
            criticalTriggered = false;
            onKnockedFeedback?.PlayFeedbacks();
            StartHeartbeatLoop();
        }
#endif
    }
}
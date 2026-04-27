using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Reflection;

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

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("플레이어가 기절 상태일 때 설정값 이하가 되는 순간부터 강조")]
        [MinValue(1), SerializeField] private int criticalBleedoutSeconds = 3;

        [BoxGroup("Config")]
        [Tooltip("부활 시작 시 표시 문구")]
        [SerializeField] private string reviveStatusLabel = "응급 처치중...";

        // Feel 피드백 - 블리드아웃
        [FoldoutGroup("Feedbacks")]
        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("기절시 1회 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("Feedbacks")]
        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("기절중 루프로 재생되는 심박음, MMF Looper 피드백 사용")]
        [SerializeField] private MMF_Player heartbeatLoopFeedback;

        [FoldoutGroup("Feedbacks")]
        [FoldoutGroup("Feedbacks/Bleedout")]
        [Tooltip("남은 시간이 3초 이하로 떨어지는 순간 1회 재생")]
        [SerializeField] private MMF_Player onCriticalBleedoutFeedback;
        
        // 비네트
        [BoxGroup("Vignette")]
        [SerializeField] private MonoBehaviour quirkyVignette; // Quirky Vignette 컴포넌트

        [BoxGroup("Vignette")]
        [SerializeField] private string intensityMemberName = "Intensity"; // 에셋의 강도 변수명

        [BoxGroup("Vignette")]
        [SerializeField] private float vignetteMin = 0.15f;

        [BoxGroup("Vignette")]
        [SerializeField] private float vignetteMax = 0.6f;

        private Tween vignetteTween;
        private float currentVignetteIntensity;

        // Feel 피드백 - 부활
        [FoldoutGroup("Feedbacks")]
        [FoldoutGroup("Feedbacks/Revive")]
        [Tooltip("동료가 응급처치 시작했을 때 재생")]
        [SerializeField] private MMF_Player onReviveStartedFeedback;

        [FoldoutGroup("Feedbacks")]
        [FoldoutGroup("Feedbacks/Revive")]
        [Tooltip("응급처치가 종료됐을 때 재생(성공/취소 공통)")]
        [SerializeField] private MMF_Player onReviveEndedFeedback;
        
        [SerializeField] private TMP_Text knockedTitleText;
        [SerializeField] private TMP_Text knockedCountText;
        [SerializeField] private TMP_Text knockedGuideText;

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
            ResetVignette();
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
                StartCriticalVignettePulse();
                criticalTriggered = true;
            }
            // 남은 시간이 줄어들수록 비네트 강도 증가
            if (!criticalTriggered && bleedoutTotal > 0f)
            {
                float ratio = bleedoutRemaining / bleedoutTotal;
                float targetIntensity = Mathf.Lerp(vignetteMax, vignetteMin, ratio);
                SetVignetteIntensity(targetIntensity);
            }
        }
        // 비네트 강도 설정
        private void SetVignetteIntensity(float value)
        {
            if (quirkyVignette == null) return;

            currentVignetteIntensity = value;

            var type = quirkyVignette.GetType();

            var property = type.GetProperty(intensityMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(quirkyVignette, value);
                return;
            }

            var field = type.GetField(intensityMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(quirkyVignette, value);
            }
        }

        // 크리티컬 상태 비네트 맥박 연출
        private void StartCriticalVignettePulse()
        {
            if (quirkyVignette == null) return;

            vignetteTween?.Kill();

            vignetteTween = DOTween.To(
                    () => currentVignetteIntensity,
                    value => SetVignetteIntensity(value),
                    vignetteMax,
                    0.3f
                )
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine);
        }

        // 비네트 초기화
        private void ResetVignette()
        {
            vignetteTween?.Kill();
            SetVignetteIntensity(vignetteMin);
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
            if (e.newState != PlayerState.Knocked)
            {
                bleedoutActive = false;
                StopHeartbeatLoop();
                ResetVignette();
            }
        }

        // 동료가 응급처치시 부활 패널 활성화
        private void OnReviveStarted(ReviveStartedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            // 기존 타이틀/카운트 숨김
            knockedTitleText.gameObject.SetActive(false);
            knockedCountText.gameObject.SetActive(false);

            // 텍스트 재사용
            bleedoutText.text = "응급 처치중...";

            // 게이지 초기화
            bleedoutFill.fillAmount = 0f;

            onReviveStartedFeedback?.PlayFeedbacks();
        }

        // 응급처치 진행도 갱신
        private void OnReviveProgress(ReviveProgressEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            bleedoutFill.fillAmount = e.progress01;
        }

        // 부활 종료 (성공/취소 공통)
        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            // 다시 원상복구
            knockedTitleText.gameObject.SetActive(true);
            knockedCountText.gameObject.SetActive(true);

            bleedoutText.text = "동료의 응급처치를 기다리세요";

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
        [Button("Test Revive Started"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestReviveStarted()
        {
            if (!Application.isPlaying) return;

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(false);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(false);

            if (knockedGuideText != null)
                knockedGuideText.text = reviveStatusLabel;

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 0.05f;

            onReviveStartedFeedback?.PlayFeedbacks();
        }

        [TitleGroup("Debug")]
        [Button("Test Revive 50%")]
        private void TestReviveHalf()
        {
            if (!Application.isPlaying) return;

            if (bleedoutFill != null)
            {
                bleedoutFill.fillAmount = 0.5f;
                Debug.Log($"Revive Fill Test: {bleedoutFill.fillAmount}");
            }
        }
        
        [TitleGroup("Debug")]
        [Button("Test Revive Ended")]
        private void TestReviveEnded()
        {
            if (!Application.isPlaying) return;

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(true);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(true);

            if (knockedGuideText != null)
                knockedGuideText.text = "동료의 응급처치를 기다리세요";

            onReviveEndedFeedback?.PlayFeedbacks();
        }

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

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(true);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(true);

            if (knockedGuideText != null)
                knockedGuideText.text = "동료의 응급처치를 기다리세요";

            if (bleedoutText != null)
                bleedoutText.text = "10s";

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 1f;

            onKnockedFeedback?.PlayFeedbacks();
            StartHeartbeatLoop();
        }
#endif
    }
}
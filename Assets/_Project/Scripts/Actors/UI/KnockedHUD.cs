using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

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
    /// 주의: HUDManager와 bleedoutFill을 공유하지 않도록 Inspector에서 다른 Image를 할당해야 함
    /// </summary>
    public class KnockedHUD : MonoBehaviour
    {
        [BoxGroup("Root")]
        [Tooltip("KnockedHUD 루트는 가능하면 항상 활성화하고, 실제 표시/숨김은 이 루트로 제어합니다.")]
        [SerializeField] private GameObject contentRoot;

        // 블리드아웃 UI
        [BoxGroup("Bleedout")]
        [Required, SerializeField, Tooltip("⚠️ HUDManager의 hpFill과 다른 Image를 할당하세요")]
        private TMP_Text bleedoutText;// 남은 시간 표시

        [BoxGroup("Bleedout")]
        [Required, SerializeField, Tooltip("⚠️ HUDManager의 hpFill과 다른 Image를 할당하세요")]
        private Image bleedoutFill;// 남은 비율 Fill 이미지

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("플레이어가 기절 상태일 때 설정값 이하가 되는 순간부터 강조")]
        [MinValue(1), SerializeField] private int criticalBleedoutSeconds = 3;

        [BoxGroup("Config")]
        [Tooltip("부활 시작 시 표시 문구")]
        [SerializeField] private string reviveStatusLabel = "응급 처치중...";

        // Feel 피드백 - 블리드아웃
        [FoldoutGroup("Bleedout Feedbacks")]
        [Tooltip("기절시 1회 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("Bleedout Feedbacks")]
        [Tooltip("기절중 루프로 재생되는 심박음, MMF Looper 피드백 사용")]
        [SerializeField] private MMF_Player heartbeatLoopFeedback;

        [FoldoutGroup("Bleedout Feedbacks")]
        [Tooltip("남은 시간이 3초 이하로 떨어지는 순간 1회 재생")]
        [SerializeField] private MMF_Player onCriticalBleedoutFeedback;
        
        // 비네트
        [BoxGroup("Vignette")]
        [Tooltip("선택 사항. 비워두면 Blood Vignette Root 아래에서 Q_Vignette 컴포넌트를 자동 탐색합니다.")]
        [SerializeField] private MonoBehaviour quirkyVignette; // Quirky Vignette 컴포넌트

        [BoxGroup("Vignette")]
        [SerializeField] private GameObject bloodVignetteRoot;

        [BoxGroup("Vignette")]
        [Tooltip("Fallback 전용. Q_Vignette/CanvasGroup을 찾지 못했을 때만 사용합니다.")]
        [SerializeField] private Graphic bloodVignetteGraphic;

        [BoxGroup("Vignette")]
        [SerializeField] private Color bloodVignetteColor = new(0.9f, 0.02f, 0.02f, 1f);

        [BoxGroup("Vignette")]
        [PropertyRange(0f, 1f), SerializeField] private float vignetteMinAlpha = 0.15f;

        [BoxGroup("Vignette")]
        [PropertyRange(0f, 1f), SerializeField] private float vignetteMaxAlpha = 0.75f;

        [BoxGroup("Vignette Scale")]
        [SerializeField] private bool animateVignetteScale = true;

        [BoxGroup("Vignette Scale")]
        [SerializeField] private Transform bloodVignetteScaleTarget;

        [BoxGroup("Vignette Scale")]
        [SerializeField] private float vignetteMinScale = 1f;

        [BoxGroup("Vignette Scale")]
        [SerializeField] private float vignetteMaxScale = 1.35f;

        private Tween vignetteTween;
        private Tween vignetteScaleTween;
        private float currentVignetteIntensity;
        private float currentVignetteScale = 1f;
        private string lastVignetteScaleApplyPath = "Fallback";
        private float lastVignetteScaleBefore;
        private float lastVignetteScaleAfter;
        private Coroutine reviveTestCoroutine;
        private Q_Vignette_Base qVignette;
        private CanvasGroup bloodVignetteCanvasGroup;
        private Graphic[] bloodVignetteGraphics;

        // Feel 피드백 - 부활
        [FoldoutGroup("Revive Feedbacks")]
        [Tooltip("동료가 응급처치 시작했을 때 재생")]
        [SerializeField] private MMF_Player onReviveStartedFeedback;

        [FoldoutGroup("Revive Feedbacks")]
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
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool reviveActive;// 부활 진행 중에는 bleedout fill 갱신을 막음

        private void Awake()
        {
            EnsureRootScale();
        }

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EnsureRootScale();

            EventBus.Subscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Subscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Subscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Subscribe<ReviveEndedEvent>(OnReviveEnded);
        }

        // 컴포넌트 비활성화 시 구독 해제 + 루프 피드백 강제 정지
        private void OnDisable()
        {
            StopReviveTestCoroutine();

            EventBus.Unsubscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Unsubscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Unsubscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Unsubscribe<ReviveEndedEvent>(OnReviveEnded);
            
            StopHeartbeatLoop();
            ResetVignette();
        }

        private void SetKnockedContentVisible(bool visible)
        {
            EnsureRootScale();

            if (contentRoot != null)
                contentRoot.SetActive(visible);
        }

        public void SetVisibleForUI(bool visible)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            EnsureRootScale();
            SetKnockedContentVisible(visible);
        }

        private bool EnsureActiveForTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[KnockedHUD] 테스트는 Play Mode에서만 실행할 수 있습니다.", this);
                return false;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            EnsureRootScale();

            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[KnockedHUD] KnockedHUD 루트 또는 부모가 비활성화되어 테스트 코루틴을 시작할 수 없습니다. 부모 HUD 오브젝트를 먼저 활성화해주세요.", this);
                return false;
            }

            SetKnockedContentVisible(true);
            return true;
        }

        // 매 프레임 블리드아웃 타이머 감소 + UI 갱신
        private void Update()
        {
            if (!bleedoutActive) return;

            // 프레임 델타만큼 감소, 음수 방지
            bleedoutRemaining = Mathf.Max(0f, bleedoutRemaining - Time.deltaTime);

            // 올림으로 표시 — 0.3초 남았을 때 0초가 잠깐 보이는 현상 방지
            if (bleedoutText != null) bleedoutText.text = $"{Mathf.CeilToInt(bleedoutRemaining)}s";

            // ⭐ 부활 진행 중이 아닐 때만 bleedout fill 갱신
            // (부활 게이지는 OnReviveProgress에서 별도 처리)
            if (!reviveActive && bleedoutFill != null && bleedoutTotal > 0f)
                bleedoutFill.fillAmount = bleedoutRemaining / bleedoutTotal;

            // 크리티컬 구간 진입 순간 1회만 피드백 재생 (edge trigger)
            if (!criticalTriggered && bleedoutRemaining <= criticalBleedoutSeconds && bleedoutRemaining > 0f)
            {
                UIFeedbackTester.Play(onCriticalBleedoutFeedback, this, "위험 출혈");
                StartCriticalVignettePulse();
                criticalTriggered = true;
            }
            
            // 남은 시간이 줄어들수록 비네트 강도 증가
            if (bleedoutTotal > 0f)
            {
                float knockedRatio = Mathf.Clamp01(bleedoutRemaining / bleedoutTotal);
                ApplyBloodVignetteFromKnockedRatio(knockedRatio, "Bleedout");
            }
        }
        
        private GameObject GetBloodVignetteRoot()
        {
            if (bloodVignetteRoot != null) return bloodVignetteRoot;
            if (quirkyVignette != null) return quirkyVignette.gameObject;
            if (bloodVignetteGraphic != null) return bloodVignetteGraphic.gameObject;
            return null;
        }

        private Transform GetBloodVignetteScaleTarget()
        {
            if (bloodVignetteScaleTarget != null)
                return bloodVignetteScaleTarget;

            GameObject root = GetBloodVignetteRoot();
            if (root == null) return null;
            if (root == gameObject)
            {
                Debug.LogWarning("[KnockedHUD] BloodVignette Scale Target이 KnockedHUD 루트와 같습니다. 루트 스케일은 항상 1로 유지해야 하므로 BloodVignette Root 또는 하위 Transform을 연결해주세요.", this);
                return null;
            }

            return root.transform;
        }

        private void CacheBloodVignetteTargets()
        {
            GameObject root = GetBloodVignetteRoot();
            if (root == null) return;

            qVignette = quirkyVignette as Q_Vignette_Base;
            if (qVignette == null)
                qVignette = root.GetComponentInChildren<Q_Vignette_Base>(true);

            bloodVignetteCanvasGroup = root.GetComponentInChildren<CanvasGroup>(true);
            bloodVignetteGraphics = root.GetComponentsInChildren<Graphic>(true);
        }

        private void SetBloodVignetteIntensity(float intensity)
        {
            currentVignetteIntensity = Mathf.Clamp01(intensity);
            SetBloodVignetteVisible(currentVignetteIntensity > 0f);

            CacheBloodVignetteTargets();

            if (ApplyQuirkyVignetteIntensity(currentVignetteIntensity)) return;
            if (ApplyCanvasGroupIntensity(currentVignetteIntensity)) return;
            if (ApplyGraphicIntensity(currentVignetteIntensity)) return;
            ApplyMaterialIntensity(currentVignetteIntensity);
        }

        private void SetBloodVignetteScale(float scale)
        {
            currentVignetteScale = Mathf.Max(0f, scale);
            lastVignetteScaleApplyPath = "Fallback";
            lastVignetteScaleBefore = currentVignetteScale;
            lastVignetteScaleAfter = currentVignetteScale;

            CacheBloodVignetteTargets();

            if (ApplyQuirkyMainScale(currentVignetteScale))
                return;

            if (!animateVignetteScale) return;

            Transform target = GetBloodVignetteScaleTarget();
            if (target == null) return;

            lastVignetteScaleApplyPath = "TransformScale";
            lastVignetteScaleBefore = target.localScale.x;
            target.localScale = Vector3.one * currentVignetteScale;
            lastVignetteScaleAfter = target.localScale.x;
        }

        private void ApplyBloodVignetteFromKnockedRatio(float knockedRatio, string context)
        {
            knockedRatio = Mathf.Clamp01(knockedRatio);
            float danger = 1f - knockedRatio;
            float alpha = Mathf.Lerp(vignetteMinAlpha, vignetteMaxAlpha, danger);
            float scale = Mathf.Lerp(vignetteMinScale, vignetteMaxScale, danger);

            SetBloodVignetteIntensity(alpha);
            SetBloodVignetteScale(scale);
            LogBloodVignetteState(context, knockedRatio, danger, alpha, scale);
        }

        private void SetBloodVignetteMax(string context)
        {
            SetBloodVignetteVisible(true);
            SetBloodVignetteIntensity(vignetteMaxAlpha);
            SetBloodVignetteScale(vignetteMaxScale);
            LogBloodVignetteState(context, 0f, 1f, vignetteMaxAlpha, vignetteMaxScale);
        }

        private void LogBloodVignetteState(string context, float knockedRatio, float danger, float alpha, float scale)
        {
            Debug.Log($"[KnockedHUD] BloodVignette {context} path={lastVignetteScaleApplyPath}, mainScale {lastVignetteScaleBefore:F2}->{lastVignetteScaleAfter:F2}, knockedRatio={knockedRatio:F2}, danger={danger:F2}, alpha={alpha:F2}, scale={scale:F2}", this);
        }

        private bool ApplyQuirkyMainScale(float scale)
        {
            if (qVignette == null) return false;

            float clampedScale = Mathf.Clamp(scale, 0f, 5f);

            if (qVignette is Q_Vignette_Single single)
            {
                lastVignetteScaleApplyPath = "QuirkyMainScale";
                lastVignetteScaleBefore = single.mainScale;
                single.CheckReferences();
                single.mainScale = clampedScale;
                single.SetVignetteMainScale(clampedScale);
                single.SetVignetteSkyScale(clampedScale);
                single.UpdateVignette();
                lastVignetteScaleAfter = single.mainScale;
                return true;
            }

            if (qVignette is Q_Vignette_Split split)
            {
                lastVignetteScaleApplyPath = "QuirkyMainScale";
                lastVignetteScaleBefore = split.mainScale;
                split.CheckReferences();
                split.mainScale = clampedScale;
                split.SetVignetteMainScale(clampedScale);
                split.UpdateVignette();
                lastVignetteScaleAfter = split.mainScale;
                return true;
            }

            return false;
        }

        private bool ApplyQuirkyVignetteIntensity(float intensity)
        {
            if (qVignette == null) return false;

            Color color = bloodVignetteColor;
            color.a = intensity;

            if (qVignette is Q_Vignette_Single single)
            {
                single.CheckReferences();
                single.mainColor = color;
                single.SetVignetteMainColor(color);
                single.SetVignetteSkyColor(color);
                single.UpdateVignette();
                return true;
            }

            if (qVignette is Q_Vignette_Split split)
            {
                split.CheckReferences();
                split.mainColor = color;
                split.skyColor = color;
                split.SetVignetteMainColor(color);
                split.SetVignetteSkyColor(color);
                split.UpdateVignette();
                return true;
            }

            qVignette.CheckReferences();
            qVignette.SetVignetteMainColor(color);
            qVignette.SetVignetteSkyColor(color);
            return true;
        }

        private bool ApplyCanvasGroupIntensity(float intensity)
        {
            if (bloodVignetteCanvasGroup == null) return false;

            bloodVignetteCanvasGroup.alpha = intensity;
            return true;
        }

        private bool ApplyGraphicIntensity(float intensity)
        {
            Graphic[] targets = bloodVignetteGraphics;
            if ((targets == null || targets.Length == 0) && bloodVignetteGraphic != null)
                targets = new[] { bloodVignetteGraphic };
            if (targets == null || targets.Length == 0) return false;

            foreach (Graphic graphic in targets)
            {
                if (graphic == null) continue;
                Color color = graphic.color;
                color.a = intensity;
                graphic.color = color;
            }

            return true;
        }

        private void ApplyMaterialIntensity(float intensity)
        {
            if (bloodVignetteGraphics == null) return;

            foreach (Graphic graphic in bloodVignetteGraphics)
            {
                if (graphic == null || graphic.material == null) continue;

                Material material = graphic.material;
                if (material.HasProperty("_Alpha"))
                    material.SetFloat("_Alpha", intensity);
                else if (material.HasProperty("_Opacity"))
                    material.SetFloat("_Opacity", intensity);
                else if (material.HasProperty("_Intensity"))
                    material.SetFloat("_Intensity", intensity);
                else if (material.HasProperty("_VignetteIntensity"))
                    material.SetFloat("_VignetteIntensity", intensity);
            }
        }

        // 크리티컬 상태 비네트 맥박 연출
        private void StartCriticalVignettePulse()
        {
            SetBloodVignetteVisible(true);
            SetBloodVignetteScale(vignetteMaxScale);

            vignetteTween?.Kill();

            vignetteTween = DOTween.To(
                    () => currentVignetteIntensity,
                    SetBloodVignetteIntensity,
                    vignetteMaxAlpha,
                    0.3f
                )
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine);
        }

        // 비네트 초기화
        private void ResetVignette()
        {
            vignetteTween?.Kill();
            vignetteScaleTween?.Kill();
            SetBloodVignetteIntensity(0f);
            SetBloodVignetteScale(vignetteMinScale);
            SetBloodVignetteVisible(false);
        }

        private void FadeVignetteTo(float targetIntensity, float duration)
        {
            vignetteTween?.Kill();
            vignetteTween = DOTween.To(
                    () => currentVignetteIntensity,
                    SetBloodVignetteIntensity,
                    Mathf.Clamp01(targetIntensity),
                    duration
                )
                .SetEase(Ease.OutSine);

            if (animateVignetteScale)
            {
                vignetteScaleTween?.Kill();
                vignetteScaleTween = DOTween.To(
                        () => currentVignetteScale,
                        SetBloodVignetteScale,
                        vignetteMinScale,
                        duration
                    )
                    .SetEase(Ease.OutSine);
            }
        }

        private void SetBloodVignetteVisible(bool visible)
        {
            GameObject target = GetBloodVignetteRoot();
            if (target == gameObject)
            {
                Debug.LogWarning("[KnockedHUD] BloodVignette Root가 KnockedHUD 루트와 같습니다. 루트 활성/비활성은 건드리지 않고 contentRoot만 사용합니다.", this);
                return;
            }

            if (target != null)
                target.SetActive(visible);
        }

        private void EnsureRootScale()
        {
            if (transform.localScale == Vector3.one) return;

            Debug.LogWarning($"[KnockedHUD] KnockedHUD 루트 scale이 {transform.localScale} 상태라 Vector3.one으로 보정합니다. 표시/숨김은 contentRoot와 bloodVignetteRoot로 처리해주세요.", this);
            transform.localScale = Vector3.one;
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

            Debug.Log($"[KnockedHUD] PlayerKnocked victim={e.victimClientId}, attacker={e.attackerClientId}, bleedout={e.bleedoutSeconds:F1}", this);

            SetKnockedContentVisible(true);
            SetBloodVignetteVisible(true);
            SetBloodVignetteIntensity(vignetteMinAlpha);
            SetBloodVignetteScale(vignetteMinScale);
            bleedoutTotal = e.bleedoutSeconds;
            bleedoutRemaining = e.bleedoutSeconds;
            bleedoutActive = true;
            reviveActive = false;

            // 새 사이클이므로 크리티컬 플래그 리셋 (두 번째 기절중에도 경고 울리게)
            criticalTriggered = false;

            UIFeedbackTester.Play(onKnockedFeedback, this, "기절 진입");
            StartHeartbeatLoop();
        }

        // 기절 상태에서 벗어났을 때 정리
        private void OnStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            Debug.Log($"[KnockedHUD] PlayerStateChanged {e.oldState} -> {e.newState}", this);

            if (e.newState != PlayerState.Knocked)
            {
                bleedoutActive = false;
                reviveActive = false;
                StopHeartbeatLoop();
                ResetVignette();
                SetKnockedContentVisible(false);
            }
            else
            {
                SetKnockedContentVisible(true);
            }
        }

        // 동료가 응급처치시 부활 패널 활성화
        private void OnReviveStarted(ReviveStartedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            Debug.Log($"[KnockedHUD] ReviveStarted reviver={e.reviverClientId}, target={e.targetClientId}, duration={e.duration:F1}", this);

            SetKnockedContentVisible(true);
            reviveActive = true;
            bleedoutActive = false;
            StopHeartbeatLoop();
            FadeVignetteTo(Mathf.Min(currentVignetteIntensity, vignetteMinAlpha), 0.5f);

            // 기존 타이틀/카운트 숨김
            if (knockedTitleText != null) knockedTitleText.gameObject.SetActive(false);
            if (knockedCountText != null) knockedCountText.gameObject.SetActive(false);

            // 텍스트 재사용
            if (bleedoutText != null) bleedoutText.text = reviveStatusLabel;

            // 게이지 초기화
            if (bleedoutFill != null) bleedoutFill.fillAmount = 0f;

            UIFeedbackTester.Play(onReviveStartedFeedback, this, "부활 시작");
        }

        // 응급처치 진행도 갱신
        private void OnReviveProgress(ReviveProgressEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            reviveActive = true;
            float progress = Mathf.Clamp01(e.progress01);
            Debug.Log($"[KnockedHUD] ReviveProgress target={e.targetClientId}, progress={progress:F2}", this);

            // ⭐ 부활 게이지는 여기서 직접 갱신 (Update의 bleedout 갱신과 별개)
            if (bleedoutFill != null) bleedoutFill.fillAmount = progress;
        }

        // 부활 종료 (성공/취소 공통)
        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            Debug.Log($"[KnockedHUD] ReviveEnded reviver={e.reviverClientId}, target={e.targetClientId}, result={e.result}", this);

            reviveActive = false;

            // 다시 원상복구
            if (knockedTitleText != null) knockedTitleText.gameObject.SetActive(true);
            if (knockedCountText != null) knockedCountText.gameObject.SetActive(true);

            if (bleedoutText != null) bleedoutText.text = "동료의 응급처치를 기다리세요";

            if (e.result == ReviveResult.Completed)
            {
                bleedoutActive = false;
                StopHeartbeatLoop();
                ResetVignette();
                SetKnockedContentVisible(false);
            }
            else
            {
                SetKnockedContentVisible(true);
                bleedoutActive = true;
                StartHeartbeatLoop();
                SetBloodVignetteVisible(true);
                SetBloodVignetteIntensity(currentVignetteIntensity);
            }

            UIFeedbackTester.Play(onReviveEndedFeedback, this, "부활 종료");
        }

        // 심박음 루프 시작 (중복 재생 방지)
        private void StartHeartbeatLoop()
        {
            if (heartbeatLoopFeedback != null && heartbeatLoopFeedback.IsPlaying) return;
            UIFeedbackTester.Play(heartbeatLoopFeedback, this, "심박 루프");
        }

        // 심박음 루프 정지
        private void StopHeartbeatLoop()
        {
            if (heartbeatLoopFeedback == null) return;
            if (heartbeatLoopFeedback.IsPlaying)
                heartbeatLoopFeedback.StopFeedbacks();
        }

        private void StopReviveTestCoroutine()
        {
            if (reviveTestCoroutine == null) return;

            StopCoroutine(reviveTestCoroutine);
            reviveTestCoroutine = null;
        }

        private HUDManager[] FindHudManagers()
        {
            HUDManager[] hudManagers = GetComponentsInParent<HUDManager>(true);
            return hudManagers.Length > 0 ? hudManagers : FindObjectsOfType<HUDManager>(true);
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("기절 피드백"), GUIColor(1f, 0.5f, 0.5f)]
        private void TestKnocked()
        {
            if (!EnsureActiveForTest()) return;
            bleedoutTotal = 10f;
            bleedoutRemaining = 10f;
            bleedoutActive = true;
            reviveActive = false;
            criticalTriggered = false;
            SetBloodVignetteVisible(true);
            SetBloodVignetteIntensity(vignetteMinAlpha);
            SetBloodVignetteScale(vignetteMinScale);

            if (knockedTitleText != null) knockedTitleText.gameObject.SetActive(true);
            if (knockedCountText != null) knockedCountText.gameObject.SetActive(true);
            if (bleedoutText != null) bleedoutText.text = "10s";
            if (bleedoutFill != null) bleedoutFill.fillAmount = 1f;

            UIFeedbackTester.Play(onKnockedFeedback, this, "기절");
            StartHeartbeatLoop();
        }

        [TitleGroup("Debug")]
        [Button("심박 루프 시작")]
        private void TestStartHeartbeat() => StartHeartbeatLoop();

        [TitleGroup("Debug")]
        [Button("심박 루프 정지")]
        private void TestStopHeartbeat() => StopHeartbeatLoop();

        [TitleGroup("Debug")]
        [Button("위험 출혈 피드백"), GUIColor(1f, 0.3f, 0.3f)]
        private void TestCritical()
        {
            if (!EnsureActiveForTest()) return;
            SetBloodVignetteMax("Critical Test");
            UIFeedbackTester.Play(onCriticalBleedoutFeedback, this, "위험 출혈");
        }

        [TitleGroup("Debug")]
        [Button("Test Blood Vignette Max"), GUIColor(1f, 0.25f, 0.25f)]
        private void TestBloodVignetteMax()
        {
            if (!EnsureActiveForTest()) return;
            SetBloodVignetteMax("Max Test");
        }

        [TitleGroup("Debug")]
        [Button("부활 시작 테스트"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestReviveStarted()
        {
            if (!EnsureActiveForTest()) return;
            reviveActive = true;

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(false);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(false);

            if (knockedGuideText != null)
                knockedGuideText.text = reviveStatusLabel;

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 0.05f;

            UIFeedbackTester.Play(onReviveStartedFeedback, this, "부활 시작");
        }

        [TitleGroup("Debug")]
        [Button("부활 50% 테스트")]
        private void TestReviveHalf()
        {
            if (!EnsureActiveForTest()) return;

            reviveActive = true;

            if (bleedoutFill != null)
            {
                bleedoutFill.fillAmount = 0.5f;
                Debug.Log($"Revive Fill Test: {bleedoutFill.fillAmount}");
            }
        }
        
        [TitleGroup("Debug")]
        [Button("부활 종료 테스트")]
        private void TestReviveEnded()
        {
            if (!EnsureActiveForTest()) return;

            reviveActive = false;

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(true);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(true);

            if (knockedGuideText != null)
                knockedGuideText.text = "동료의 응급처치를 기다리세요";

            UIFeedbackTester.Play(onReviveEndedFeedback, this, "부활 종료");
        }

        // 이벤트 없이 10초 블리드아웃 시뮬레이션
        [TitleGroup("Debug")]
        [Button("10초 출혈 테스트"), GUIColor(0.8f, 0.8f, 1f)]
        private void SimulateBleedout()
        {
            if (!EnsureActiveForTest()) return;

            bleedoutTotal = 10f;
            bleedoutRemaining = 10f;
            bleedoutActive = true;
            reviveActive = false;
            criticalTriggered = false;
            SetBloodVignetteVisible(true);
            SetBloodVignetteIntensity(vignetteMinAlpha);
            SetBloodVignetteScale(vignetteMinScale);

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

            UIFeedbackTester.Play(onKnockedFeedback, this, "기절");
            StartHeartbeatLoop();
        }

        [TitleGroup("Debug")]
        [Button("기절+부활 통합 테스트"), GUIColor(0.7f, 1f, 0.9f)]
        private void TestKnockedReviveFlow()
        {
            if (!EnsureActiveForTest()) return;

            StopReviveTestCoroutine();
            reviveTestCoroutine = StartCoroutine(TestKnockedReviveFlowRoutine());
        }

        private IEnumerator TestKnockedReviveFlowRoutine()
        {
            SetKnockedContentVisible(true);

            const float bleedoutTestDuration = 3f;
            const float reviveTestDuration = 3f;
            const float testBleedoutTotal = 6f;

            bleedoutTotal = testBleedoutTotal;
            bleedoutRemaining = testBleedoutTotal;
            bleedoutActive = true;
            reviveActive = false;
            criticalTriggered = false;
            SetBloodVignetteVisible(true);
            SetBloodVignetteIntensity(vignetteMinAlpha);
            SetBloodVignetteScale(vignetteMinScale);

            foreach (HUDManager hudManager in FindHudManagers())
                hudManager.ApplyKnockedHpModeForUI();

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(true);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(true);

            if (bleedoutText != null)
                bleedoutText.text = $"{Mathf.CeilToInt(bleedoutRemaining)}s";

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 1f;

            UIFeedbackTester.Play(onKnockedFeedback, this, "기절");
            StartHeartbeatLoop();

            float elapsed = 0f;

            while (elapsed < bleedoutTestDuration)
            {
                elapsed += Time.deltaTime;
                bleedoutRemaining = Mathf.Max(0f, testBleedoutTotal - elapsed);
                float ratio = Mathf.Clamp01(bleedoutRemaining / testBleedoutTotal);

                if (bleedoutFill != null)
                    bleedoutFill.fillAmount = ratio;

                if (bleedoutText != null)
                    bleedoutText.text = $"{Mathf.CeilToInt(bleedoutRemaining)}s";

                ApplyBloodVignetteFromKnockedRatio(ratio, "Integrated Test");

                foreach (HUDManager hudManager in FindHudManagers())
                    hudManager.SetKnockedHpBarRatioForUI(ratio, bleedoutRemaining);

                yield return null;
            }

            bleedoutActive = false;
            reviveActive = true;
            StopHeartbeatLoop();

            foreach (HUDManager hudManager in FindHudManagers())
                hudManager.PauseKnockedHpBarForUI();

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(false);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(false);

            if (knockedGuideText != null)
                knockedGuideText.text = reviveStatusLabel;

            if (bleedoutText != null)
                bleedoutText.text = reviveStatusLabel;

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 0f;

            UIFeedbackTester.Play(onReviveStartedFeedback, this, "부활 시작");

            elapsed = 0f;
            while (elapsed < reviveTestDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / reviveTestDuration);

                if (bleedoutFill != null)
                    bleedoutFill.fillAmount = progress;

                yield return null;
            }

            if (bleedoutFill != null)
                bleedoutFill.fillAmount = 1f;

            reviveActive = false;
            bleedoutActive = false;
            ResetVignette();

            if (knockedTitleText != null)
                knockedTitleText.gameObject.SetActive(true);

            if (knockedCountText != null)
                knockedCountText.gameObject.SetActive(true);

            if (knockedGuideText != null)
                knockedGuideText.text = "동료의 응급처치를 기다리세요";

            UIFeedbackTester.Play(onReviveEndedFeedback, this, "부활 종료");
            foreach (HUDManager hudManager in FindHudManagers())
                hudManager.ApplyAliveHpModeForUI();

            reviveTestCoroutine = null;
        }
#endif
    }
}

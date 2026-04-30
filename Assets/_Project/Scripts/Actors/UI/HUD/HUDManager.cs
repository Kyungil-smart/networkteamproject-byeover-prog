using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 인게임 메인 HUD 관리자
// HP/스태미나 바, 상호작용 프롬프트, 상태 패널(Alive/Knocked/Dead) 전환 담당
// EventBus로 PlayerHpChanged / PlayerStaminaChanged / PlayerStateChanged 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// EventBus를 구독해 HUD 위젯을 갱신
    /// 플레이어 상태 변경 시 상태별 패널을 활성화
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        // HP / 스태미나 UI
        [BoxGroup("스탯")]
        [Required, SerializeField] private Image hpFill;// HP Fill 이미지

        [BoxGroup("스탯")]
        [SerializeField] private TMP_Text hpValueText;

        [BoxGroup("스탯")]
        [Required, SerializeField] private Image staminaFill;// 스태미나 Fill 이미지

        [BoxGroup("스탯")]
        [SerializeField] private TMP_Text staminaValueText;

        [BoxGroup("스탯")]
        [MinValue(1f), SerializeField] private float maxHP = 100f;// 최대 HP

        [BoxGroup("스탯")]
        [MinValue(1f), SerializeField] private float maxStamina = 100f;// 최대 스태미나

        [BoxGroup("스탯")]
        [SerializeField] private Color aliveHpColor = new(0.78f, 0.08f, 0.05f, 1f);

        [BoxGroup("스탯")]
        [SerializeField] private Color knockedHpColor = new(1f, 0.45f, 0.2f);

        [BoxGroup("스탯")]
        [Tooltip("비워두면 hpFill만 색상 변경합니다. 추가로 같은 색을 입힐 그래픽이 있으면 지정하세요.")]
        [SerializeField] private Graphic[] hpColorTargets;

        [BoxGroup("스탯")]
        [MinValue(1f), SerializeField] private float fallbackBleedoutSeconds = 60f;

        // 상호작용 프롬프트
        [FoldoutGroup("상호작용 안내")]
        [Required, SerializeField] private GameObject interactPromptRoot;// 프롬프트 루트

        [FoldoutGroup("상호작용 안내")]
        [Required, SerializeField] private TMP_Text interactPromptText;// 프롬프트 문구

        // 상태 패널
        [FoldoutGroup("상태 패널")]
        [Required, SerializeField] private GameObject alivePanel;// 생존 상태 패널

        [FoldoutGroup("상태 패널")]
        [Required, SerializeField] private GameObject knockedPanel;// 기절 상태 패널

        [FoldoutGroup("상태 패널")]
        [Required, SerializeField] private GameObject spectatorPanel;// 사망 관전 패널

        // Feel 피드백 - HP
        [FoldoutGroup("체력 피드백")]
        [Tooltip("체력 감소 시 재생")]
        [SerializeField] private MMF_Player hpDamagedFeedback;

        [FoldoutGroup("체력 피드백")]
        [Tooltip("체력 회복 시 재생")]
        [SerializeField] private MMF_Player hpHealedFeedback;

        [FoldoutGroup("체력 피드백")]
        [Tooltip("저체력 경고가 발동되는 체력 비율 (0~1)")]
        [PropertyRange(0f, 1f), SerializeField] private float lowHpThreshold = 0.3f;

        [FoldoutGroup("체력 피드백")]
        [Tooltip("저체력 임계치를 처음 넘었을 때 1회 재생")]
        [SerializeField] private MMF_Player lowHpEnteredFeedback;

        // Feel 피드백 - 상태 전환
        [FoldoutGroup("상태 피드백")]
        [Tooltip("생존 상태 진입 시 재생 (부활 축하 연출)")]
        [SerializeField] private MMF_Player onAliveFeedback;

        [FoldoutGroup("상태 피드백")]
        [Tooltip("기절 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("상태 피드백")]
        [Tooltip("사망 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onSpectatorFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private float currentHp01 = 1f;// 현재 HP 비율
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool isLowHp;// 저체력 구간 진입 여부
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool isKnockedHpMode;
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool knockedBleedoutActive;
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool knockedReviveActive;
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private float knockedBleedoutRemaining;
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private float knockedBleedoutTotal;

        private float currentHpValue;
        private Color lastAppliedHpColor;
        private bool hasAppliedHpColor;

        private void Awake()
        {
            ShowInteractPrompt(string.Empty);
            RefreshHpUI(maxHP);
            RefreshStaminaUI(maxStamina);
        }

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Subscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
            EventBus.Subscribe<PlayerKnockedEvent>(OnPlayerKnocked);
            EventBus.Subscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Subscribe<ReviveEndedEvent>(OnReviveEnded);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Unsubscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
            EventBus.Unsubscribe<PlayerKnockedEvent>(OnPlayerKnocked);
            EventBus.Unsubscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Unsubscribe<ReviveEndedEvent>(OnReviveEnded);
        }

        private void Update()
        {
            if (!isKnockedHpMode || !knockedBleedoutActive || knockedReviveActive) return;

            knockedBleedoutRemaining = Mathf.Max(0f, knockedBleedoutRemaining - Time.deltaTime);
            RefreshKnockedHpUI();
        }

        // 해당 clientId가 로컬 플레이어인지 판별
        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null
                && clientId == NetworkManager.Singleton.LocalClientId;
        }

        // HP 변경 시 Fill 갱신 + 데미지/회복 구분 피드백 재생
        private void OnHpChanged(PlayerHpChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            Debug.Log($"[HUDManager] PlayerHpChanged old={e.oldValue:F1}, new={e.newValue:F1}", this);

            currentHpValue = e.newValue;
            
            // ⭐ Knocked 모드일 때는 Alive HP 갱신 무시
            if (isKnockedHpMode) return;

            float prev = currentHp01;
            RefreshHpUI(e.newValue);

            // 이전값과 비교해서 데미지인지 회복인지 자동 분기
            if (currentHp01 < prev)
                UIFeedbackTester.Play(hpDamagedFeedback, this, "HUD HP 피해");
            else if (currentHp01 > prev)
                UIFeedbackTester.Play(hpHealedFeedback, this, "HUD HP 회복");

            // 저체력 구간 진입 순간 1회만 경고 피드백 (edge trigger)
            bool nowLow = currentHp01 <= lowHpThreshold && currentHp01 > 0f;
            if (nowLow && !isLowHp)
                UIFeedbackTester.Play(lowHpEnteredFeedback, this, "HUD 저체력 진입");
            isLowHp = nowLow;
        }

        // 스태미나 변경 시 Fill 갱신
        private void OnStaminaChanged(PlayerStaminaChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            Debug.Log($"[HUDManager] PlayerStaminaChanged old={e.oldValue:F1}, new={e.newValue:F1}", this);
            RefreshStaminaUI(e.newValue);
        }

        private void RefreshHpUI(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, maxHP);
            currentHpValue = clamped;
            currentHp01 = Mathf.Clamp01(clamped / maxHP);

            if (hpFill != null)
            {
                ApplyHpColor(aliveHpColor);
                hpFill.fillAmount = currentHp01;
            }

            if (hpValueText != null) hpValueText.text = Mathf.CeilToInt(clamped).ToString();
        }

        private void RefreshKnockedHpUI()
        {
            if (hpFill != null)
            {
                hpFill.gameObject.SetActive(true);
                hpFill.enabled = true;
                ApplyHpColor(knockedHpColor);
                hpFill.fillAmount = GetKnockedBleedoutRatio();
                hpFill.SetVerticesDirty();
                hpFill.SetMaterialDirty();
            }

            if (hpValueText != null)
                hpValueText.text = Mathf.CeilToInt(knockedBleedoutRemaining).ToString();
        }

        /// <summary>
        /// Knocked HP 모드 진입. KnockedHUD의 기절 게이지와 함께 기존 HP바도
        /// 기절 잔여량 표시로 전환한다.
        /// </summary>
        private void EnterKnockedHpMode(float bleedoutSeconds)
        {
            Debug.Log($"[HUDManager] EnterKnockedHpMode bleedout={bleedoutSeconds:F1}", this);
            
            isKnockedHpMode = true;
            knockedBleedoutActive = true;
            knockedReviveActive = false;
            knockedBleedoutTotal = Mathf.Max(1f, bleedoutSeconds);
            knockedBleedoutRemaining = knockedBleedoutTotal;
            RefreshKnockedHpUI();
            LogHpFillState("EnterKnockedHpMode");
        }

        /// <summary>
        /// Alive 모드 복귀 (일반 HP 색상/게이지로 전환)
        /// </summary>
        private void ExitKnockedHpMode()
        {
            Debug.Log("[HUDManager] ExitKnockedHpMode", this);
            
            isKnockedHpMode = false;
            knockedBleedoutActive = false;
            knockedReviveActive = false;
            knockedBleedoutRemaining = 0f;
            knockedBleedoutTotal = 0f;
            
            RefreshHpUI(currentHpValue);
            LogHpFillState("ExitKnockedHpMode");
        }

        private float GetKnockedBleedoutRatio()
        {
            if (knockedBleedoutTotal <= 0f) return 0f;
            return Mathf.Clamp01(knockedBleedoutRemaining / knockedBleedoutTotal);
        }

        private void RefreshStaminaUI(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, maxStamina);
            if (staminaFill != null) staminaFill.fillAmount = Mathf.Clamp01(clamped / maxStamina);
            if (staminaValueText != null) staminaValueText.text = Mathf.CeilToInt(clamped).ToString();
        }

        // 플레이어 상태 변경 시 패널 전환 + 상태별 피드백 재생
        private void OnPlayerStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            Debug.Log($"[HUDManager] PlayerStateChanged {e.oldState} -> {e.newState}", this);

            if (alivePanel != null)     alivePanel.SetActive(e.newState != PlayerState.Dead);
            if (knockedPanel != null)   knockedPanel.SetActive(e.newState == PlayerState.Knocked);
            if (spectatorPanel != null) spectatorPanel.SetActive(e.newState == PlayerState.Dead);

            switch (e.newState)
            {
                case PlayerState.Alive:
                    ExitKnockedHpMode();
                    UIFeedbackTester.Play(onAliveFeedback, this, "HUD 생존 상태");
                    break;
                case PlayerState.Knocked:
                    EnterKnockedHpMode(fallbackBleedoutSeconds);
                    UIFeedbackTester.Play(onKnockedFeedback, this, "HUD 기절 상태");
                    break;
                case PlayerState.Dead:
                    isKnockedHpMode = false;
                    knockedBleedoutActive = false;
                    knockedReviveActive = false;
                    if (hpFill != null)
                    {
                        ApplyHpColor(aliveHpColor);
                        hpFill.fillAmount = 0f;
                    }
                    UIFeedbackTester.Play(onSpectatorFeedback, this, "HUD 관전 상태");
                    break;
            }
        }

        private void OnPlayerKnocked(PlayerKnockedEvent e)
        {
            if (!IsLocalClient(e.victimClientId)) return;

            Debug.Log($"[HUDManager] PlayerKnocked bleedout={e.bleedoutSeconds:F1}", this);

            EnterKnockedHpMode(e.bleedoutSeconds);
        }

        private void OnReviveStarted(ReviveStartedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            Debug.Log($"[HUDManager] ReviveStarted target={e.targetClientId}, pause knocked HP bar", this);
            PauseKnockedHpBarForUI();
        }

        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;

            Debug.Log($"[HUDManager] ReviveEnded target={e.targetClientId}, result={e.result}", this);

            if (e.result == ReviveResult.Completed)
                ApplyAliveHpModeForUI();
            else
                ResumeKnockedHpBarForUI();
        }

        /// <summary>
        /// 테스트용: Knocked HP 모드 진입 (fallback bleedout 사용)
        /// </summary>
        public void ApplyKnockedHpModeForUI()
        {
            Debug.Log($"[HUDManager] ApplyKnockedHpModeForUI hpFill={(hpFill != null ? hpFill.name : "null")}, color={knockedHpColor}", this);
            EnterKnockedHpMode(fallbackBleedoutSeconds);
        }

        /// <summary>
        /// 테스트용: Alive HP 모드 복귀
        /// </summary>
        public void ApplyAliveHpModeForUI()
        {
            Debug.Log("[HUDManager] ApplyAliveHpModeForUI", this);
            ExitKnockedHpMode();
        }

        public void SetKnockedHpBarRatioForUI(float ratio, float remainingSeconds)
        {
            isKnockedHpMode = true;
            knockedBleedoutActive = false;
            knockedBleedoutTotal = Mathf.Max(1f, knockedBleedoutTotal);
            knockedBleedoutRemaining = Mathf.Max(0f, remainingSeconds);

            if (hpFill != null)
            {
                ApplyHpColor(knockedHpColor);
                hpFill.fillAmount = Mathf.Clamp01(ratio);
            }

            if (hpValueText != null)
                hpValueText.text = Mathf.CeilToInt(knockedBleedoutRemaining).ToString();
        }

        public void PauseKnockedHpBarForUI()
        {
            if (!isKnockedHpMode) return;

            knockedReviveActive = true;
            knockedBleedoutActive = false;
            RefreshKnockedHpUI();
        }

        public void ResumeKnockedHpBarForUI()
        {
            if (!isKnockedHpMode) return;

            knockedReviveActive = false;
            knockedBleedoutActive = true;
            RefreshKnockedHpUI();
        }

        /// <summary>
        /// HP 색상 적용 (hpFill과 명시 타겟 또는 자동 타겟)
        /// </summary>
        private void ApplyHpColor(Color color)
        {
            bool shouldLog = ShouldLogHpColor(color);
            int changedCount = 0;
            
            // ⭐ hpFill 자신은 항상 색상 변경
            changedCount += ApplyGraphicColor(hpFill, color, shouldLog);

            // 명시 타겟이 있으면 그것들만 색상 변경
            if (hpColorTargets != null && hpColorTargets.Length > 0)
            {
                foreach (Graphic target in hpColorTargets)
                {
                    changedCount += ApplyGraphicColor(target, color, shouldLog);
                }

                if (shouldLog)
                    Debug.Log($"[HUDManager] ApplyHpColor explicit targets={changedCount}, hpFillColor={(hpFill != null ? hpFill.color.ToString() : "null")}", this);
                return;
            }

            if (shouldLog)
                Debug.Log($"[HUDManager] ApplyHpColor auto targets={changedCount}, hpFillColor={(hpFill != null ? hpFill.color.ToString() : "null")}", this);
        }

        /// <summary>
        /// 같은 색상 중복 로그 방지
        /// </summary>
        private bool ShouldLogHpColor(Color color)
        {
            if (!hasAppliedHpColor || lastAppliedHpColor != color)
            {
                hasAppliedHpColor = true;
                lastAppliedHpColor = color;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 개별 Graphic에 색상 적용 (Text 제외)
        /// </summary>
        private int ApplyGraphicColor(Graphic graphic, Color color, bool logTarget)
        {
            if (graphic == null) return 0;
            if (graphic is TMP_Text) return 0;
            if (graphic is Text) return 0;

            // Inspector에서 Image Color를 바꾸는 것과 같은 경로로만 적용한다.
            graphic.color = color;
            graphic.SetVerticesDirty();
            graphic.SetMaterialDirty();

            if (logTarget)
            {
                Debug.Log($"[HUDManager] HP color target={GetHierarchyPath(graphic.transform)}, " +
                          $"type={graphic.GetType().Name}, active={graphic.gameObject.activeInHierarchy}, " +
                          $"color={graphic.color}, canvasColor={graphic.canvasRenderer.GetColor()}", graphic);
            }

            return 1;
        }

        /// <summary>
        /// HP 색상 타겟 전체 진단 (Odin 버튼용)
        /// </summary>
        private void DumpHpColorTargets()
        {
            if (hpFill == null)
            {
                Debug.LogWarning("[HUDManager] hpFill 참조가 비어 있습니다.", this);
                return;
            }

            Transform root = hpFill.transform.parent != null ? hpFill.transform.parent : hpFill.transform;
            Debug.Log($"[HUDManager] HP target audit root={GetHierarchyPath(root)}, " +
                      $"hpFill={GetHierarchyPath(hpFill.transform)}, " +
                      $"hpFillColor={hpFill.color}, fillAmount={hpFill.fillAmount}", this);

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text || graphic is Text) continue;

                Image image = graphic as Image;
                string imageInfo = image != null
                    ? $", imageType={image.type}, fillAmount={image.fillAmount}, sprite={(image.sprite != null ? image.sprite.name : "null")}"
                    : string.Empty;

                Debug.Log($"[HUDManager] HP graphic={GetHierarchyPath(graphic.transform)}, " +
                          $"type={graphic.GetType().Name}, active={graphic.gameObject.activeInHierarchy}, " +
                          $"color={graphic.color}, rendererColor={graphic.canvasRenderer.GetColor()}{imageInfo}", graphic);
            }
        }

        /// <summary>
        /// hpFill 상태 진단 로그 (모드 전환 직후 호출)
        /// </summary>
        private void LogHpFillState(string context)
        {
            if (hpFill == null) return;

            Debug.Log($"[HUDManager] {context} hpFill state:\n" +
                      $"  - path={GetHierarchyPath(hpFill.transform)}\n" +
                      $"  - active={hpFill.gameObject.activeInHierarchy}\n" +
                      $"  - enabled={hpFill.enabled}\n" +
                      $"  - color={hpFill.color}\n" +
                      $"  - canvasRenderer.color={hpFill.canvasRenderer.GetColor()}\n" +
                      $"  - fillAmount={hpFill.fillAmount}\n" +
                      $"  - siblingIndex={hpFill.transform.GetSiblingIndex()}\n" +
                      $"  - material={(hpFill.material != null ? hpFill.material.name : "null")}\n" +
                      $"  - sprite={(hpFill.sprite != null ? hpFill.sprite.name : "null")}", this);
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null) return "null";

            string path = target.name;
            Transform parent = target.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        // 상호작용 프롬프트 표시/숨김
        public void ShowInteractPrompt(string text)
        {
            if (interactPromptRoot != null) interactPromptRoot.SetActive(!string.IsNullOrEmpty(text));
            if (interactPromptText != null) interactPromptText.text = text;
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("체력 피해 피드백"), GUIColor(1f, 0.7f, 0.7f)]
        private void TestHpDamaged() => UIFeedbackTester.Play(hpDamagedFeedback, this, "HP 피해");

        [TitleGroup("디버그")]
        [Button("체력 회복 피드백"), GUIColor(0.7f, 1f, 0.7f)]
        private void TestHpHealed() => UIFeedbackTester.Play(hpHealedFeedback, this, "HP 회복");

        [TitleGroup("디버그")]
        [Button("체력 회복 화면+피드백 테스트"), GUIColor(0.55f, 1f, 0.55f)]
        private void TestHealValueAndFeedback()
        {
            if (!Application.isPlaying) return;

            float before = currentHp01 * maxHP;
            float healed = Mathf.Min(maxHP, before + 20f);
            RefreshHpUI(healed);
            Debug.Log($"[HUDManager] Test heal UI refreshed before={before:F1}, after={healed:F1}", this);
            UIFeedbackTester.Play(hpHealedFeedback, this, "HUD HP 회복 테스트");
        }

        [TitleGroup("디버그")]
        [Button("저체력 경고 피드백")]
        private void TestLowHp() => UIFeedbackTester.Play(lowHpEnteredFeedback, this, "저체력 경고");

        [TitleGroup("디버그")]
        [Button("기절 체력바 색상 테스트"), GUIColor(1f, 0.55f, 0.25f)]
        private void TestKnockedHpBar()
        {
            if (!Application.isPlaying) return;
            
            Debug.Log("[HUDManager] Test knocked HP bar", this);
            EnterKnockedHpMode(fallbackBleedoutSeconds);
        }

        [TitleGroup("디버그")]
        [Button("일반 체력바 복구 테스트"), GUIColor(0.55f, 1f, 0.55f)]
        private void TestAliveHpBar()
        {
            if (!Application.isPlaying) return;
            ApplyAliveHpModeForUI();
        }

        [TitleGroup("디버그")]
        [Button("체력 색상 대상 진단")]
        private void TestDumpHpColorTargets() => DumpHpColorTargets();
        
        [TitleGroup("디버그")]
        [Button("체력 채움 상태 로그")]
        private void TestLogHpFillState() => LogHpFillState("Manual Test");
#endif
    }

    internal static class UIFeedbackTester
    {
        public static void Play(MMF_Player feedback, Object context, string label)
        {
            if (feedback == null)
            {
                Debug.LogWarning($"{label} 피드백 참조가 비어 있습니다.", context);
                return;
            }

            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{label} 피드백은 Play Mode에서 테스트해주세요.", context);
                return;
            }

            if (!feedback.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"{label} 피드백 오브젝트가 비활성화 상태입니다: {feedback.name}", feedback);
                return;
            }

            feedback.Initialization(feedback.gameObject);

            if (feedback.IsPlaying)
                feedback.StopFeedbacks();

            feedback.PlayFeedbacks();
        }
    }
}

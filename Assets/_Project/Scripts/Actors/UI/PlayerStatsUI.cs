using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 로컬 플레이어의 HP, 스태미나, 상호작용 프롬프트를 표시한다.
    /// </summary>
    public class PlayerStatsUI : MonoBehaviour
    {
        [BoxGroup("Stats")]
        [Required, SerializeField] private Image hpFill;

        [BoxGroup("Stats")]
        [SerializeField] private TMP_Text hpValueText;

        [BoxGroup("Stats")]
        [Required, SerializeField] private Image staminaFill;

        [BoxGroup("Stats")]
        [SerializeField] private TMP_Text staminaValueText;

        [BoxGroup("Stats")]
        [MinValue(1f), SerializeField] private float maxHP = 100f;

        [BoxGroup("Stats")]
        [MinValue(1f), SerializeField] private float maxStamina = 100f;

        [FoldoutGroup("Interact Prompt")]
        [Required, SerializeField] private GameObject interactPromptRoot;

        [FoldoutGroup("Interact Prompt")]
        [Required, SerializeField] private TMP_Text interactPromptText;

        [FoldoutGroup("HP Feedbacks")]
        [Tooltip("HP 감소 시 재생")]
        [SerializeField] private MMF_Player hpDamagedFeedback;

        [FoldoutGroup("HP Feedbacks")]
        [Tooltip("HP 회복 시 재생")]
        [SerializeField] private MMF_Player hpHealedFeedback;

        [FoldoutGroup("HP Feedbacks")]
        [Tooltip("저체력 경고가 발동되는 HP 비율 (0~1)")]
        [PropertyRange(0f, 1f), SerializeField] private float lowHpThreshold = 0.3f;

        [FoldoutGroup("HP Feedbacks")]
        [Tooltip("저체력 임계치를 처음 넘었을 때 1회 재생")]
        [SerializeField] private MMF_Player lowHpEnteredFeedback;

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float currentHp01 = 1f;

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool isLowHp;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Subscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);

            if (interactPromptRoot != null) interactPromptRoot.SetActive(false);
            RefreshHpUI(maxHP);
            RefreshStaminaUI(maxStamina);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Unsubscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null
                && clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void OnHpChanged(PlayerHpChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            Debug.Log($"[PlayerStatsUI] PlayerHpChanged old={e.oldValue:F1}, new={e.newValue:F1}", this);

            float prev = currentHp01;
            RefreshHpUI(e.newValue);

            if (currentHp01 < prev)
                UIFeedbackTester.Play(hpDamagedFeedback, this, "PlayerStats HP 피해");
            else if (currentHp01 > prev)
                UIFeedbackTester.Play(hpHealedFeedback, this, "PlayerStats HP 회복");

            bool nowLow = currentHp01 <= lowHpThreshold && currentHp01 > 0f;
            if (nowLow && !isLowHp)
                UIFeedbackTester.Play(lowHpEnteredFeedback, this, "PlayerStats 저체력 진입");
            isLowHp = nowLow;
        }

        private void OnStaminaChanged(PlayerStaminaChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            Debug.Log($"[PlayerStatsUI] PlayerStaminaChanged old={e.oldValue:F1}, new={e.newValue:F1}", this);
            RefreshStaminaUI(e.newValue);
        }

        private void RefreshHpUI(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, maxHP);
            currentHp01 = Mathf.Clamp01(clamped / maxHP);

            if (hpFill != null) hpFill.fillAmount = currentHp01;
            if (hpValueText != null) hpValueText.text = Mathf.CeilToInt(clamped).ToString();
        }

        private void RefreshStaminaUI(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, maxStamina);
            if (staminaFill != null) staminaFill.fillAmount = Mathf.Clamp01(clamped / maxStamina);
            if (staminaValueText != null) staminaValueText.text = Mathf.CeilToInt(clamped).ToString();
        }

        public void ShowInteractPrompt(string text)
        {
            if (interactPromptRoot != null) interactPromptRoot.SetActive(!string.IsNullOrEmpty(text));
            if (interactPromptText != null) interactPromptText.text = text;
        }

#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("HP 50%로 설정"), GUIColor(1f, 0.8f, 0.5f)]
        private void TestSetHp50()
        {
            RefreshHpUI(maxHP * 0.5f);
        }

        [TitleGroup("Debug")]
        [Button("HP 20%로 설정 (저체력)"), GUIColor(1f, 0.5f, 0.5f)]
        private void TestSetHp20()
        {
            RefreshHpUI(maxHP * 0.2f);
            isLowHp = true;
        }

        [TitleGroup("Debug")]
        [Button("HP 피해 피드백"), GUIColor(1f, 0.7f, 0.7f)]
        private void TestHpDamaged() => UIFeedbackTester.Play(hpDamagedFeedback, this, "PlayerStats HP 피해");

        [TitleGroup("Debug")]
        [Button("HP 회복 피드백"), GUIColor(0.7f, 1f, 0.7f)]
        private void TestHpHealed() => UIFeedbackTester.Play(hpHealedFeedback, this, "PlayerStats HP 회복");

        [TitleGroup("Debug")]
        [Button("HP 회복 UI+피드백 테스트"), GUIColor(0.55f, 1f, 0.55f)]
        private void TestHealValueAndFeedback()
        {
            if (!Application.isPlaying) return;

            float before = currentHp01 * maxHP;
            float healed = Mathf.Min(maxHP, before + 20f);
            RefreshHpUI(healed);
            Debug.Log($"[PlayerStatsUI] Test heal UI refreshed before={before:F1}, after={healed:F1}", this);
            UIFeedbackTester.Play(hpHealedFeedback, this, "PlayerStats HP 회복 테스트");
        }

        [TitleGroup("Debug")]
        [Button("저체력 경고 피드백")]
        private void TestLowHp() => UIFeedbackTester.Play(lowHpEnteredFeedback, this, "PlayerStats 저체력 경고");

        [TitleGroup("Debug")]
        [Button("상호작용 문구 보이기")]
        private void TestShowPrompt() => ShowInteractPrompt("[E] 상호작용");

        [TitleGroup("Debug")]
        [Button("상호작용 문구 숨기기")]
        private void TestHidePrompt() => ShowInteractPrompt(string.Empty);
#endif
    }
}

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
        [BoxGroup("HP / Stamina")]
        [Required, SerializeField] private Image hpFill;// HP Fill 이미지

        [BoxGroup("HP / Stamina")]
        [Required, SerializeField] private Image staminaFill;// 스태미나 Fill 이미지

        [BoxGroup("HP / Stamina")]
        [MinValue(1f), SerializeField] private float maxHP = 100f;// 최대 HP

        [BoxGroup("HP / Stamina")]
        [MinValue(1f), SerializeField] private float maxStamina = 100f;// 최대 스태미나

        // 상호작용 프롬프트
        [FoldoutGroup("Interact Prompt")]
        [Required, SerializeField] private GameObject interactPromptRoot;// 프롬프트 루트

        [FoldoutGroup("Interact Prompt")]
        [Required, SerializeField] private TMP_Text interactPromptText;// 프롬프트 문구

        // 상태 패널
        [FoldoutGroup("State Panels")]
        [Required, SerializeField] private GameObject alivePanel;// 생존 상태 패널

        [FoldoutGroup("State Panels")]
        [Required, SerializeField] private GameObject knockedPanel;// 기절 상태 패널

        [FoldoutGroup("State Panels")]
        [Required, SerializeField] private GameObject spectatorPanel;// 사망 관전 패널

        // Feel 피드백 - HP
        [FoldoutGroup("Feedbacks/HP")]
        [Tooltip("HP 감소 시 재생")]
        [SerializeField] private MMF_Player hpDamagedFeedback;

        [FoldoutGroup("Feedbacks/HP")]
        [Tooltip("HP 회복 시 재생")]
        [SerializeField] private MMF_Player hpHealedFeedback;

        [FoldoutGroup("Feedbacks/HP")]
        [Tooltip("저체력 경고가 발동되는 HP 비율 (0~1)")]
        [PropertyRange(0f, 1f), SerializeField] private float lowHpThreshold = 0.3f;

        [FoldoutGroup("Feedbacks/HP")]
        [Tooltip("저체력 임계치를 처음 넘었을 때 1회 재생")]
        [SerializeField] private MMF_Player lowHpEnteredFeedback;

        // Feel 피드백 - 상태 전환
        [FoldoutGroup("Feedbacks/State")]
        [Tooltip("Alive 상태 진입 시 재생 (부활 축하 연출)")]
        [SerializeField] private MMF_Player onAliveFeedback;

        [FoldoutGroup("Feedbacks/State")]
        [Tooltip("Knocked 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("Feedbacks/State")]
        [Tooltip("Dead 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onSpectatorFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private float currentHp01 = 1f;// 현재 HP 비율
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool isLowHp;// 저체력 구간 진입 여부

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Subscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Unsubscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
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

            float prev = currentHp01;
            currentHp01 = Mathf.Clamp01(e.newValue / maxHP);

            if (hpFill != null) hpFill.fillAmount = currentHp01;

            // 이전값과 비교해서 데미지인지 회복인지 자동 분기
            if (currentHp01 < prev)
                hpDamagedFeedback?.PlayFeedbacks();
            else if (currentHp01 > prev)
                hpHealedFeedback?.PlayFeedbacks();

            // 저체력 구간 진입 순간 1회만 경고 피드백 (edge trigger)
            bool nowLow = currentHp01 <= lowHpThreshold && currentHp01 > 0f;
            if (nowLow && !isLowHp)
                lowHpEnteredFeedback?.PlayFeedbacks();
            isLowHp = nowLow;
        }

        // 스태미나 변경 시 Fill 갱신
        private void OnStaminaChanged(PlayerStaminaChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            if (staminaFill != null) staminaFill.fillAmount = e.newValue / maxStamina;
        }

        // 플레이어 상태 변경 시 패널 전환 + 상태별 피드백 재생
        private void OnPlayerStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            if (alivePanel != null)     alivePanel.SetActive(e.newState == PlayerState.Alive);
            if (knockedPanel != null)   knockedPanel.SetActive(e.newState == PlayerState.Knocked);
            if (spectatorPanel != null) spectatorPanel.SetActive(e.newState == PlayerState.Dead);

            switch (e.newState)
            {
                case PlayerState.Alive:   onAliveFeedback?.PlayFeedbacks();     break;
                case PlayerState.Knocked: onKnockedFeedback?.PlayFeedbacks();   break;
                case PlayerState.Dead:    onSpectatorFeedback?.PlayFeedbacks(); break;
            }
        }

        // 상호작용 프롬프트 표시/숨김
        public void ShowInteractPrompt(string text)
        {
            if (interactPromptRoot != null) interactPromptRoot.SetActive(!string.IsNullOrEmpty(text));
            if (interactPromptText != null) interactPromptText.text = text;
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(1f, 0.7f, 0.7f)]
        private void TestHpDamaged() => hpDamagedFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium), GUIColor(0.7f, 1f, 0.7f)]
        private void TestHpHealed() => hpHealedFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button(ButtonSizes.Medium)]
        private void TestLowHp() => lowHpEnteredFeedback?.PlayFeedbacks();
#endif
    }
}
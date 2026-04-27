using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// EventBus를 구독하여 HUD 위젯을 갱신한다.
    /// 플레이어 상태 변경 시 상태별 패널(Alive/Knocked/Spectator)을 활성화한다.
    /// Canvas_RaidHUD > HUDManager에 부착된다.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [Header("HP / Stamina")]
        [SerializeField] private Image hpFill;
        [SerializeField] private Image staminaFill;
        [SerializeField] private float maxHP = 100f;
        [SerializeField] private float maxStamina = 100f;

        [Header("Interact Prompt")]
        [SerializeField] private GameObject interactPromptRoot;
        [SerializeField] private TMP_Text interactPromptText;

        [Header("State Panels")]
        [SerializeField] private GameObject alivePanel;
        [SerializeField] private GameObject knockedPanel;
        [SerializeField] private GameObject spectatorPanel;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Subscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnHpChanged);
            EventBus.Unsubscribe<PlayerStaminaChangedEvent>(OnStaminaChanged);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        private void OnHpChanged(PlayerHpChangedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;
            if (hpFill != null) hpFill.fillAmount = e.newValue / maxHP;
        }

        private void OnStaminaChanged(PlayerStaminaChangedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;
            if (staminaFill != null) staminaFill.fillAmount = e.newValue / maxStamina;
        }

        private void OnPlayerStateChanged(PlayerStateChangedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;

            if (alivePanel != null)     alivePanel.SetActive(e.newState == PlayerState.Alive);
            if (knockedPanel != null)   knockedPanel.SetActive(e.newState == PlayerState.Knocked);
            if (spectatorPanel != null) spectatorPanel.SetActive(e.newState == PlayerState.Dead);
        }

        public void ShowInteractPrompt(string text)
        {
            if (interactPromptRoot != null) interactPromptRoot.SetActive(!string.IsNullOrEmpty(text));
            if (interactPromptText != null) interactPromptText.text = text;
        }
    }
}

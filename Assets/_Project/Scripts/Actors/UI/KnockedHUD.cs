using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// Knocked 상태 UI: 출혈 타이머 + 부활 진행 바.
    /// 로컬 플레이어가 Knocked 상태에 진입하면 HUDManager가 활성화한다.
    /// </summary>
    public class KnockedHUD : MonoBehaviour
    {
        [Header("Bleedout")]
        [SerializeField] private TMP_Text bleedoutText;
        [SerializeField] private Image bleedoutFill;

        [Header("Revive")]
        [SerializeField] private GameObject revivePanel;
        [SerializeField] private Image reviveProgressFill;
        [SerializeField] private TMP_Text reviveStatusText;

        private float bleedoutTotal;
        private float bleedoutRemaining;
        private bool bleedoutActive;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Subscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Subscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Subscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Subscribe<ReviveEndedEvent>(OnReviveEnded);

            if (revivePanel != null) revivePanel.SetActive(false);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerKnockedEvent>(OnKnocked);
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnStateChanged);
            EventBus.Unsubscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Unsubscribe<ReviveProgressEvent>(OnReviveProgress);
            EventBus.Unsubscribe<ReviveEndedEvent>(OnReviveEnded);
        }

        private void Update()
        {
            if (!bleedoutActive) return;
            bleedoutRemaining = Mathf.Max(0f, bleedoutRemaining - Time.deltaTime);
            if (bleedoutText != null) bleedoutText.text = $"{Mathf.CeilToInt(bleedoutRemaining)}s";
            if (bleedoutFill != null && bleedoutTotal > 0)
                bleedoutFill.fillAmount = bleedoutRemaining / bleedoutTotal;
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void OnKnocked(PlayerKnockedEvent e)
        {
            if (!IsLocalClient(e.victimClientId)) return;
            bleedoutTotal = e.bleedoutSeconds;
            bleedoutRemaining = e.bleedoutSeconds;
            bleedoutActive = true;
        }

        private void OnStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            if (e.newState != PlayerState.Knocked)
            {
                bleedoutActive = false;
                if (revivePanel != null) revivePanel.SetActive(false);
            }
        }

        private void OnReviveStarted(ReviveStartedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;
            if (revivePanel != null) revivePanel.SetActive(true);
            if (reviveStatusText != null) reviveStatusText.text = "Being revived...";
            if (reviveProgressFill != null) reviveProgressFill.fillAmount = 0f;
        }

        private void OnReviveProgress(ReviveProgressEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;
            if (reviveProgressFill != null) reviveProgressFill.fillAmount = e.progress01;
        }

        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (!IsLocalClient(e.targetClientId)) return;
            if (revivePanel != null) revivePanel.SetActive(false);
        }
    }
}

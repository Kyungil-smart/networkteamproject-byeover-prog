using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    public class ExtractionUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text extractionNameText;
        [SerializeField] private Image progressFill;

        private float timeRemaining;
        private float totalTime;
        private bool active;

        private void Awake() { if (panelRoot != null) panelRoot.SetActive(false); }

        private void OnEnable()
        {
            EventBus.Subscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Subscribe<ExtractionCompletedEvent>(OnCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Unsubscribe<ExtractionCompletedEvent>(OnCompleted);
        }

        private void OnStarted(ExtractionStartedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;

            active = true;
            timeRemaining = e.countdownSeconds;
            totalTime = e.countdownSeconds;
            if (panelRoot != null) panelRoot.SetActive(true);
            if (extractionNameText != null) extractionNameText.text = e.extractionId.ToString();
        }

        private void OnCompleted(ExtractionCompletedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.clientId != NetworkManager.Singleton.LocalClientId) return;

            active = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (!active) return;
            timeRemaining = Mathf.Max(0, timeRemaining - Time.deltaTime);
            if (countdownText != null) countdownText.text = timeRemaining.ToString("F1");
            if (progressFill != null && totalTime > 0)
                progressFill.fillAmount = 1f - (timeRemaining / totalTime);
        }
    }
}

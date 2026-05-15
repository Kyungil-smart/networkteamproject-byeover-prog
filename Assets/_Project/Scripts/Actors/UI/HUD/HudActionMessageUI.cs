using System.Collections;
using TMPro;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class HudActionMessageUI : MonoBehaviour
    {
        public static HudActionMessageUI Instance { get; private set; }

        [Header("액션 메시지")]
        [SerializeField] private GameObject messageRoot;
        [SerializeField] private TMP_Text messageText;

        [Header("표시 시간")]
        [SerializeField] private float showDuration = 1.2f;

        private Coroutine hideRoutine;

        private void Awake()
        {
            Instance = this;

            if (messageRoot != null)
                messageRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ShowMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (messageRoot == null || messageText == null)
            {
                Debug.LogWarning("[HudActionMessageUI] messageRoot 또는 messageText가 연결되지 않았습니다.", this);
                return;
            }

            messageText.text = message;
            messageRoot.SetActive(true);

            if (hideRoutine != null)
                StopCoroutine(hideRoutine);

            hideRoutine = StartCoroutine(HideRoutine());
        }

        private IEnumerator HideRoutine()
        {
            yield return new WaitForSeconds(showDuration);

            if (messageRoot != null)
                messageRoot.SetActive(false);

            hideRoutine = null;
        }
        public void ShowPersistentMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (messageRoot == null || messageText == null)
            {
                Debug.LogWarning("[HudActionMessageUI] messageRoot 또는 messageText가 연결되지 않았습니다.", this);
                return;
            }

            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            messageText.text = message;
            messageRoot.SetActive(true);
        }

        public void HideMessage()
        {
            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            if (messageRoot != null)
                messageRoot.SetActive(false);
        }
    }
}
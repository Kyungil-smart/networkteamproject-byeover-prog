using DeadZone.Core;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors
{
    public class ExtractionUI : MonoBehaviour
    {
        [BoxGroup("References")]
        [Required, SerializeField] private GameObject panelRoot;

        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text countdownText;

        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text extractionNameText;

        [BoxGroup("References")]
        [Required, SerializeField] private Image progressFill;

        [BoxGroup("References")]
        [SerializeField] private Button confirmYesButton;

        [BoxGroup("References")]
        [SerializeField] private Button confirmNoButton;

        [FoldoutGroup("Feedback")]
        [SerializeField] private MMF_Player onStartFeedback;

        [FoldoutGroup("Feedback")]
        [SerializeField] private MMF_Player onTickFeedback;

        [FoldoutGroup("Feedback")]
        [SerializeField] private MMF_Player onFinalCountdownTickFeedback;

        [FoldoutGroup("Feedback")]
        [SerializeField] private MMF_Player onCompletedFeedback;

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private bool promptActive;

        private void Awake()
        {
            EnsureConfirmationButtons();
            SetPanelVisible(false);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Subscribe<ExtractionCompletedEvent>(OnCompleted);
            EventBus.Subscribe<ExtractionCanceledEvent>(OnCanceled);
            BindConfirmationButtons();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ExtractionStartedEvent>(OnStarted);
            EventBus.Unsubscribe<ExtractionCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<ExtractionCanceledEvent>(OnCanceled);
            UnbindConfirmationButtons();
        }

        private void OnStarted(ExtractionStartedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId))
                return;

            Debug.Log($"[ExtractionUI] Extraction confirmation opened id={e.extractionId}", this);

            promptActive = true;
            SetPanelVisible(true);

            if (extractionNameText != null)
                extractionNameText.text = e.extractionId.ToString();

            if (countdownText != null)
                countdownText.text = "은신처로 돌아가시겠습니까?";

            if (progressFill != null)
                progressFill.fillAmount = 1f;

            SetConfirmationButtonsVisible(true);
            UIFeedbackTester.Play(onStartFeedback, this, "탈출 확인");
        }

        private void OnCompleted(ExtractionCompletedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId))
                return;

            Debug.Log($"[ExtractionUI] Extraction confirmed id={e.extractionId}", this);
            promptActive = false;
            SetPanelVisible(false);
            UIFeedbackTester.Play(onCompletedFeedback, this, "탈출 완료");
        }

        private void OnCanceled(ExtractionCanceledEvent e)
        {
            if (!IsLocalClientEvent(e.clientId))
                return;

            Debug.Log($"[ExtractionUI] Extraction canceled id={e.extractionId}", this);
            promptActive = false;
            SetPanelVisible(false);
        }

        private static bool IsLocalClientEvent(ulong clientId)
        {
            return NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void BindConfirmationButtons()
        {
            if (confirmYesButton != null)
            {
                confirmYesButton.onClick.RemoveListener(HandleConfirmYesClicked);
                confirmYesButton.onClick.AddListener(HandleConfirmYesClicked);
            }

            if (confirmNoButton != null)
            {
                confirmNoButton.onClick.RemoveListener(HandleConfirmNoClicked);
                confirmNoButton.onClick.AddListener(HandleConfirmNoClicked);
            }
        }

        private void UnbindConfirmationButtons()
        {
            if (confirmYesButton != null)
                confirmYesButton.onClick.RemoveListener(HandleConfirmYesClicked);

            if (confirmNoButton != null)
                confirmNoButton.onClick.RemoveListener(HandleConfirmNoClicked);
        }

        private void HandleConfirmYesClicked()
        {
            ExtractionZone.ConfirmCurrentPrompt();
        }

        private void HandleConfirmNoClicked()
        {
            ExtractionZone.CancelCurrentPrompt();
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelRoot != null)
                panelRoot.SetActive(visible);

            SetConfirmationButtonsVisible(visible);
        }

        private void SetConfirmationButtonsVisible(bool visible)
        {
            if (confirmYesButton != null)
                confirmYesButton.gameObject.SetActive(visible);

            if (confirmNoButton != null)
                confirmNoButton.gameObject.SetActive(visible);
        }

        private void EnsureConfirmationButtons()
        {
            if (panelRoot == null)
                return;

            confirmYesButton ??= FindButton(panelRoot.transform, "Btn_Yes", "Button_Yes", "Yes");
            confirmNoButton ??= FindButton(panelRoot.transform, "Btn_No", "Button_No", "No");

            if (confirmYesButton != null && confirmNoButton != null)
                return;

            RectTransform buttonRoot = CreateButtonRoot(panelRoot.transform);
            confirmYesButton ??= CreateConfirmButton(buttonRoot, "Btn_Yes", "YES");
            confirmNoButton ??= CreateConfirmButton(buttonRoot, "Btn_No", "NO");
        }

        private static Button FindButton(Transform root, params string[] names)
        {
            if (root == null || names == null)
                return null;

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                for (int j = 0; j < names.Length; j++)
                {
                    if (button.name == names[j])
                        return button;
                }
            }

            return null;
        }

        private static RectTransform CreateButtonRoot(Transform parent)
        {
            GameObject root = new("ExtractionConfirmButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            root.transform.SetParent(parent, false);

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 48f);
            rect.sizeDelta = new Vector2(260f, 56f);

            HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rect;
        }

        private static Button CreateConfirmButton(Transform parent, string objectName, string label)
        {
            GameObject buttonObject = new(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.05f, 0.35f, 0.65f, 0.95f);

            GameObject textObject = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 24f;
            text.color = Color.white;

            return buttonObject.GetComponent<Button>();
        }
    }
}

using System.Collections;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class LobbyTabButtonUI : MonoBehaviour, IPointerClickHandler
    {
        [Title("참조")]
        [Required]
        [SerializeField] private Button button;

        [Required]
        [SerializeField] private TMP_Text label;

        [SerializeField] private Image underline;

        [Title("색상")]
        [SerializeField] private Color normalTextColor = new Color(1f, 1f, 1f, 0.25f);

        [SerializeField] private Color selectedTextColor = new Color(0.8f, 0.95f, 1f, 1f);

        [Title("애니메이션")]
        [MinValue(0.01f)]
        [SerializeField] private float tweenDuration = 0.15f;

        [Title("디버그")]
        [SerializeField] private bool logClickEvents = true;

        private RectTransform underlineRect;
        private Coroutine selectionRoutine;
        private UnityAction buttonRelayAction;
        private int lastNotifyFrame = -1;

        public Button Button => button;
        public event UnityAction Clicked;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            EnsureReady();
            BindButtonRelay();
            SetSelectedInstant(false);
        }

        private void OnDestroy()
        {
            UnbindButtonRelay();
        }

        public bool EnsureReady()
        {
            AutoBindReferences();
            BindButtonRelay();
            return button != null;
        }

        public string GetSearchText()
        {
            AutoBindReferences();

            string searchText = name;
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null)
                    continue;

                searchText += " " + texts[i].name + " " + texts[i].text;
            }

            return searchText;
        }

        public void SetSelected(bool selected)
        {
            if (selectionRoutine != null)
                StopCoroutine(selectionRoutine);

            selectionRoutine = StartCoroutine(AnimateSelected(selected));
        }

        public void SetSelectedInstant(bool selected)
        {
            if (selectionRoutine != null)
            {
                StopCoroutine(selectionRoutine);
                selectionRoutine = null;
            }

            if (label != null)
                label.color = selected ? selectedTextColor : normalTextColor;

            if (underline != null)
            {
                Color color = underline.color;
                color.a = selected ? 1f : 0f;
                underline.color = color;
                underline.gameObject.SetActive(selected);
            }

            if (underlineRect != null)
                underlineRect.localScale = new Vector3(selected ? 1f : 0.2f, 1f, 1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Button.onClick이 어떤 이유로 바인딩되지 않아도 탭 루트 클릭을 컨트롤러에 전달한다.
            NotifyClicked();
        }

        [Button("참조 자동 연결")]
        private void AutoBindReferences()
        {
            if (button == null)
                button = GetComponent<Button>();

            if (button == null)
                button = GetComponentInChildren<Button>(true);

            if (label == null)
                label = GetComponentInChildren<TMP_Text>(true);

            if (underline == null)
                underline = FindUnderlineImage();

            underlineRect = underline != null ? underline.rectTransform : null;
        }

        private void BindButtonRelay()
        {
            if (button == null || buttonRelayAction != null)
                return;

            buttonRelayAction = NotifyClicked;
            button.onClick.AddListener(buttonRelayAction);
        }

        private void UnbindButtonRelay()
        {
            if (button == null || buttonRelayAction == null)
                return;

            button.onClick.RemoveListener(buttonRelayAction);
            buttonRelayAction = null;
        }

        private void NotifyClicked()
        {
            if (button != null && !button.interactable)
                return;

            if (lastNotifyFrame == Time.frameCount)
                return;

            lastNotifyFrame = Time.frameCount;

            if (logClickEvents)
                Debug.Log($"[LobbyTabButtonUI] Clicked: {name}, Button={button}", this);

            Clicked?.Invoke();
        }

        [Button("선택 상태 테스트")]
        private void TestSelected()
        {
            SetSelected(true);
        }

        [Button("비선택 상태 테스트")]
        private void TestUnselected()
        {
            SetSelected(false);
        }

        private IEnumerator AnimateSelected(bool selected)
        {
            float duration = Mathf.Max(0.01f, tweenDuration);
            float elapsed = 0f;

            Color startLabelColor = label != null ? label.color : Color.white;
            Color endLabelColor = selected ? selectedTextColor : normalTextColor;

            float startAlpha = underline != null ? underline.color.a : 0f;
            float endAlpha = selected ? 1f : 0f;

            float startScaleX = underlineRect != null ? underlineRect.localScale.x : 1f;
            float endScaleX = selected ? 1f : 0.2f;

            if (underline != null)
                underline.gameObject.SetActive(true);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = 1f - Mathf.Pow(1f - t, 3f);

                if (label != null)
                    label.color = Color.Lerp(startLabelColor, endLabelColor, t);

                if (underline != null)
                {
                    Color color = underline.color;
                    color.a = Mathf.Lerp(startAlpha, endAlpha, t);
                    underline.color = color;
                }

                if (underlineRect != null)
                {
                    Vector3 scale = underlineRect.localScale;
                    scale.x = Mathf.Lerp(startScaleX, endScaleX, t);
                    underlineRect.localScale = scale;
                }

                yield return null;
            }

            if (!selected && underline != null)
                underline.gameObject.SetActive(false);

            selectionRoutine = null;
        }

        private Image FindUnderlineImage()
        {
            Image[] images = GetComponentsInChildren<Image>(true);

            foreach (Image image in images)
            {
                if (image == null || image.gameObject == gameObject)
                    continue;

                if (image.name.ToLowerInvariant().Contains("underline"))
                    return image;
            }

            return null;
        }
    }
}

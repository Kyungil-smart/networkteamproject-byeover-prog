using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class ControlGuidePanelUI : MonoBehaviour
    {
        private const string RuntimeObjectName = "[ControlGuidePanelUI]";
        private const string DefaultGuideSpritePath = "UI/ControlGuide/조작법";
        private const int OverlaySortingOrder = 5000;

        [Header("조작법 패널")]
        [Tooltip("H키를 눌렀을 때 화면 중앙에 띄울 조작법 이미지 패널입니다.")]
        [SerializeField] private GameObject controlGuidePanel;

        [Tooltip("패널 안에 실제 조작법 PNG 스프라이트를 표시하는 이미지입니다.")]
        [SerializeField] private Image controlGuideImage;

        [Tooltip("화면 좌측에 항상 표시할 '조작법 = H' 안내 텍스트입니다.")]
        [SerializeField] private TextMeshProUGUI helpHintText;

        [Header("입력")]
        [Tooltip("조작법 패널을 열고 닫을 키입니다. 기본값은 H입니다.")]
        [SerializeField] private Key toggleKey = Key.H;

        [Tooltip("게임 시작 시 조작법 패널을 닫아둘지 정합니다.")]
        [SerializeField] private bool hidePanelOnStart = true;

        [Header("자동 생성")]
        [Tooltip("씬에 조작법 UI가 연결되어 있지 않으면 실행 중 자동으로 Canvas, 안내 텍스트, 이미지 패널을 만듭니다.")]
        [SerializeField] private bool autoCreateUiWhenMissing = true;

        [Tooltip("Resources 폴더 기준 조작법 이미지 경로입니다. 확장자는 쓰지 않습니다.")]
        [SerializeField] private string guideSpriteResourcePath = DefaultGuideSpritePath;

        [Tooltip("Resources 경로 대신 직접 넣을 조작법 이미지 스프라이트입니다. 비워두면 위 Resources 경로를 사용합니다.")]
        [SerializeField] private Sprite guideSpriteOverride;

        [Header("안내 텍스트 위치")]
        [Tooltip("안내 텍스트의 기준점입니다. (0, 0.5)는 화면 왼쪽 중앙, (0, 1)은 화면 왼쪽 위입니다.")]
        [SerializeField] private Vector2 helpHintAnchor = new(1f, 1f);

        [Tooltip("안내 텍스트의 피벗입니다. 보통 Anchor와 같은 값으로 두면 위치 조절이 쉽습니다.")]
        [SerializeField] private Vector2 helpHintPivot = new(1f, 1f);

        [Tooltip("안내 텍스트 위치입니다. X는 오른쪽/왼쪽, Y는 위/아래 이동입니다.")]
        [SerializeField] private Vector2 helpHintAnchoredPosition = new(-310f, -12f);

        [Tooltip("안내 텍스트 박스 크기입니다.")]
        [SerializeField] private Vector2 helpHintSize = new(180f, 44f);

        [Tooltip("안내 텍스트에 표시할 문구입니다.")]
        [SerializeField] private string helpHintMessage = "조작법 = H";

        [Tooltip("안내 텍스트 글자 크기입니다.")]
        [SerializeField, Min(1f)] private float helpHintFontSize = 28f;

        [Tooltip("안내 텍스트 색상입니다.")]
        [SerializeField] private Color helpHintColor = new(1f, 0.86f, 0.27f, 1f);

        [Header("조작법 이미지 위치")]
        [Tooltip("조작법 이미지의 기준점입니다. 기본값은 화면 중앙입니다.")]
        [SerializeField] private Vector2 guideImageAnchor = new(0.5f, 0.5f);

        [Tooltip("조작법 이미지의 피벗입니다. 기본값은 중앙입니다.")]
        [SerializeField] private Vector2 guideImagePivot = new(0.5f, 0.5f);

        [Tooltip("조작법 이미지 위치입니다. X는 오른쪽/왼쪽, Y는 위/아래 이동입니다.")]
        [SerializeField] private Vector2 guideImageAnchoredPosition = Vector2.zero;

        [Tooltip("조작법 이미지 박스 크기입니다.")]
        [SerializeField] private Vector2 guideImageSize = new(1672f, 941f);

        [Tooltip("조작법 이미지 확대/축소 비율입니다.")]
        [SerializeField, Min(0.01f)] private float guideImageScale = 0.92f;

        [Tooltip("조작법 패널 뒤 어두운 배경의 투명도입니다. 0이면 투명, 1이면 완전 검정입니다.")]
        [SerializeField, Range(0f, 1f)] private float dimBackgroundAlpha = 0.62f;

        private Image dimBackgroundImage;

        private void Awake()
        {
            if (autoCreateUiWhenMissing)
                EnsureRuntimeUi();

            ApplyInspectorSettings();

            if (hidePanelOnStart)
                SetPanelVisible(false);
        }

        private void OnValidate()
        {
            ApplyInspectorSettings();
        }

        private void Update()
        {
            if (Keyboard.current == null || toggleKey == Key.None)
                return;

            if (!Keyboard.current[toggleKey].wasPressedThisFrame)
                return;

            Toggle();
        }

        public void Toggle()
        {
            bool nextVisible = controlGuidePanel == null || !controlGuidePanel.activeSelf;
            SetPanelVisible(nextVisible);
        }

        public void Open()
        {
            SetPanelVisible(true);
        }

        public void Close()
        {
            SetPanelVisible(false);
        }

        private void SetPanelVisible(bool visible)
        {
            if (controlGuidePanel == null)
                return;

            controlGuidePanel.SetActive(visible);
        }

        private void EnsureRuntimeUi()
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                canvas = CreateOverlayCanvas(transform);

            if (helpHintText == null)
                helpHintText = CreateHelpHintText(canvas.transform);

            if (controlGuidePanel == null)
                CreateControlGuidePanel(canvas.transform);

            if (controlGuideImage != null && controlGuideImage.sprite == null)
                controlGuideImage.sprite = ResolveGuideSprite();

            ApplyInspectorSettings();
        }

        private static Canvas CreateOverlayCanvas(Transform parent)
        {
            GameObject canvasObject = new GameObject("Canvas_ControlGuide", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = OverlaySortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private static TextMeshProUGUI CreateHelpHintText(Transform parent)
        {
            GameObject textObject = new GameObject("Text_HelpHint", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Outline));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.raycastTarget = false;

            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        private void CreateControlGuidePanel(Transform parent)
        {
            controlGuidePanel = new GameObject("Panel_ControlGuide", typeof(RectTransform), typeof(CanvasGroup));
            controlGuidePanel.transform.SetParent(parent, false);

            RectTransform panelRect = controlGuidePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            CanvasGroup panelGroup = controlGuidePanel.GetComponent<CanvasGroup>();
            panelGroup.blocksRaycasts = false;
            panelGroup.interactable = false;

            GameObject dimObject = new GameObject("Image_DimBackground", typeof(RectTransform), typeof(Image));
            dimObject.transform.SetParent(controlGuidePanel.transform, false);

            RectTransform dimRect = dimObject.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;

            Image dimImage = dimObject.GetComponent<Image>();
            dimImage.raycastTarget = false;
            dimBackgroundImage = dimImage;

            GameObject guideObject = new GameObject("Image_ControlGuide", typeof(RectTransform), typeof(Image));
            guideObject.transform.SetParent(controlGuidePanel.transform, false);

            controlGuideImage = guideObject.GetComponent<Image>();
            controlGuideImage.sprite = ResolveGuideSprite();
            controlGuideImage.preserveAspect = true;
            controlGuideImage.raycastTarget = false;

            ApplyInspectorSettings();
        }

        private void ApplyInspectorSettings()
        {
            ApplyHelpHintSettings();
            ApplyGuideImageSettings();
            ApplyDimBackgroundSettings();
        }

        private void ApplyHelpHintSettings()
        {
            if (helpHintText == null)
                return;

            RectTransform rect = helpHintText.rectTransform;
            rect.anchorMin = helpHintAnchor;
            rect.anchorMax = helpHintAnchor;
            rect.pivot = helpHintPivot;
            rect.anchoredPosition = helpHintAnchoredPosition;
            rect.sizeDelta = helpHintSize;

            helpHintText.text = helpHintMessage;
            helpHintText.fontSize = helpHintFontSize;
            helpHintText.color = helpHintColor;
            helpHintText.raycastTarget = false;
        }

        private void ApplyGuideImageSettings()
        {
            if (controlGuideImage == null)
                return;

            RectTransform rect = controlGuideImage.rectTransform;
            rect.anchorMin = guideImageAnchor;
            rect.anchorMax = guideImageAnchor;
            rect.pivot = guideImagePivot;
            rect.anchoredPosition = guideImageAnchoredPosition;
            rect.sizeDelta = guideImageSize;
            rect.localScale = Vector3.one * guideImageScale;

            Sprite sprite = ResolveGuideSprite();
            if (sprite != null)
                controlGuideImage.sprite = sprite;

            controlGuideImage.preserveAspect = true;
            controlGuideImage.raycastTarget = false;
        }

        private void ApplyDimBackgroundSettings()
        {
            if (dimBackgroundImage == null && controlGuidePanel != null)
                dimBackgroundImage = controlGuidePanel.transform.Find("Image_DimBackground")?.GetComponent<Image>();

            if (dimBackgroundImage == null)
                return;

            dimBackgroundImage.color = new Color(0f, 0f, 0f, dimBackgroundAlpha);
            dimBackgroundImage.raycastTarget = false;
        }

        private Sprite ResolveGuideSprite()
        {
            if (guideSpriteOverride != null)
                return guideSpriteOverride;

            return Resources.Load<Sprite>(guideSpriteResourcePath);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeRuntimeController()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureControllerForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureControllerForScene(scene);
        }

        private static void EnsureControllerForScene(Scene scene)
        {
            if (!IsInGameScene(scene))
                return;

            ControlGuidePanelUI[] controllers = FindObjectsByType<ControlGuidePanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                ControlGuidePanelUI controller = controllers[i];
                if (controller != null && controller.gameObject.scene == scene)
                    return;
            }

            GameObject controllerObject = new GameObject(RuntimeObjectName);
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            controllerObject.AddComponent<ControlGuidePanelUI>();
        }

        private static bool IsInGameScene(Scene scene)
        {
            return scene.IsValid() && (scene.name == "Game_Stage_1" || scene.name == "Game_Stage_2");
        }
    }
}

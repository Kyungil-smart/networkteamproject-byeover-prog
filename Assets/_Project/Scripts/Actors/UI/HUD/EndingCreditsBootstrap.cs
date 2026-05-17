using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class EndingCreditsBootstrap : MonoBehaviour
    {
        private const string EndingSceneName = "Ending";
        private const string RootName = "__EndingCredits";
        private const float ScrollSpeed = 70f;

        private static bool registered;

        private RectTransform contentRoot;
        private float endY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSceneHook()
        {
            if (!registered)
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
                registered = true;
            }

            TryCreateForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryCreateForScene(scene);
        }

        private static void TryCreateForScene(Scene scene)
        {
            if (!scene.IsValid() || scene.name != EndingSceneName)
                return;

            if (GameObject.Find(RootName) != null)
                return;

            EnsureCamera();

            GameObject root = new(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(EndingCreditsBootstrap));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject background = new("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            Stretch(backgroundRect);
            background.GetComponent<Image>().color = Color.black;

            GameObject content = new("CreditsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            content.transform.SetParent(root.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0f);
            contentRect.anchorMax = new Vector2(0.5f, 0f);
            contentRect.pivot = new Vector2(0.5f, 0f);
            contentRect.anchoredPosition = new Vector2(0f, -1080f);
            contentRect.sizeDelta = new Vector2(1400f, 1600f);

            TextMeshProUGUI text = content.GetComponent<TextMeshProUGUI>();
            text.text = BuildCreditsText();
            text.color = Color.white;
            text.fontSize = 38f;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.richText = true;
            text.raycastTarget = false;

            EndingCreditsBootstrap scroller = root.GetComponent<EndingCreditsBootstrap>();
            scroller.contentRoot = contentRect;
            scroller.endY = 2700f;
        }

        private void Update()
        {
            if (contentRoot == null)
                return;

            Vector2 position = contentRoot.anchoredPosition;
            position.y = Mathf.Min(endY, position.y + ScrollSpeed * Time.deltaTime);
            contentRoot.anchoredPosition = position;
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
                return;

            GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string BuildCreditsText()
        {
            return
                "<size=78>플레이 해주셔서 감사합니다.</size>\n\n\n" +
                "<size=46>개발팀</size>\n" +
                "옥황상제의 노예들\n\n" +
                "정승우\n" +
                "기획 및 아키텍쳐\n\n" +
                "홍정옥\n" +
                "UI\n\n" +
                "이상현\n" +
                "플레이어 및 네트워크\n\n" +
                "조규민\n" +
                "시설\n\n" +
                "강현우\n" +
                "파밍 아이템 데이터\n\n" +
                "강세환\n" +
                "카메라 및 전투 시스템\n\n\n" +
                "<size=46>에셋</size>\n" +
                "Quirky's Easy Animatable Vignette\n" +
                "https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/quirky-s-easy-animatable-vignette-177253";
        }
    }
}

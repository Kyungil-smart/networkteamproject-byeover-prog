using System.Threading.Tasks;

using TMPro;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using DeadZone.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 전역 로딩 화면 표시와 일반 Unity 씬 전환을 담당합니다.
    /// Netcode 씬 전환은 ShowForNetworkLoad/Hide로 표시만 제어하고,
    /// 실제 씬 로드는 NetworkSceneManager가 계속 담당해야 합니다.
    /// </summary>
    public sealed class LoadingScreenService : MonoBehaviour
    {
        [Header("로딩 프리팹")]
        [SerializeField] private GameObject loadingPrefab;
        [SerializeField] private bool instantiatePrefabOnAwake = true;
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("루트")]
        [SerializeField] private GameObject loadingRoot;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("진행률 UI")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private string progressFormat = "{0}%";

        [Header("표시 옵션")]
        [SerializeField, Min(0f)] private float minimumVisibleSeconds = 1.5f;
        [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
        [SerializeField] private bool hideOnUnitySceneLoaded;
        [SerializeField] private bool hideOnNetworkSceneChanged;
        [SerializeField] private bool forceOverlayCanvas = true;
        [SerializeField] private int loadingCanvasSortOrder = 30000;

        [Header("로그")]
        [SerializeField] private bool logDebug;

        private static LoadingScreenService instance;

        private bool isVisible;
        private bool isNetworkControlledLoading;
        private float shownAtRealtime;

        public static LoadingScreenService Instance => instance;
        public bool IsVisible => isVisible;
        public bool IsLoading => isVisible;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            ServiceLocator.Register(this);

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            if (instantiatePrefabOnAwake)
                EnsureLoadingRoot();

            SetRootActive(false);
            SetProgress(0f);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleUnitySceneLoaded;
            EventBus.Subscribe<SceneChangedEvent>(HandleNetworkSceneChanged);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
            EventBus.Unsubscribe<SceneChangedEvent>(HandleNetworkSceneChanged);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                ServiceLocator.Unregister(this);
                instance = null;
            }
        }

        public static void LoadSceneOrFallback(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            string fallbackSceneName = null)
        {
            Debug.Log($"[SceneLoad] Request scene={sceneName}");

            if (!TryResolveLoadableScene(sceneName, fallbackSceneName, out string resolvedSceneName))
                return;

            LoadingScreenService service = Resolve();

            if (service == null)
            {
                SceneManager.LoadScene(resolvedSceneName, mode);
                return;
            }

            _ = service.LoadSceneAsync(resolvedSceneName, mode);
        }

        public static void ShowForNetworkLoadOrFallback(string sceneName)
        {
            LoadingScreenService service = Resolve();
            if (service == null)
                return;

            service.isNetworkControlledLoading = true;
            service.Show(sceneName);
        }

        public void Show(string sceneName = null)
        {
            EnsureLoadingRoot();

            if (!isVisible)
                shownAtRealtime = Time.realtimeSinceStartup;

            isVisible = true;
            SetRootActive(true);
            SetProgress(0f);
            ConfigureLoadingCanvas();

            LoadingTipView tipView = loadingRoot != null
                ? loadingRoot.GetComponentInChildren<LoadingTipView>(true)
                : null;

            if (tipView != null)
                tipView.ShowRandomTipImmediate();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }

            if (logDebug)
                Debug.Log($"[LoadingScreenService] Show. Scene={sceneName}", this);
        }

        public async Task HideAsync()
        {
            if (!isVisible)
                return;

            float elapsed = Time.realtimeSinceStartup - shownAtRealtime;
            float remaining = minimumVisibleSeconds - elapsed;

            if (remaining > 0f)
                await DelaySecondsRealtime(remaining);

            if (fadeDuration > 0f)
                await FadeCanvasGroupAsync(0f, fadeDuration);

            HideImmediate();
        }

        public void HideImmediate()
        {
            isNetworkControlledLoading = false;
            isVisible = false;
            SetProgress(1f);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            SetRootActive(false);

            if (logDebug)
                Debug.Log("[LoadingScreenService] Hide.", this);
        }

        public async Task LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning("[LoadingScreenService] 씬 이름이 비어 있어 로딩을 시작할 수 없습니다.", this);
                return;
            }

            if (!TryResolveLoadableScene(sceneName, null, out string resolvedSceneName))
                return;

            isNetworkControlledLoading = false;
            Show(resolvedSceneName);

            AsyncOperation operation = SceneManager.LoadSceneAsync(resolvedSceneName, mode);

            if (operation == null)
            {
                SceneManager.LoadScene(resolvedSceneName, mode);
                return;
            }

            operation.allowSceneActivation = false;

            while (operation.progress < 0.9f)
            {
                SetProgress(Mathf.Clamp01(operation.progress / 0.9f));
                await Task.Yield();
            }

            SetProgress(1f);

            float elapsed = Time.realtimeSinceStartup - shownAtRealtime;
            float remaining = minimumVisibleSeconds - elapsed;

            if (remaining > 0f)
                await DelaySecondsRealtime(remaining);

            operation.allowSceneActivation = true;

            while (!operation.isDone)
                await Task.Yield();

            if (!hideOnUnitySceneLoaded)
                await HideAsync();
        }

        public void SetProgress(float normalized)
        {
            float progress = Mathf.Clamp01(normalized);

            if (progressSlider != null)
                progressSlider.value = progress;

            if (progressText != null)
                progressText.text = string.Format(progressFormat, Mathf.RoundToInt(progress * 100f));
        }

        private void EnsureLoadingRoot()
        {
            if (loadingRoot != null)
                return;

            if (loadingPrefab != null)
            {
                loadingRoot = Instantiate(loadingPrefab);
                loadingRoot.name = loadingPrefab.name;

                if (dontDestroyOnLoad)
                    DontDestroyOnLoad(loadingRoot);
            }
            else
            {
                loadingRoot = gameObject;
            }

            if (canvasGroup == null && loadingRoot != null)
                canvasGroup = loadingRoot.GetComponentInChildren<CanvasGroup>(true);

            if (progressSlider == null && loadingRoot != null)
                progressSlider = loadingRoot.GetComponentInChildren<Slider>(true);

            ConfigureLoadingCanvas();
        }

        private void SetRootActive(bool active)
        {
            if (loadingRoot == null)
                return;

            if (loadingRoot.activeSelf != active)
                loadingRoot.SetActive(active);
        }

        private void ConfigureLoadingCanvas()
        {
            if (!forceOverlayCanvas || loadingRoot == null)
                return;

            Canvas[] canvases = loadingRoot.GetComponentsInChildren<Canvas>(true);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null)
                    continue;

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, loadingCanvasSortOrder);
            }
        }

        private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!hideOnUnitySceneLoaded)
                return;

            if (isNetworkControlledLoading)
                return;

            if (!isVisible)
                return;

            _ = HideAsync();
        }

        private void HandleNetworkSceneChanged(SceneChangedEvent e)
        {
            if (!hideOnNetworkSceneChanged)
                return;

            if (isNetworkControlledLoading)
                return;

            if (!isVisible)
                return;

            _ = HideAsync();
        }

        private static LoadingScreenService Resolve()
        {
            LoadingScreenService service = ServiceLocator.Get<LoadingScreenService>();

            if (service != null)
                return service;

            if (instance != null)
                return instance;

            return FindFirstObjectByType<LoadingScreenService>(FindObjectsInactive.Include);
        }

        private static bool TryResolveLoadableScene(
            string sceneName,
            string fallbackSceneName,
            out string resolvedSceneName)
        {
            resolvedSceneName = string.Empty;

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoad] Failed. No valid scene or fallback. scene is empty.");
                return false;
            }

            bool canLoad = Application.CanStreamedLevelBeLoaded(sceneName);
            Debug.Log($"[SceneLoad] CanStreamedLevelBeLoaded={canLoad}. scene={sceneName}");

            if (canLoad)
            {
                resolvedSceneName = sceneName;
                return true;
            }

            LogSceneNotRegistered(sceneName);

            if (!string.IsNullOrWhiteSpace(fallbackSceneName) &&
                !string.Equals(sceneName, fallbackSceneName, System.StringComparison.Ordinal))
            {
                bool canLoadFallback = Application.CanStreamedLevelBeLoaded(fallbackSceneName);
                Debug.Log($"[SceneLoad] CanStreamedLevelBeLoaded={canLoadFallback}. scene={fallbackSceneName}");

                if (canLoadFallback)
                {
                    Debug.Log($"[SceneLoad] Loading fallback scene={fallbackSceneName}");
                    resolvedSceneName = fallbackSceneName;
                    return true;
                }

                LogSceneNotRegistered(fallbackSceneName);
            }

            Debug.LogError("[SceneLoad] Failed. No valid scene or fallback.");
            return false;
        }

        private static void LogSceneNotRegistered(string sceneName)
        {
#if UNITY_EDITOR
            bool existsInBuildSettings = false;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (scene == null || !scene.enabled)
                    continue;

                string registeredName = System.IO.Path.GetFileNameWithoutExtension(scene.path);
                if (string.Equals(registeredName, sceneName, System.StringComparison.Ordinal))
                {
                    existsInBuildSettings = true;
                    break;
                }
            }

            Debug.LogError(
                $"[SceneLoad] Scene not registered in build profile/shared scene list. scene={sceneName}, editorBuildSettingsContainsEnabledScene={existsInBuildSettings}");
#else
            Debug.LogError($"[SceneLoad] Scene not registered in build profile/shared scene list. scene={sceneName}");
#endif
        }

        private static async Task DelaySecondsRealtime(float seconds)
        {
            float endTime = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup < endTime)
                await Task.Yield();
        }

        private async Task FadeCanvasGroupAsync(float targetAlpha, float duration)
        {
            if (canvasGroup == null)
                return;

            float startAlpha = canvasGroup.alpha;
            float startTime = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startTime < duration)
            {
                float normalized = Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / duration);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, normalized);
                await Task.Yield();
            }

            canvasGroup.alpha = targetAlpha;
        }
    }
}

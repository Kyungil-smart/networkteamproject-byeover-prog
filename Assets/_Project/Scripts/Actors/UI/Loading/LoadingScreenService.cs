using System.Threading.Tasks;

using TMPro;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using DeadZone.Core;

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
        [SerializeField, Min(0f)] private float minimumVisibleSeconds = 0.35f;
        [SerializeField] private bool hideOnUnitySceneLoaded = true;
        [SerializeField] private bool hideOnNetworkSceneChanged = true;

        [Header("로그")]
        [SerializeField] private bool logDebug;

        private static LoadingScreenService instance;

        private bool isVisible;
        private float shownAtRealtime;

        public static LoadingScreenService Instance => instance;
        public bool IsVisible => isVisible;

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
                ServiceLocator.Unregister<LoadingScreenService>();
                instance = null;
            }
        }

        public static void LoadSceneOrFallback(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            LoadingScreenService service = Resolve();

            if (service == null)
            {
                SceneManager.LoadScene(sceneName, mode);
                return;
            }

            _ = service.LoadSceneAsync(sceneName, mode);
        }

        public static void ShowForNetworkLoadOrFallback(string sceneName)
        {
            LoadingScreenService service = Resolve();
            service?.Show(sceneName);
        }

        public void Show(string sceneName = null)
        {
            EnsureLoadingRoot();

            shownAtRealtime = Time.realtimeSinceStartup;
            isVisible = true;
            SetRootActive(true);
            SetProgress(0f);

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

            HideImmediate();
        }

        public void HideImmediate()
        {
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

            Show(sceneName);

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, mode);

            if (operation == null)
            {
                SceneManager.LoadScene(sceneName, mode);
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
        }

        private void SetRootActive(bool active)
        {
            if (loadingRoot == null)
                return;

            if (loadingRoot.activeSelf != active)
                loadingRoot.SetActive(active);
        }

        private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!hideOnUnitySceneLoaded)
                return;

            if (!isVisible)
                return;

            _ = HideAsync();
        }

        private void HandleNetworkSceneChanged(SceneChangedEvent e)
        {
            if (!hideOnNetworkSceneChanged)
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

        private static async Task DelaySecondsRealtime(float seconds)
        {
            float endTime = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup < endTime)
                await Task.Yield();
        }
    }
}

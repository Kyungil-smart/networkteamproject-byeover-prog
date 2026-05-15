using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using DeadZone.Actors.UI;

namespace DeadZone.Core
{
    /// <summary>
    /// 부트스트랩 셋업. _Bootstrap 씬의 NetworkBootstrap GameObject에 부착된다.
    /// 자신을 DontDestroyOnLoad로 표시하고 즉시 MainMenu를 로드한다.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("부트스트랩 후 로드할 씬")]
        [SerializeField] private string firstSceneName = "MainMenu";

        private static GameBootstrap instance;

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            RemoveDuplicateNetworkManagers();

            Application.targetFrameRate = 60;
            Debug.Log("[Bootstrap] 초기화 완료");
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name == "_Bootstrap")
            {
                LoadingScreenService.LoadSceneOrFallback(firstSceneName);
            }
        }

        private void OnApplicationQuit()
        {
            EventBus.Clear();
            ServiceLocator.Clear();
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RemoveDuplicateNetworkManagers();
        }

        private static void RemoveDuplicateNetworkManagers()
        {
            NetworkManager[] managers = FindObjectsByType<NetworkManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (managers == null || managers.Length <= 1)
                return;

            NetworkManager bootstrapManager = instance != null
                ? instance.GetComponent<NetworkManager>()
                : null;

            NetworkManager keeper = bootstrapManager != null
                ? bootstrapManager
                : NetworkManager.Singleton;

            if (keeper == null)
            {
                for (int i = 0; i < managers.Length; i++)
                {
                    if (managers[i] != null)
                    {
                        keeper = managers[i];
                        break;
                    }
                }
            }

            for (int i = 0; i < managers.Length; i++)
            {
                NetworkManager manager = managers[i];
                if (manager == null || manager == keeper)
                    continue;

                Debug.Log(
                    $"[Bootstrap] Duplicate NetworkManager removed. Keep={GetObjectName(keeper)}, Remove={GetObjectName(manager)}",
                    manager);
                if (manager.IsListening)
                    manager.Shutdown();

                Destroy(manager.gameObject);
            }
        }

        private static string GetObjectName(Object target)
        {
            return target != null ? target.name : "None";
        }
    }
}

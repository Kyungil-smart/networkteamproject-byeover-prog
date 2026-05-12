using UnityEngine;
using UnityEngine.SceneManagement;
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
    }
}

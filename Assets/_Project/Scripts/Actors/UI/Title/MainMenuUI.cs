using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// DEADZONE 타이틀 화면 전용 UI 컨트롤러.
    /// - 게임 시작 버튼
    /// - 설정 팝업
    /// - 종료 확인 팝업
    /// - ESC 팝업 닫기
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        [Header("Main Buttons")]
        [SerializeField] private Button gameStartButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("Settings Popup")]
        [SerializeField] private GameObject settingsPopup;
        [SerializeField] private Button settingsCloseButton;

        [Header("Quit Confirm Popup")]
        [SerializeField] private GameObject quitConfirmPopup;
        [SerializeField] private Button quitYesButton;
        [SerializeField] private Button quitNoButton;

        [Header("Login")]
        [Tooltip("체크하면 게임 시작 시 로그인 패널을 열고, 체크 해제하면 loginSceneName 씬을 로드합니다.")]
        [SerializeField] private bool useLoginPanel = true;

        [SerializeField] private GameObject loginPanel;

        [Tooltip("useLoginPanel이 false일 때 로드할 씬 이름")]
        [SerializeField] private string loginSceneName = "Login";

        private void Awake()
        {
            CloseAllPopups();
        }

        private void Update()
        {
            if (Keyboard.current == null)
                return;

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseTopPopup();
            }
        }

        private void OnEnable()
        {
            BindButtons();
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        private void BindButtons()
        {
            if (gameStartButton != null)
                gameStartButton.onClick.AddListener(OnClickGameStart);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnClickSettings);

            if (quitButton != null)
                quitButton.onClick.AddListener(OnClickQuit);

            if (settingsCloseButton != null)
                settingsCloseButton.onClick.AddListener(CloseSettingsPopup);

            if (quitYesButton != null)
                quitYesButton.onClick.AddListener(QuitGame);

            if (quitNoButton != null)
                quitNoButton.onClick.AddListener(CloseQuitConfirmPopup);
        }

        private void UnbindButtons()
        {
            if (gameStartButton != null)
                gameStartButton.onClick.RemoveListener(OnClickGameStart);

            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OnClickSettings);

            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnClickQuit);

            if (settingsCloseButton != null)
                settingsCloseButton.onClick.RemoveListener(CloseSettingsPopup);

            if (quitYesButton != null)
                quitYesButton.onClick.RemoveListener(QuitGame);

            if (quitNoButton != null)
                quitNoButton.onClick.RemoveListener(CloseQuitConfirmPopup);
        }

        private void OnClickGameStart()
        {
            CloseAllPopups();

            if (useLoginPanel)
            {
                OpenLoginPanel();
                return;
            }

            LoadLoginScene();
        }

        private void OnClickSettings()
        {
            CloseQuitConfirmPopup();
            CloseLoginPanel();

            if (settingsPopup != null)
                settingsPopup.SetActive(true);
        }

        private void OnClickQuit()
        {
            CloseSettingsPopup();
            CloseLoginPanel();

            if (quitConfirmPopup != null)
                quitConfirmPopup.SetActive(true);
        }

        private void OpenLoginPanel()
        {
            if (loginPanel == null)
            {
                Debug.LogWarning("[MainMenuUI] LoginPanel이 연결되지 않았습니다.");
                return;
            }

            loginPanel.SetActive(true);
        }

        private void LoadLoginScene()
        {
            if (string.IsNullOrWhiteSpace(loginSceneName))
            {
                Debug.LogWarning("[MainMenuUI] loginSceneName이 비어 있습니다.");
                return;
            }

            SceneManager.LoadScene(loginSceneName);
        }

        private void CloseTopPopup()
        {
            // 나중에 뜬 팝업을 우선 닫는 방식
            if (quitConfirmPopup != null && quitConfirmPopup.activeSelf)
            {
                quitConfirmPopup.SetActive(false);
                return;
            }

            if (settingsPopup != null && settingsPopup.activeSelf)
            {
                settingsPopup.SetActive(false);
                return;
            }

            if (loginPanel != null && loginPanel.activeSelf)
            {
                loginPanel.SetActive(false);
                return;
            }
        }

        private void CloseSettingsPopup()
        {
            if (settingsPopup != null)
                settingsPopup.SetActive(false);
        }

        private void CloseQuitConfirmPopup()
        {
            if (quitConfirmPopup != null)
                quitConfirmPopup.SetActive(false);
        }

        private void CloseLoginPanel()
        {
            if (loginPanel != null)
                loginPanel.SetActive(false);
        }

        private void CloseAllPopups()
        {
            CloseSettingsPopup();
            CloseQuitConfirmPopup();
            CloseLoginPanel();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
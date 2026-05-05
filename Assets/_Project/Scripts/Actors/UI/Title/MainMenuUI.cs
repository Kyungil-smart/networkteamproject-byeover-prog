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
        private const string SettingsPopupName = "Popup_Setting";
        private const string SettingsCloseButtonName = "Btn_CloseSetting";

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
            ResolveSettingsPopupReferences();
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
            ResolveSettingsPopupReferences();
            BindButtons();
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        private void BindButtons()
        {
            UnbindButtons();

            if (gameStartButton != null)
                gameStartButton.onClick.AddListener(OnClickGameStart);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnClickSettings);

            if (quitButton != null)
                quitButton.onClick.AddListener(OnClickQuit);

            BindSettingsCloseButton();

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
            ResolveSettingsPopupReferences();
            CloseQuitConfirmPopup();
            CloseLoginPanel();

            if (settingsPopup != null)
            {
                EnsureSettingsPopupScale();
                settingsPopup.SetActive(true);
                ResolveSettingsPopupReferences();
                BindSettingsCloseButton();
            }
            else
            {
                Debug.LogWarning("[MainMenuUI] Popup_Setting was not found. Assign settingsPopup in the inspector.", this);
            }
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
            ResolveSettingsPopupReferences();

            if (settingsPopup != null)
                settingsPopup.SetActive(false);
            else
                Debug.LogWarning("[MainMenuUI] Cannot close settings popup because Popup_Setting was not found.", this);
        }

        private void BindSettingsCloseButton()
        {
            if (settingsCloseButton == null)
                return;

            settingsCloseButton.onClick.RemoveListener(CloseSettingsPopup);
            settingsCloseButton.onClick.AddListener(CloseSettingsPopup);
        }

        private void EnsureSettingsPopupScale()
        {
            if (settingsPopup == null)
                return;

            Transform popupTransform = settingsPopup.transform;
            if (popupTransform.localScale == Vector3.zero)
                popupTransform.localScale = Vector3.one;
        }

        private void ResolveSettingsPopupReferences()
        {
            if (settingsPopup == null)
            {
                GameObject popup = FindSceneObjectByName(SettingsPopupName);
                if (popup != null)
                    settingsPopup = popup;
            }

            if (settingsCloseButton != null)
                return;

            Transform popupTransform = settingsPopup != null ? settingsPopup.transform : transform;
            Transform closeButtonTransform = FindChildByName(popupTransform, SettingsCloseButtonName);
            if (closeButtonTransform != null)
                settingsCloseButton = closeButtonTransform.GetComponent<Button>();

            if (settingsCloseButton == null)
            {
                Debug.LogWarning(
                    "[MainMenuUI] Btn_CloseSetting Button was not found. Assign settingsCloseButton in the inspector.",
                    this);
            }
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i];
            }

            return null;
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate.name == objectName && candidate.scene.IsValid())
                    return candidate;
            }

            return null;
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

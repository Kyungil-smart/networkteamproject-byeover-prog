using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems;
using DeadZone.Systems.Save;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 설정 팝업의 열기/닫기와 파산신청 입력을 처리합니다.
    /// </summary>
    public sealed class SettingPopupUI : MonoBehaviour
    {
        private const string CloseButtonName = "Btn_CloseSetting";
        private const string BankruptcyButtonName = "Btn_Bankruptcy";
        private const string BankruptcyConfirmButtonName = "Btn_BankruptcyConfirm";
        private const string BankruptcyCancelButtonName = "Btn_BankruptcyCancel";
        private const string BankruptcyConfirmRootName = "Popup_BankruptcyConfirm";
        private const string LogoutButtonName = "Btn_Logout";
        private const string ChangeAccountButtonName = "Btn_ChangeAccount";

        [Header("설정 팝업")]
        [Tooltip("설정 팝업을 닫는 버튼입니다.")]
        [SerializeField] private Button closeButton;

        [Header("파산신청")]
        [Tooltip("파산신청 시 적용할 스타터팩 설정입니다. 비어 있으면 CloudSaveSystem의 기본 설정을 사용합니다.")]
        [SerializeField] private StarterPackConfigSO starterPackConfig;

        [Tooltip("파산신청을 요청하는 버튼입니다.")]
        [SerializeField] private Button bankruptcyButton;

        [Tooltip("파산신청 확인 UI 루트입니다. 연결하지 않으면 즉시 실행됩니다.")]
        [SerializeField] private GameObject bankruptcyConfirmRoot;

        [Tooltip("파산신청 확인 버튼입니다.")]
        [SerializeField] private Button bankruptcyConfirmButton;

        [Tooltip("파산신청 취소 버튼입니다.")]
        [SerializeField] private Button bankruptcyCancelButton;

        [Tooltip("파산신청 전 확인 UI를 요구할지 여부입니다.")]
        [SerializeField] private bool requireBankruptcyConfirmation = true;

        [Tooltip("파산신청 성공 후 설정 팝업을 자동으로 닫을지 여부입니다.")]
        [SerializeField] private bool closeAfterBankruptcySuccess;

        [Header("계정")]
        [Tooltip("현재 Firebase 계정에서 로그아웃하는 버튼입니다.")]
        [SerializeField] private Button logoutButton;

        [Tooltip("현재 Firebase 계정에서 로그아웃하고 타이틀로 돌아가 다른 계정 로그인을 준비하는 버튼입니다.")]
        [SerializeField] private Button changeAccountButton;

        [Tooltip("로그아웃 또는 계정 변경 후 이동할 타이틀 씬 이름입니다.")]
        [SerializeField] private string titleSceneName = "Title";

        [Tooltip("로그아웃 또는 계정 변경 시 진행 중인 Netcode 세션을 종료합니다.")]
        [SerializeField] private bool shutdownNetworkOnSignOut = true;

        private bool isApplyingBankruptcy;
        private bool isAccountActionRunning;

        /// <summary>
        /// 현재 설정 팝업이 활성화되어 있는지 반환합니다.
        /// </summary>
        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            ResolveReferences();
            HideBankruptcyConfirmation();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindButtons();
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        /// <summary>
        /// 설정 팝업을 엽니다.
        /// </summary>
        public void Open()
        {
            gameObject.SetActive(true);
            EnsurePopupScale();
            ResolveReferences();
            BindButtons();
        }

        /// <summary>
        /// 설정 팝업을 닫습니다.
        /// </summary>
        public void Close()
        {
            HideBankruptcyConfirmation();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 설정 팝업의 열림 상태를 전환합니다.
        /// </summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (bankruptcyButton != null)
            {
                bankruptcyButton.onClick.RemoveListener(HandleBankruptcyRequested);
                bankruptcyButton.onClick.AddListener(HandleBankruptcyRequested);
            }

            if (bankruptcyConfirmButton != null)
            {
                bankruptcyConfirmButton.onClick.RemoveListener(HandleBankruptcyConfirmed);
                bankruptcyConfirmButton.onClick.AddListener(HandleBankruptcyConfirmed);
            }

            if (bankruptcyCancelButton != null)
            {
                bankruptcyCancelButton.onClick.RemoveListener(HandleBankruptcyCanceled);
                bankruptcyCancelButton.onClick.AddListener(HandleBankruptcyCanceled);
            }

            if (logoutButton != null)
            {
                logoutButton.onClick.RemoveListener(HandleLogoutRequested);
                logoutButton.onClick.AddListener(HandleLogoutRequested);
            }

            if (changeAccountButton != null)
            {
                changeAccountButton.onClick.RemoveListener(HandleChangeAccountRequested);
                changeAccountButton.onClick.AddListener(HandleChangeAccountRequested);
            }
        }

        private void UnbindButtons()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);

            if (bankruptcyButton != null)
                bankruptcyButton.onClick.RemoveListener(HandleBankruptcyRequested);

            if (bankruptcyConfirmButton != null)
                bankruptcyConfirmButton.onClick.RemoveListener(HandleBankruptcyConfirmed);

            if (bankruptcyCancelButton != null)
                bankruptcyCancelButton.onClick.RemoveListener(HandleBankruptcyCanceled);

            if (logoutButton != null)
                logoutButton.onClick.RemoveListener(HandleLogoutRequested);

            if (changeAccountButton != null)
                changeAccountButton.onClick.RemoveListener(HandleChangeAccountRequested);
        }

        private void ResolveReferences()
        {
            closeButton ??= FindButtonByName(CloseButtonName);
            bankruptcyButton ??= FindButtonByName(BankruptcyButtonName);
            bankruptcyConfirmButton ??= FindButtonByName(BankruptcyConfirmButtonName);
            bankruptcyCancelButton ??= FindButtonByName(BankruptcyCancelButtonName);
            logoutButton ??= FindButtonByName(LogoutButtonName);
            changeAccountButton ??= FindButtonByName(ChangeAccountButtonName);

            if (bankruptcyConfirmRoot == null)
            {
                Transform confirmTransform = FindChildByName(transform, BankruptcyConfirmRootName);
                if (confirmTransform != null)
                    bankruptcyConfirmRoot = confirmTransform.gameObject;
            }

            if (closeButton == null)
            {
                Debug.LogWarning(
                    "[SettingPopupUI] Btn_CloseSetting Button was not found. Assign closeButton in the inspector.",
                    this);
            }
        }

        private Button FindButtonByName(string objectName)
        {
            Transform buttonTransform = FindChildByName(transform, objectName);
            return buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        }

        private void HandleBankruptcyRequested()
        {
            if (isApplyingBankruptcy)
                return;

            if (requireBankruptcyConfirmation && bankruptcyConfirmRoot != null)
            {
                bankruptcyConfirmRoot.SetActive(true);
                return;
            }

            HandleBankruptcyConfirmed();
        }

        private void HandleBankruptcyCanceled()
        {
            HideBankruptcyConfirmation();
        }

        private void HandleLogoutRequested()
        {
            _ = SignOutAndReturnToTitleAsync(suppressAutoLoginOnTitle: false);
        }

        private void HandleChangeAccountRequested()
        {
            _ = SignOutAndReturnToTitleAsync(suppressAutoLoginOnTitle: true);
        }

        private async void HandleBankruptcyConfirmed()
        {
            if (isApplyingBankruptcy)
                return;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null)
            {
                Debug.LogWarning("[SettingPopupUI] 파산신청 실패. CloudSaveSystem을 찾을 수 없습니다.", this);
                return;
            }

            isApplyingBankruptcy = true;
            SetBankruptcyButtonsInteractable(false);

            bool success = await cloudSaveSystem.ApplyBankruptcyStarterPackAsync(starterPackConfig);

            isApplyingBankruptcy = false;
            SetBankruptcyButtonsInteractable(true);

            if (!success)
                return;

            HideBankruptcyConfirmation();
            LobbySaveService lobbySaveService = ResolveLobbySaveService();
            if (lobbySaveService != null)
                lobbySaveService.LoadLobbyDataFromCloudAuthoritative();
            else
                Debug.LogWarning("[SettingPopupUI] 파산신청은 완료됐지만 LobbySaveService를 찾지 못해 현재 로비 UI를 즉시 갱신하지 못했습니다.", this);

            Close();
        }

        private static CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem != null)
                return cloudSaveSystem;

            return Object.FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);
        }

        private static LobbySaveService ResolveLobbySaveService()
        {
            LobbySaveService lobbySaveService = ServiceLocator.Get<LobbySaveService>();
            if (lobbySaveService != null)
                return lobbySaveService;

            return Object.FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
        }

        private void SetBankruptcyButtonsInteractable(bool interactable)
        {
            if (bankruptcyButton != null)
                bankruptcyButton.interactable = interactable;

            if (bankruptcyConfirmButton != null)
                bankruptcyConfirmButton.interactable = interactable;

            if (bankruptcyCancelButton != null)
                bankruptcyCancelButton.interactable = interactable;
        }

        private async Task SignOutAndReturnToTitleAsync(bool suppressAutoLoginOnTitle)
        {
            if (isAccountActionRunning)
                return;

            isAccountActionRunning = true;
            SetAccountButtonsInteractable(false);

            FirebaseAuthManager authManager = ResolveFirebaseAuthManager();
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();

            if (cloudSaveSystem != null
                && authManager != null
                && authManager.IsSignedIn
                && cloudSaveSystem.HasLoadedData
                && cloudSaveSystem.LoadedFirebaseUid == authManager.CurrentUid)
            {
                bool uploaded = await cloudSaveSystem.UploadAsync();
                if (!uploaded)
                    Debug.LogWarning("[SettingPopupUI] Account sign-out continued after Cloud Save upload failed.", this);
            }

            if (suppressAutoLoginOnTitle)
                TitleLoginUI.SuppressNextAutoLoginOnce();

            if (authManager != null)
                authManager.SignOut();

            if (cloudSaveSystem != null)
                cloudSaveSystem.ClearLoadedData();

            if (shutdownNetworkOnSignOut
                && NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            Time.timeScale = 1f;
            HideBankruptcyConfirmation();

            if (!string.IsNullOrWhiteSpace(titleSceneName))
                LoadingScreenService.LoadSceneOrFallback(titleSceneName);

            isAccountActionRunning = false;
            SetAccountButtonsInteractable(true);
        }

        private void SetAccountButtonsInteractable(bool interactable)
        {
            if (logoutButton != null)
                logoutButton.interactable = interactable;

            if (changeAccountButton != null)
                changeAccountButton.interactable = interactable;
        }

        private static FirebaseAuthManager ResolveFirebaseAuthManager()
        {
            FirebaseAuthManager authManager = ServiceLocator.Get<FirebaseAuthManager>();
            if (authManager != null)
                return authManager;

            return Object.FindFirstObjectByType<FirebaseAuthManager>(FindObjectsInactive.Include);
        }

        private void HideBankruptcyConfirmation()
        {
            if (bankruptcyConfirmRoot != null)
                bankruptcyConfirmRoot.SetActive(false);
        }

        private void EnsurePopupScale()
        {
            if (transform.localScale == Vector3.zero)
                transform.localScale = Vector3.one;
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
    }
}

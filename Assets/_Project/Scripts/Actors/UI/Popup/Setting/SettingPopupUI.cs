using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems;
using DeadZone.Systems.Audio;
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
        private const string MasterVolumeRootName = "Master";
        private const string BgmVolumeRootName = "BGM";
        private const string SfxVolumeRootName = "SFX";
        private const string MasterVolumeSliderName = "Slider_MasterVolume";
        private const string BgmVolumeSliderName = "Slider_BgmVolume";
        private const string SfxVolumeSliderName = "Slider_SfxVolume";

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

        [Header("볼륨")]
        [Tooltip("전체 음량 슬라이더입니다. 0은 무음, 1은 최대 볼륨입니다.")]
        [SerializeField] private Slider masterVolumeSlider;

        [Tooltip("배경음 슬라이더입니다. AudioGroup이 BGM인 사운드만 조절합니다.")]
        [SerializeField] private Slider bgmVolumeSlider;

        [Tooltip("효과음 슬라이더입니다. BGM을 제외한 게임 효과음과 UI 클릭음을 함께 조절합니다.")]
        [SerializeField] private Slider sfxVolumeSlider;

        private bool isApplyingBankruptcy;
        private bool isAccountActionRunning;

        /// <summary>
        /// 현재 설정 팝업이 활성화되어 있는지 반환합니다.
        /// </summary>
        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            ResolveReferences();
            ApplySavedVolumesToAudioManager();
            RefreshVolumeSlidersFromSavedValues();
            HideBankruptcyConfirmation();
        }

        private void OnEnable()
        {
            ResolveReferences();
            RefreshVolumeSlidersFromSavedValues();
            BindButtons();
            BindVolumeSliders();
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnbindVolumeSliders();
        }

        /// <summary>
        /// 설정 팝업을 엽니다.
        /// </summary>
        public void Open()
        {
            gameObject.SetActive(true);
            EnsurePopupScale();
            ResolveReferences();
            RefreshVolumeSlidersFromSavedValues();
            BindButtons();
            BindVolumeSliders();
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

        private void BindVolumeSliders()
        {
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.onValueChanged.RemoveListener(HandleMasterVolumeChanged);
                masterVolumeSlider.onValueChanged.AddListener(HandleMasterVolumeChanged);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.onValueChanged.RemoveListener(HandleBgmVolumeChanged);
                bgmVolumeSlider.onValueChanged.AddListener(HandleBgmVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.onValueChanged.RemoveListener(HandleSfxVolumeChanged);
                sfxVolumeSlider.onValueChanged.AddListener(HandleSfxVolumeChanged);
            }
        }

        private void UnbindVolumeSliders()
        {
            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.RemoveListener(HandleMasterVolumeChanged);

            if (bgmVolumeSlider != null)
                bgmVolumeSlider.onValueChanged.RemoveListener(HandleBgmVolumeChanged);

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.RemoveListener(HandleSfxVolumeChanged);
        }

        private void ResolveReferences()
        {
            closeButton ??= FindButtonByName(CloseButtonName);
            bankruptcyButton ??= FindButtonByName(BankruptcyButtonName);
            bankruptcyConfirmButton ??= FindButtonByName(BankruptcyConfirmButtonName);
            bankruptcyCancelButton ??= FindButtonByName(BankruptcyCancelButtonName);
            logoutButton ??= FindButtonByName(LogoutButtonName);
            changeAccountButton ??= FindButtonByName(ChangeAccountButtonName);
            ResolveVolumeSliders();

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

        private void ResolveVolumeSliders()
        {
            masterVolumeSlider ??= FindSliderByName(MasterVolumeSliderName);
            bgmVolumeSlider ??= FindSliderByName(BgmVolumeSliderName);
            sfxVolumeSlider ??= FindSliderByName(SfxVolumeSliderName);

            masterVolumeSlider ??= FindSliderInChildRoot(MasterVolumeRootName);
            bgmVolumeSlider ??= FindSliderInChildRoot(BgmVolumeRootName);
            sfxVolumeSlider ??= FindSliderInChildRoot(SfxVolumeRootName);

            ConfigureVolumeSlider(masterVolumeSlider);
            ConfigureVolumeSlider(bgmVolumeSlider);
            ConfigureVolumeSlider(sfxVolumeSlider);
        }

        private Button FindButtonByName(string objectName)
        {
            Transform buttonTransform = FindChildByName(transform, objectName);
            return buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        }

        private Slider FindSliderByName(string objectName)
        {
            Transform sliderTransform = FindChildByName(transform, objectName);
            return sliderTransform != null ? sliderTransform.GetComponent<Slider>() : null;
        }

        private Slider FindSliderInChildRoot(string rootName)
        {
            Transform rootTransform = FindChildByName(transform, rootName);
            return rootTransform != null ? rootTransform.GetComponentInChildren<Slider>(true) : null;
        }

        private static void ConfigureVolumeSlider(Slider slider)
        {
            if (slider == null)
                return;

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
        }

        private void RefreshVolumeSlidersFromSavedValues()
        {
            AudioManager audioManager = ResolveAudioManager();

            SetSliderValueWithoutNotify(
                masterVolumeSlider,
                PlayerPrefs.GetFloat(AudioManager.MasterVolumePrefsKey, audioManager != null ? audioManager.MasterVolume : 1f));

            SetSliderValueWithoutNotify(
                bgmVolumeSlider,
                PlayerPrefs.GetFloat(AudioManager.BgmVolumePrefsKey, audioManager != null ? audioManager.BgmVolume : 1f));

            SetSliderValueWithoutNotify(
                sfxVolumeSlider,
                PlayerPrefs.GetFloat(AudioManager.SfxVolumePrefsKey, audioManager != null ? audioManager.SfxVolume : 1f));
        }

        private static void SetSliderValueWithoutNotify(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        private void ApplySavedVolumesToAudioManager()
        {
            AudioManager audioManager = ResolveAudioManager();
            if (audioManager == null)
                return;

            audioManager.SetMasterVolume(PlayerPrefs.GetFloat(AudioManager.MasterVolumePrefsKey, audioManager.MasterVolume));
            audioManager.SetBgmVolume(PlayerPrefs.GetFloat(AudioManager.BgmVolumePrefsKey, audioManager.BgmVolume));
            audioManager.SetEffectVolume(PlayerPrefs.GetFloat(AudioManager.SfxVolumePrefsKey, audioManager.SfxVolume));
        }

        private void HandleMasterVolumeChanged(float value)
        {
            float clampedValue = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(AudioManager.MasterVolumePrefsKey, clampedValue);
            PlayerPrefs.Save();

            AudioManager audioManager = ResolveAudioManager();
            if (audioManager != null)
                audioManager.SetMasterVolume(clampedValue);
        }

        private void HandleBgmVolumeChanged(float value)
        {
            float clampedValue = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(AudioManager.BgmVolumePrefsKey, clampedValue);
            PlayerPrefs.Save();

            AudioManager audioManager = ResolveAudioManager();
            if (audioManager != null)
                audioManager.SetBgmVolume(clampedValue);
        }

        private void HandleSfxVolumeChanged(float value)
        {
            float clampedValue = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(AudioManager.SfxVolumePrefsKey, clampedValue);
            PlayerPrefs.Save();

            AudioManager audioManager = ResolveAudioManager();
            if (audioManager != null)
                audioManager.SetEffectVolume(clampedValue);
        }

        private static AudioManager ResolveAudioManager()
        {
            AudioManager audioManager = AudioManager.Instance;
            if (audioManager != null)
                return audioManager;

            audioManager = ServiceLocator.Get<AudioManager>();
            if (audioManager != null)
                return audioManager;

            return Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
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

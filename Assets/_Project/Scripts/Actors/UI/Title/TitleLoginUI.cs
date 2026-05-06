using System;
using System.Threading.Tasks;

using DeadZone.Core;
using DeadZone.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class TitleLoginUI : MonoBehaviour
    {
        [Header("====입력 필드====")]
        [Tooltip("Firebase 로그인/회원가입에 사용할 이메일 입력")]
        [SerializeField] private TMP_InputField emailInput;
        
        [Tooltip("Firebase 로그인/회원가입에 사용할 비밀번호 입력" +
                 "\nContent Type을 Password로 설정하세요!!")]
        [SerializeField] private TMP_InputField passwordInput;
        
        [Tooltip("회원가입 시 저장할 닉네임 입력" +
                 "\n기존 계정 로그인 시 Cloud Save의 displayName이 비어 있을 때만 사용")]
        [SerializeField] private TMP_InputField displayNameInput;
        
        [Header("====버튼====")]
        [Tooltip("기존 Firebase 계정으로 로그인하는 버튼")]
        [SerializeField] private Button loginButton;
        
        [Tooltip("새 Firebase 계정을 생성하는 버튼")]
        [SerializeField] private Button registerButton;
        
        [Tooltip("로그인 팝업을 닫는 버튼" +
                 "\n로그인 처리 중에는 비활성화")]
        [SerializeField] private Button closeButton;
        
        [Header("====상태 표시====")]
        [Tooltip("오류 메시지와 진행 상태를 표시")]
        [SerializeField] private TMP_Text messageText;
        
        [Tooltip("로그인 처리 중 표시할 선택 오브젝트입니다. null이어도 동작합니다.")]
        [SerializeField] private GameObject loadingBlocker;
        
        [Header("====씬 이동====")]
        [Tooltip("로그인과 Cloud Save 로드가 끝난 뒤 이동할 로비 씬 이름" +
                 "\nBuild Settings에 등록되어 있어야 합니다.")]
        [SerializeField] private string lobbySceneName = "Lobby";
        
        [Header("====대기 시간====")]
        [Tooltip("Cloud Save 로드 이벤트를 기다릴 최대 시간(ms)" +
                 "\n네트워크 상황에 따라 조정가능 합니다.")]
        [SerializeField] private int cloudSaveTimeoutMs = 10000;

        private bool isBusy;

        private void Awake()
        {
            SetBusy(false);
            ShowMessage(string.Empty);
        }

        private void OnEnable()
        {
            BindButtons();
            SetBusy(false);
            ShowMessage(string.Empty);
        }

        private void OnDisable()
        {
            UnbindButtons();    
        }
        
        private void BindButtons()
        {
            if (loginButton != null) loginButton.onClick.AddListener(OnClickLogin);
            if (registerButton != null) registerButton.onClick.AddListener(OnClickRegister);
            if (closeButton != null) closeButton.onClick.AddListener(OnClickClose);
        }

        private void UnbindButtons()
        {
            if (loginButton != null) loginButton.onClick.RemoveListener(OnClickLogin);
            if (registerButton != null) registerButton.onClick.RemoveListener(OnClickRegister);
            if (closeButton != null) closeButton.onClick.RemoveListener(OnClickClose);
        }

        private async void OnClickLogin()
        {
            await RunAuthFlowAsync(isRegister: false);
        }

        private async void OnClickRegister()
        {
            await RunAuthFlowAsync(isRegister: true);
        }
        
        private void OnClickClose()
        {
            if (isBusy) return;
            gameObject.SetActive(false);
        }

        private async Task RunAuthFlowAsync(bool isRegister)
        {
            if (isBusy) return;
            
            string email = GetText(emailInput);
            string password = GetText(passwordInput);
            string inputDisplayName = GetText(displayNameInput);

            if (!ValidateCommonInput(email, password)) return;
            if (isRegister && string.IsNullOrWhiteSpace(inputDisplayName))
            {
                ShowMessage("닉네임을 입력해주세요.");
                return;
            }
            
            FirebaseAuthManager authManager = ServiceLocator.Get<FirebaseAuthManager>();
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();

            if (authManager == null || cloudSaveSystem == null)
            {
                ShowMessage("로그인 시스템이 아직 준비되지 않았습니다. 잠시 후 다시 시도해주세요.");
                return;
            }
            
            SetBusy(true);
            ShowMessage(isRegister ? "회원가입 중입니다..." : "로그인 중입니다...");

            try
            {
                Task<PlayerCloudData> waitCloudSaveTask = WaitForCloudSaveLoadedAfterAuthAsync(
                    cloudSaveSystem,
                    authManager
                );
                
                Firebase.Auth.FirebaseUser user = isRegister
                    ? await authManager.RegisterAsync(email, password)
                    : await authManager.SignInAsync(email, password);

                if (user == null)
                {
                    ShowMessage(isRegister ? "회원가입에 실패했습니다." : "로그인에 실패했습니다.");
                    return;
                }

                string expectedUid = user.UserId;
                
                ShowMessage("플레이어 데이터를 불러오는 중입니다...");
                
                PlayerCloudData cloudData = await waitCloudSaveTask;

                if (cloudData == null)
                {
                    ShowMessage("플레이어 데이터 로드가 실패했거나 시간이 초과되었습니다.");
                    return;
                }

                if (authManager.CurrentUid != expectedUid
                    || cloudSaveSystem.LoadedFirebaseUid != expectedUid)
                {
                    ShowMessage("로그인 계정과 플레이어 데이터가 일치하지 않습니다.");
                    return;
                }

                if (cloudData.profile == null)
                {
                    cloudData.profile = new ProfileData();
                }

                bool needsUpload = false;

                if (string.IsNullOrWhiteSpace(cloudData.profile.displayName))
                {
                    if (string.IsNullOrWhiteSpace(inputDisplayName))
                    {
                        ShowMessage("닉네임을 입력해주세요.");
                        return;
                    }

                    cloudData.profile.displayName = inputDisplayName;
                    needsUpload = true;
                }

                if (needsUpload)
                {
                    ShowMessage("닉네임을 저장하는 중입니다...");

                    bool uploaded = await cloudSaveSystem.UploadAsync();
                    if (!uploaded)
                    {
                        ShowMessage("닉네임 저장에 실패했습니다.");
                        return;
                    }
                }
                
                ShowMessage("로비로 이동합니다...");
                SceneManager.LoadScene(lobbySceneName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TitleLoginUI] 로그인 흐름 중 예외 발생: {ex}");
                ShowMessage("로그인 처리 중 오류가 발생했습니다.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<PlayerCloudData> WaitForCloudSaveLoadedAfterAuthAsync(
            CloudSaveSystem cloudSaveSystem,
            FirebaseAuthManager authManager)
        {
            TaskCompletionSource<PlayerCloudData> completion = new();

            void OnCloudSaveLoaded(CloudSaveLoadedEvent e)
            {
                string eventUid = e.firebaseUid.ToString();
                
                if (string.IsNullOrWhiteSpace(eventUid)) return;
                if (authManager.CurrentUid != eventUid) return;
                if (cloudSaveSystem.LoadedFirebaseUid != eventUid) return;
                
                completion.TrySetResult(cloudSaveSystem.CurrentData);
            }

            EventBus.Subscribe<CloudSaveLoadedEvent>(OnCloudSaveLoaded);

            try
            {
                Task timeoutTask = Task.Delay(cloudSaveTimeoutMs);
                Task completedTask = await Task.WhenAny(completion.Task, timeoutTask);
                
                if (completedTask != completion.Task) return null;
                
                return await completion.Task;
            }
            finally
            {
                EventBus.Unsubscribe<CloudSaveLoadedEvent>(OnCloudSaveLoaded);
            }
        }

        private bool ValidateCommonInput(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ShowMessage("이메일을 입력해주세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowMessage("비밀번호를 입력해주세요.");
                return false;
            }
            
            return true;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            
            if (loginButton != null) loginButton.interactable = !busy;
            if (registerButton != null) registerButton.interactable = !busy;
            if (closeButton != null) closeButton.interactable = !busy;
            if (loadingBlocker != null) loadingBlocker.SetActive(busy);
        }

        private void ShowMessage(string message)
        {
            if (messageText != null) messageText.text = message;
        }

        private static string GetText(TMP_InputField input)
        {
            return input == null ? string.Empty : input.text.Trim();
        }
        
    }
}
using System;
using System.Threading.Tasks;

using TMPro;

using Unity.Netcode;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Actors.UI
{
    public class LobbyRoomController : MonoBehaviour
    {
        private const int ShutdownPollMilliseconds = 100;
        private const float MinimumShutdownWaitSeconds = 2.5f;

        public static event Action PartyRoomVisibilityChanged;
        public static bool IsPartyRoomVisible { get; private set; }

        private enum RoomUiState
        {
            Idle,
            Busy,
            HostRoom,
            ClientRoom,
            ShuttingDown
        }
        
        [Header("====방 생성/참가 버튼====")]
        [Tooltip("Relay Host 방 생성을 요청하는 버튼")]
        [SerializeField] private Button createRoomButton;
        
        [Tooltip("joinCode 입력 팝업을 여는 버튼")]
        [SerializeField] private Button openJoinPopupButton;
        
        [Tooltip("입력한 JoinCode로 Relay 방 참가를 요청하는 버튼")]
        [SerializeField] private Button joinRoomButton;

        [Tooltip("Join popup cancel button")]
        [SerializeField] private Button cancelJoinPopupButton;
        
        [Tooltip("현재 Host/Client 연결을 종료하는 버튼")]
        [SerializeField] private Button leaveRoomButton;
        
        [Tooltip("현재 표시된 JoinCode를 클립보드에 복사하는 버튼")]
        [SerializeField] private Button copyJoinCodeButton;

        [Tooltip("파티 생성 전에는 꺼지고, 파티 생성/참가 후 켜지는 Slot_Party 오브젝트")]
        [SerializeField] private GameObject slotPartyRoot;
        
        [Header("====방 생성/참가 UI====")]
        [Tooltip("JoinCode 입력용 팝업 루트")]
        [SerializeField] private GameObject joinPopup;
        
        [Tooltip("Client가 참가할 Relay JoinCode를 입력하는 TMP_InputField")]
        [SerializeField] private TMP_InputField joinCodeInput;
        
        [Tooltip("Host가 생성한 Relay JoinCode를 표시하는 TMP_Text")]
        [SerializeField] private TMP_Text joinCodeText;
        
        [Tooltip("방 생성/참가 진행 상태와 실패 사유를 표시하는 TMP_Text")]
        [SerializeField] private TMP_Text statusText;
        
        [Header("====로비 방 설정====")]
        [Tooltip("Relay 방 최대 인원 기본값 4")]
        [SerializeField, Min(1)] private int maxPlayers = 4;
        
        [Tooltip("JoinCode가 아직 없을 때 표시할 기본 텍스트")]
        [SerializeField] private string emptyJoinCodeText = "-";
        
        [Tooltip("방 참가 성공 시 Join 팝업을 자동으로 닫을지 여부")]
        [SerializeField] private bool closeJoinPopupOnJoinSuccess = true;
        
        [Tooltip("Disconnect 호출 후 다시 Create/Join을 허용하기 전까지 기다리는 시간")]
        [SerializeField, Min(0f)] private float shutdownRetryDelaySec = 1f;
        
        [Header("====선택 UI====")]
        [Tooltip("네트워크 요청 중 입력을 막는 오브젝트 없으면 버튼 비활성화만 사용")]
        [SerializeField] private GameObject loadingBlocker;

        private RoomUiState currentState = RoomUiState.Idle;
        private string currentJoinCode = string.Empty;
        private bool hasCreatedOrJoinedRoomInThisScene;
        private bool staleSessionCleanupInProgress;

        private void Awake()
        {
            ResolveMissingReferences();
            InitializeView();
        }

        private void OnEnable()
        {
            ResolveMissingReferences();
            BindButtons();
            RestoreRoomStateFromNetworkSession();
        }
        
        private void OnDisable() => UnbindButtons();

        private void Update()
        {
            if (currentState is not (RoomUiState.HostRoom or RoomUiState.ClientRoom or RoomUiState.ShuttingDown))
                return;

            if (IsNetworkSessionActive())
                return;

            ResetRoomUiAfterSessionEnded("NetworkSessionEnded");
        }

        private void BindButtons()
        {
            if (createRoomButton != null)
                createRoomButton.onClick.AddListener(OnCreateRoomClicked);

            if (openJoinPopupButton != null)
                openJoinPopupButton.onClick.AddListener(OnOpenJoinPopupClicked);

            if (joinRoomButton != null)
                joinRoomButton.onClick.AddListener(OnJoinRoomClicked);

            if (cancelJoinPopupButton != null)
                cancelJoinPopupButton.onClick.AddListener(OnCancelJoinPopupClicked);

            if (leaveRoomButton != null)
                leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);

            if (copyJoinCodeButton != null)
                copyJoinCodeButton.onClick.AddListener(OnCopyJoinCodeClicked);
        }

        private void UnbindButtons()
        {
            if (createRoomButton != null)
                createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);

            if (openJoinPopupButton != null)
                openJoinPopupButton.onClick.RemoveListener(OnOpenJoinPopupClicked);

            if (joinRoomButton != null)
                joinRoomButton.onClick.RemoveListener(OnJoinRoomClicked);

            if (cancelJoinPopupButton != null)
                cancelJoinPopupButton.onClick.RemoveListener(OnCancelJoinPopupClicked);

            if (leaveRoomButton != null)
                leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);

            if (copyJoinCodeButton != null)
                copyJoinCodeButton.onClick.RemoveListener(OnCopyJoinCodeClicked);
        }
        
        private void InitializeView()
        {
            RestoreRoomStateFromNetworkSession();
            PartyPlayerColorCache.Clear();
            currentJoinCode = string.Empty;
            SetJoinCodeText(emptyJoinCodeText);
            SetJoinPopupVisible(false);
            SetLoadingBlocker(false);
            ShowStatus("방을 만들거나 초대 코드로 참가할 수 있습니다.");
        }
        
        private void OnCreateRoomClicked()
        {
            if (IsBusyState()) return;

            _ = CreateRoomAsync();
        }
        
        private void OnOpenJoinPopupClicked()
        {
            if (IsBusyState()) return;

            if (joinCodeInput != null)
                joinCodeInput.text = string.Empty;

            SetJoinPopupVisible(true);
            ApplyButtonsForCurrentState();
            ShowStatus("초대 코드를 입력해주세요.");
        }
        
        private void OnJoinRoomClicked()
        {
            if (IsBusyState()) return;

            _ = JoinRoomAsync();
        }

        private void OnCancelJoinPopupClicked()
        {
            if (IsBusyState()) return;

            if (joinCodeInput != null)
                joinCodeInput.text = string.Empty;

            SetJoinPopupVisible(false);
            ApplyButtonsForCurrentState();
        }

        private void OnLeaveRoomClicked()
        {
            if (IsBusyState()) return;
            
            _ = LeaveRoomAsync();
        }
        
        private void OnCopyJoinCodeClicked()
        {
            if (string.IsNullOrWhiteSpace(currentJoinCode))
            {
                ShowStatus("복사할 초대 코드가 없습니다.");
                return;
            }
            
            GUIUtility.systemCopyBuffer = currentJoinCode;
            ShowStatus("초대 코드를 클립보드에 복사했습니다.");
        }

        private async Task CreateRoomAsync()
        {
            if (!await ClearStaleNetworkSessionBeforeNewRoomAsync("CreateRoom"))
                return;

            if (!TryGetSessionManager(out var sessionManager)) return;
            
            Debug.Log($"[Party] CreateParty called. userId={GetCurrentUserId()}", this);
            SetRoomState(RoomUiState.Busy);
            ShowStatus("Relay 방을 생성하는 중입니다...");

            try
            {
                string joinCode = await sessionManager.StartHostWithRelayAsync(maxPlayers);

                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    currentJoinCode = string.Empty;
                    SetJoinCodeText(emptyJoinCodeText);
                    SetRoomState(RoomUiState.Idle);
                    ShowStatus("방 생성에 실패했습니다.");
                    return;
                }

                currentJoinCode = joinCode.Trim();
                Debug.Log($"[Party] Party created. partyId={currentJoinCode}, memberCount={GetConnectedClientCount()}", this);

                SetJoinCodeText(currentJoinCode);
                SetJoinPopupVisible(false);
                hasCreatedOrJoinedRoomInThisScene = true;
                SetRoomState(RoomUiState.HostRoom);

                ShowStatus("방 생성 완료. 초대 코드를 공유하세요.");
            }
            catch (Exception e)
            {
                currentJoinCode = string.Empty;
                SetJoinCodeText(emptyJoinCodeText);
                SetRoomState(RoomUiState.Idle);

                Debug.LogError($"[LobbyRoomController] 방 생성 실패: {e}");
                ShowStatus("방 생성에 실패했습니다.");
            }
        }

        private async Task JoinRoomAsync()
        {
            if (!await ClearStaleNetworkSessionBeforeNewRoomAsync("JoinRoom"))
                return;

            if (!TryGetSessionManager(out var sessionManager)) return;

            string joinCode = GetTrimmedJoinCode();

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                ShowStatus("초대 코드를 입력해주세요.");
                return;
            }
            
            SetRoomState(RoomUiState.Busy);
            ShowStatus("방에 참가하는 중입니다...");

            try
            {
                bool joined = await sessionManager.StartClientWithJoinCodeAsync(joinCode);

                if (!joined)
                {
                    SetRoomState(RoomUiState.Idle);
                    ShowStatus("방 참가에 실패했습니다.\n초대 코드를 확인해주세요.");
                    return;
                }

                currentJoinCode = string.Empty;
                SetJoinCodeText(emptyJoinCodeText);
                
                if (closeJoinPopupOnJoinSuccess) SetJoinPopupVisible(false);
                
                hasCreatedOrJoinedRoomInThisScene = true;
                SetRoomState(RoomUiState.ClientRoom);
                ShowStatus("방 참가 완료.");
            }
            catch (Exception e)
            {
                SetRoomState(RoomUiState.Idle);
                
                Debug.LogError($"[LobbyRoomController] 방 참가 실패: {e}");
                ShowStatus("방 참가에 실패했습니다.");
            }
        }

        private async Task LeaveRoomAsync()
        {
            if (!TryGetSessionManager(out var sessionManager)) return;
            
            SetRoomState(RoomUiState.ShuttingDown);
            ShowStatus("방 연결을 종료하는 중입니다...");
            
            sessionManager.Disconnect();
            
            if (shutdownRetryDelaySec > 0f)
                await Task.Delay(TimeSpan.FromSeconds(shutdownRetryDelaySec));

            currentJoinCode = string.Empty;
            SetJoinCodeText(emptyJoinCodeText);
            SetJoinPopupVisible(false);
            PartyPlayerColorCache.Clear();
            hasCreatedOrJoinedRoomInThisScene = false;
            SetRoomState(RoomUiState.Idle);
            
            ShowStatus("방 연결을 종료했습니다.");
        }

        private static string GetCurrentUserId()
        {
            DeadZone.Network.CloudSaveSystem cloudSave = ServiceLocator.Get<DeadZone.Network.CloudSaveSystem>();

            if (cloudSave != null && !string.IsNullOrWhiteSpace(cloudSave.LoadedFirebaseUid))
                return cloudSave.LoadedFirebaseUid;

            return "unknown";
        }

        private static int GetConnectedClientCount()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null && networkManager.ConnectedClients != null
                ? networkManager.ConnectedClients.Count
                : 0;
        }

        private bool TryGetSessionManager(out SessionManager sessionManager)
        {
            sessionManager = ServiceLocator.Get<SessionManager>();
            if (sessionManager == null)
                sessionManager = FindFirstObjectByType<SessionManager>(FindObjectsInactive.Include);
            
            if (sessionManager != null) return true;
            
            ShowStatus("네트워크 시스템이 준비되지 않았습니다.");
            Debug.LogWarning(
                "[LobbyRoomController] SessionManager를 찾을 수 없습니다." +
                "\nNetworkBootstrap이 먼저 로드되었는지 확인하세요.");
            return false;
        }

        private bool IsNetworkSessionActive()
        {
            if (NetworkManager.Singleton == null) return false;
            var networkManager = NetworkManager.Singleton;
            
            return networkManager.IsListening
                || networkManager.IsHost
                || networkManager.IsServer
                || networkManager.IsClient;
        }

        private void RestoreRoomStateFromNetworkSession()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            bool isListening = networkManager != null && networkManager.IsListening;
            bool isServer = isListening && networkManager.IsServer;
            bool isClient = isListening && networkManager.IsClient;

            if ((isServer || isClient) && IsRestoredLobbySessionStale())
            {
                _ = CleanupRestoredLobbySessionAsync();
                return;
            }

            if (isServer)
            {
                SetRoomState(RoomUiState.HostRoom);
                Debug.Log("[LobbyPartyUI] Restored party UI from active network session. role=Server, Slot_Party=Visible", this);
                ShowStatus("네트워크 파티 상태를 복구했습니다. 현재 서버로 연결 중입니다.");
                return;
            }

            if (isClient)
            {
                SetRoomState(RoomUiState.ClientRoom);
                Debug.Log("[LobbyPartyUI] Restored party UI from active network session. role=Client, Slot_Party=Visible", this);
                ShowStatus("네트워크 파티 상태를 복구했습니다. 현재 파티에 참가 중입니다.");
                return;
            }

            SetRoomState(RoomUiState.Idle);
            Debug.Log("[LobbyPartyUI] No active network session found. Slot_Party=Hidden", this);
        }

        private bool IsRestoredLobbySessionStale()
        {
            if (hasCreatedOrJoinedRoomInThisScene)
                return false;

            return string.Equals(SceneManager.GetActiveScene().name, "Lobby", StringComparison.Ordinal);
        }

        private async Task CleanupRestoredLobbySessionAsync()
        {
            if (staleSessionCleanupInProgress)
                return;

            staleSessionCleanupInProgress = true;
            SetRoomState(RoomUiState.ShuttingDown);
            ShowStatus("이전 방 연결을 정리하는 중입니다...");

            SessionManager.DisconnectActiveSession("RestoreStaleLobbySession");
            await WaitForNetworkSessionShutdownAsync();

            if (!IsNetworkSessionActive())
            {
                ResetRoomUiAfterSessionEnded("RestoreStaleLobbySession");
                staleSessionCleanupInProgress = false;
                return;
            }

            SetRoomState(RoomUiState.Idle);
            ShowStatus("이전 방 연결이 아직 종료되지 않았습니다. 잠시 후 다시 시도해주세요.");
            staleSessionCleanupInProgress = false;
        }

        private async Task<bool> ClearStaleNetworkSessionBeforeNewRoomAsync(string reason)
        {
            if (!IsNetworkSessionActive())
                return true;

            SetRoomState(RoomUiState.ShuttingDown);
            ShowStatus("이전 방 연결을 정리하는 중입니다...");

            SessionManager.DisconnectActiveSession(reason);
            await WaitForNetworkSessionShutdownAsync();

            if (!IsNetworkSessionActive())
            {
                ResetRoomUiAfterSessionEnded(reason);
                return true;
            }

            SetRoomState(RoomUiState.Idle);
            ShowStatus("이전 방 연결이 아직 종료되지 않았습니다. 잠시 후 다시 시도해주세요.");
            return false;
        }

        private async Task WaitForNetworkSessionShutdownAsync()
        {
            float timeoutSeconds = Mathf.Max(shutdownRetryDelaySec, MinimumShutdownWaitSeconds);
            int maxPollCount = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds * 1000f / ShutdownPollMilliseconds));

            for (int i = 0; i < maxPollCount; i++)
            {
                if (!IsNetworkSessionActive())
                    return;

                await Task.Delay(ShutdownPollMilliseconds);
            }
        }

        private void ResetRoomUiAfterSessionEnded(string reason)
        {
            currentJoinCode = string.Empty;
            SetJoinCodeText(emptyJoinCodeText);
            SetJoinPopupVisible(false);
            PartyPlayerColorCache.Clear();
            hasCreatedOrJoinedRoomInThisScene = false;
            staleSessionCleanupInProgress = false;
            SetRoomState(RoomUiState.Idle);
            ShowStatus("방을 만들거나 초대 코드로 참가할 수 있습니다.");
            Debug.Log($"[LobbyRoomController] Room UI reset after session ended. reason={reason}", this);
        }
        
        private bool IsBusyState()
        {
            return currentState is RoomUiState.Busy or RoomUiState.ShuttingDown;
        }
        
        private string GetTrimmedJoinCode()
        {
            return joinCodeInput == null ? string.Empty : joinCodeInput.text.Trim();
        }
        
        private void SetRoomState(RoomUiState state)
        {
            currentState = state;

            bool isBusy = IsBusyState();
            SetLoadingBlocker(isBusy);
            bool isPartyRoomState = state is RoomUiState.HostRoom or RoomUiState.ClientRoom;
            SetPartyRoomVisible(isPartyRoomState);
            SetPartyObjectsVisible(isPartyRoomState);

            ApplyButtonsForCurrentState();
        }

        private void SetPartyObjectsVisible(bool visible)
        {
            if (slotPartyRoot != null)
                slotPartyRoot.SetActive(visible);

            if (leaveRoomButton != null)
                leaveRoomButton.gameObject.SetActive(visible);
        }

        private static void SetPartyRoomVisible(bool visible)
        {
            if (IsPartyRoomVisible == visible)
                return;

            IsPartyRoomVisible = visible;
            PartyRoomVisibilityChanged?.Invoke();
        }

        private void ApplyButtonsForCurrentState()
        {
            switch (currentState)
            {
                case RoomUiState.Idle:
                    SetButtonInteractable(createRoomButton, true);
                    SetButtonInteractable(openJoinPopupButton, true);
                    SetButtonInteractable(joinRoomButton, IsJoinPopupVisible());
                    SetButtonInteractable(cancelJoinPopupButton, IsJoinPopupVisible());
                    SetButtonInteractable(leaveRoomButton, false);
                    SetButtonInteractable(copyJoinCodeButton, false);
                    break;
                case RoomUiState.Busy:
                case RoomUiState.ShuttingDown:
                    SetButtonInteractable(createRoomButton, false);
                    SetButtonInteractable(openJoinPopupButton, false);
                    SetButtonInteractable(joinRoomButton, false);
                    SetButtonInteractable(cancelJoinPopupButton, false);
                    SetButtonInteractable(leaveRoomButton, false);
                    SetButtonInteractable(copyJoinCodeButton, false);
                    break;
                
                case RoomUiState.HostRoom:
                    SetButtonInteractable(createRoomButton, false);
                    SetButtonInteractable(openJoinPopupButton, false);
                    SetButtonInteractable(joinRoomButton, false);
                    SetButtonInteractable(cancelJoinPopupButton, false);
                    SetButtonInteractable(leaveRoomButton, true);
                    SetButtonInteractable(copyJoinCodeButton, !string.IsNullOrWhiteSpace(currentJoinCode));
                    break;
                
                case RoomUiState.ClientRoom:
                    SetButtonInteractable(createRoomButton, false);
                    SetButtonInteractable(openJoinPopupButton, false);
                    SetButtonInteractable(joinRoomButton, false);
                    SetButtonInteractable(cancelJoinPopupButton, false);
                    SetButtonInteractable(leaveRoomButton, true);
                    SetButtonInteractable(copyJoinCodeButton, false);
                    break;
            }
        }
        
        private bool IsJoinPopupVisible() => joinPopup != null && joinPopup.activeSelf;

        private void SetButtonInteractable(Button btn, bool interactable)
        {
            if (btn == null) return;
            btn.interactable = interactable;
        }

        private void SetJoinPopupVisible(bool visible)
        {
            if (joinPopup == null) return;
            joinPopup.SetActive(visible);
        }

        private void SetJoinCodeText(string text)
        {
            if (joinCodeText == null) return;
            joinCodeText.text = text;
        }

        private void ShowStatus(string msg)
        {
            if (statusText == null) return;
            statusText.text = msg;
        }

        private void SetLoadingBlocker(bool active)
        {
            if (loadingBlocker == null) return;
            loadingBlocker.SetActive(active);
        }

        private void ResolveMissingReferences()
        {
            if (cancelJoinPopupButton != null || joinPopup == null) return;

            cancelJoinPopupButton = FindButtonInJoinPopup("Btn_Cancle");
            if (cancelJoinPopupButton == null)
                cancelJoinPopupButton = FindButtonInJoinPopup("Btn_Cancel");
        }

        private Button FindButtonInJoinPopup(string buttonName)
        {
            if (joinPopup == null || string.IsNullOrWhiteSpace(buttonName)) return null;

            Button[] buttons = joinPopup.GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                if (button != null && button.name == buttonName)
                    return button;
            }

            return null;
        }
    }
    
}

using System;
using System.Threading.Tasks;

using TMPro;

using Unity.Netcode;

using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Actors.UI
{
    public class LobbyRoomController : MonoBehaviour
    {
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

        private void Awake()
        {
            ResolveMissingReferences();
            InitializeView();
        }

        private void OnEnable()
        {
            ResolveMissingReferences();
            BindButtons();
            SetRoomState(RoomUiState.Idle);
        }
        
        private void OnDisable() => UnbindButtons();

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
            if (!TryGetSessionManager(out var sessionManager)) return;
            if (IsNetworkSessionActive())
            {
                ShowStatus("이미 네트워크 세션에 연결되어 있습니다.");
                return;
            }
            
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

                SetJoinCodeText(currentJoinCode);
                SetJoinPopupVisible(false);
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
            if (!TryGetSessionManager(out var sessionManager)) return;
            if (IsNetworkSessionActive())
            {
                ShowStatus("이미 네트워크 세션에 연결되어 있습니다.");
                return;
            }

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
            SetRoomState(RoomUiState.Idle);
            
            ShowStatus("방 연결을 종료했습니다.");
        }

        private bool TryGetSessionManager(out SessionManager sessionManager)
        {
            sessionManager = ServiceLocator.Get<SessionManager>();
            
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

            ApplyButtonsForCurrentState();
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

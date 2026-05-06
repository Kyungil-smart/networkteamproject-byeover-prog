using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

using DeadZone.Network;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 로비 맵 선택 UI를 처리합니다.
    /// 선택된 맵 상태는 네트워크로 동기화하고, 출격 가능 여부를 UI에 반영합니다.
    /// </summary>
    public class LobbyMapSelectUI : MonoBehaviour
    {
        [Header("==== 컨트롤러 ====")]
        [SerializeField] private LobbyRaidStartController raidStartController;
        [SerializeField] private NetworkLobbyState lobbyState;

        [Header("==== 버튼 ====")]
        [SerializeField] private Button btnMapA;
        [SerializeField] private Button btnMapB;

        [Tooltip("기존 파티 슬롯 Ready 버튼을 쓰는 경우에는 꺼둡니다. 별도 전역 Ready 버튼이 있을 때만 켭니다.")]
        [SerializeField] private bool useReadyButton;

        [SerializeField] private Button btnReady;
        [SerializeField] private Button btnStart;

        [Header("==== 표시 ====")]
        [SerializeField] private TMP_Text textStatus;
        [SerializeField] private GameObject iconLock;
        [SerializeField] private CanvasGroup btnMapBCanvasGroup;
        [SerializeField, Range(0.1f, 1f)] private float lockedMapBAlpha = 0.45f;

        [Header("==== 자동 연결 이름 ====")]
        [SerializeField] private string btnMapAName = "Btn_Map_A";
        [SerializeField] private string btnMapBName = "Btn_Map_B";
        [SerializeField] private string btnReadyName = "";
        [SerializeField] private string btnStartName = "Btn_Raid start";
        [SerializeField] private string iconLockName = "Icon_Lock";
        [SerializeField] private string textStatusName = "Text_RedyMessge";

        [Header("==== 디버그 ====")]
        [SerializeField] private bool logDebug;

        private bool isBound;
        private float refreshTimer;
        private bool hasWarnedInvalidStatusText;

        private const string HostOnlyMapSelectionMessage = "맵 선택은 Host만 변경할 수 있습니다.";

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindButtons();
            BindLobbyState();
            BindRaidController();
            Refresh();
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnbindLobbyState();
            UnbindRaidController();
        }

        private void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;

            if (refreshTimer > 0f) return;

            refreshTimer = 0.25f;
            Refresh();
        }

        private void HandleMapAClicked()
        {
            if (raidStartController == null) return;

            if (!CanLocalPlayerSelectMap())
            {
                SetStatus(HostOnlyMapSelectionMessage);
                Refresh();
                return;
            }

            raidStartController.SelectMap(LobbyRaidMap.MapA);
            Refresh();
        }

        private void HandleMapBClicked()
        {
            if (raidStartController == null) return;

            if (!CanLocalPlayerSelectMap())
            {
                SetStatus(HostOnlyMapSelectionMessage);
                Refresh();
                return;
            }

            if (!raidStartController.CanSelectMap(LobbyRaidMap.MapB, out string reason))
            {
                SetStatus(reason);
                Refresh();
                return;
            }

            raidStartController.SelectMap(LobbyRaidMap.MapB);
            Refresh();
        }

        private void HandleReadyClicked()
        {
            if (lobbyState == null ||
                !lobbyState.IsSpawned ||
                Unity.Netcode.NetworkManager.Singleton == null ||
                !Unity.Netcode.NetworkManager.Singleton.IsClient)
            {
                SetStatus("네트워크 로비에서만 준비 상태를 변경할 수 있습니다.");
                return;
            }

            ulong localClientId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            bool nextReady = true;

            if (lobbyState.TryGetPlayer(localClientId, out LobbyPlayerState state))
                nextReady = !state.IsReady;

            lobbyState.SetReadyServerRpc(nextReady);
            Refresh();
        }

        private void HandleStartClicked()
        {
            ResolveReferences();

            if (raidStartController == null)
            {
                SetStatus("출격 조건을 확인할 수 없습니다.");
                Refresh();
                return;
            }

            if (!CanLocalPlayerStartRaid())
            {
                SetStatus("Host만 출격을 시작할 수 있습니다.");
                Refresh();
                return;
            }

            if (!raidStartController.CanStartRaid(out string reason))
            {
                SetStatus(reason);
                Refresh();
                return;
            }

            SetStatus("출격을 시작합니다.");
            raidStartController.StartRaid();
            Refresh();
        }

        public void DebugUnlockMapBForLocalPlayer()
        {
            if (raidStartController == null) return;

            raidStartController.DebugUnlockMapBForLocalPlayer();
            Refresh();
        }

#if ODIN_INSPECTOR
        [Button("테스트: B맵 선택 가능 처리")]
#endif
        private void DebugUnlockMapBForLocalPlayerButton()
        {
            DebugUnlockMapBForLocalPlayer();
        }

        private void Refresh()
        {
            ResolveReferences();

            bool hasController = raidStartController != null;
            bool canLocalSelectMap = CanLocalPlayerSelectMap();
            bool canLocalStartRaid = CanLocalPlayerStartRaid();

            bool canMapB = false;
            string mapBReason = string.Empty;

            if (hasController)
                canMapB = raidStartController.CanSelectMap(LobbyRaidMap.MapB, out mapBReason);

            if (btnMapA != null)
                btnMapA.interactable = hasController && canLocalSelectMap;

            if (btnMapB != null)
                btnMapB.interactable = hasController && canLocalSelectMap && canMapB;

            if (useReadyButton && btnReady != null)
                btnReady.interactable = IsNetworkSessionActive();

            if (btnStart != null)
                btnStart.interactable = hasController && canLocalStartRaid && raidStartController.CanStartRaid();

            if (iconLock != null)
                iconLock.SetActive(!canMapB);

            if (btnMapBCanvasGroup != null)
                btnMapBCanvasGroup.alpha = canMapB ? 1f : lockedMapBAlpha;

            if (IsValidStatusText() && hasController && !canMapB)
                textStatus.text = mapBReason;
        }

        private void BindButtons()
        {
            if (isBound) return;

            if (btnMapA != null) btnMapA.onClick.AddListener(HandleMapAClicked);
            if (btnMapB != null) btnMapB.onClick.AddListener(HandleMapBClicked);
            if (useReadyButton && btnReady != null) btnReady.onClick.AddListener(HandleReadyClicked);

            if (btnStart != null)
            {
                btnStart.onClick.RemoveListener(HandleStartClicked);
                btnStart.onClick.AddListener(HandleStartClicked);
                btnStart.interactable = false;
            }

            isBound = true;
        }

        private void UnbindButtons()
        {
            if (!isBound) return;

            if (btnMapA != null) btnMapA.onClick.RemoveListener(HandleMapAClicked);
            if (btnMapB != null) btnMapB.onClick.RemoveListener(HandleMapBClicked);
            if (useReadyButton && btnReady != null) btnReady.onClick.RemoveListener(HandleReadyClicked);

            if (btnStart != null)
                btnStart.onClick.RemoveListener(HandleStartClicked);

            isBound = false;
        }

        private void BindLobbyState()
        {
            if (lobbyState == null) return;

            lobbyState.NetworkSpawned -= HandleLobbyStateChanged;
            lobbyState.NetworkDespawned -= HandleLobbyStateChanged;
            lobbyState.NetworkSpawned += HandleLobbyStateChanged;
            lobbyState.NetworkDespawned += HandleLobbyStateChanged;

            if (lobbyState.IsSpawned && lobbyState.Players != null)
            {
                lobbyState.Players.OnListChanged -= HandlePlayersChanged;
                lobbyState.Players.OnListChanged += HandlePlayersChanged;
            }
        }

        private void UnbindLobbyState()
        {
            if (lobbyState == null) return;

            lobbyState.NetworkSpawned -= HandleLobbyStateChanged;
            lobbyState.NetworkDespawned -= HandleLobbyStateChanged;

            if (lobbyState.Players != null)
                lobbyState.Players.OnListChanged -= HandlePlayersChanged;
        }

        private void BindRaidController()
        {
            if (raidStartController == null) return;

            raidStartController.SelectedMapChanged -= HandleSelectedMapChanged;
            raidStartController.SelectedMapChanged += HandleSelectedMapChanged;
        }

        private void UnbindRaidController()
        {
            if (raidStartController == null) return;

            raidStartController.SelectedMapChanged -= HandleSelectedMapChanged;
        }

        private void HandleLobbyStateChanged()
        {
            BindLobbyState();
            Refresh();
        }

        private void HandlePlayersChanged(NetworkListEvent<LobbyPlayerState> changeEvent)
        {
            Refresh();
        }

        private void HandleSelectedMapChanged(LobbyRaidMap previous, LobbyRaidMap current)
        {
            Refresh();
        }

        private void ResolveReferences()
        {
            if (raidStartController == null)
                raidStartController = FindObjectOfType<LobbyRaidStartController>();

            if (lobbyState == null)
                lobbyState = FindObjectOfType<NetworkLobbyState>();

            if (btnMapA == null)
                btnMapA = FindButton(btnMapAName);

            if (btnMapB == null)
                btnMapB = FindButton(btnMapBName);

            if (useReadyButton && btnReady == null)
                btnReady = FindButton(btnReadyName);

            if (btnStart == null)
                btnStart = FindButton(btnStartName);

            if (iconLock == null)
                iconLock = FindSceneObject(iconLockName);

            if (textStatus == null)
                textStatus = FindTMPText(textStatusName);

            if (btnMapBCanvasGroup == null && btnMapB != null)
            {
                btnMapBCanvasGroup = btnMapB.GetComponent<CanvasGroup>();

                if (btnMapBCanvasGroup == null)
                    btnMapBCanvasGroup = btnMapB.gameObject.AddComponent<CanvasGroup>();
            }
        }

        private Button FindButton(string objectName)
        {
            GameObject target = FindSceneObject(objectName);
            return target == null ? null : target.GetComponent<Button>();
        }

        private TMP_Text FindTMPText(string objectName)
        {
            GameObject target = FindSceneObject(objectName);
            return target == null ? null : target.GetComponent<TMP_Text>();
        }

        private GameObject FindSceneObject(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return null;

            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject sceneObject in objects)
            {
                if (sceneObject == null) continue;
                if (!sceneObject.scene.IsValid()) continue;
                if (sceneObject.name == objectName) return sceneObject;
            }

            return null;
        }

        private bool IsNetworkSessionActive()
        {
            return Unity.Netcode.NetworkManager.Singleton != null &&
                   Unity.Netcode.NetworkManager.Singleton.IsListening;
        }

        private bool CanLocalPlayerSelectMap()
        {
            if (!IsNetworkSessionActive())
                return true;

            return Unity.Netcode.NetworkManager.Singleton != null &&
                   Unity.Netcode.NetworkManager.Singleton.IsHost;
        }

        private bool CanLocalPlayerStartRaid()
        {
            if (!IsNetworkSessionActive())
                return false;

            if (Unity.Netcode.NetworkManager.Singleton == null ||
                !Unity.Netcode.NetworkManager.Singleton.IsClient)
            {
                return false;
            }

            return Unity.Netcode.NetworkManager.Singleton.LocalClientId ==
                   Unity.Netcode.NetworkManager.ServerClientId;
        }

        private void SetStatus(string message)
        {
            if (IsValidStatusText())
                textStatus.text = message;

            LogDebug(message);
        }

        private bool IsValidStatusText()
        {
            if (textStatus == null) return false;

            if (btnStart != null && textStatus.transform.IsChildOf(btnStart.transform))
            {
                if (!hasWarnedInvalidStatusText)
                {
                    Debug.LogWarning(
                        "[LobbyMapSelectUI] Text_Status가 Btn_Start 하위 텍스트로 연결되어 있어 버튼 라벨 변경을 막았습니다. " +
                        "출격 버튼 아래에 별도 TMP_Text를 만들고 Text_Status에 연결하세요.", this);
                    hasWarnedInvalidStatusText = true;
                }

                return false;
            }

            return true;
        }

        private void LogDebug(string message)
        {
            if (!logDebug) return;
            Debug.Log($"[LobbyMapSelectUI] {message}", this);
        }
    }
}
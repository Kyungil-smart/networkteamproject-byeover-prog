using System.Collections.Generic;

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 파티/Ready 영역에서 NetworkLobbyState와 LobbyPartyView 사이를 조율합니다.
    /// 네트워크 상태를 표시 데이터로 변환하고, UI 입력은 서버 RPC 요청으로 전달합니다.
    /// </summary>
    public class LobbyPartyController : MonoBehaviour
    {
        private const string MapBUnlockZoneId = "MapB_All";

        [Header("==== 연결 ====")]
        [Tooltip("HJO_Lobby 씬의 LobbyNetworkState 오브젝트에 붙은 NetworkLobbyState")]
        [SerializeField] private NetworkLobbyState lobbyState;

        [Tooltip("4개 파티 슬롯을 표시하는 View")]
        [SerializeField] private LobbyPartyView partyView;

        [Header("==== 표시 이름 ====")]
        [Tooltip("Cloud Save 표시 이름을 찾지 못했을 때 사용할 기본 이름")]
        [SerializeField] private string fallbackDisplayName = "Player";

        [Tooltip("표시 이름 최대 글자 수 FixedString64Bytes에 저장되며 한글 기준 약 20자")]
        [SerializeField, Range(1, 20)] private int maxDisplayNameCharacters = 20;

        [Header("==== 디버그 ====")]
        [Tooltip("파티/Ready 연결 흐름 로그를 출력할지 여부입니다.")]
        [SerializeField] private bool logDebug = false;

        private readonly List<LobbyPlayerState> sortedPlayers = new();
        private readonly List<LobbyPartySlotViewData> slotViewDataBuffer = new();

        private NetworkLobbyState subscribedLobbyState;
        private LobbyPartyView subscribedPartyView;

        private bool hasSubmittedLocalInfo;
        private string lastSubmittedDisplayName = string.Empty;
        private NetworkLobbyState lastSubmittedLobbyState;

        private bool hasSubmittedMapBUnlockState;
        private bool lastSubmittedMapBUnlockState;
        private NetworkLobbyState lastSubmittedMapBUnlockLobbyState;

        private bool isPlayersListSubscribed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            LobbyRoomController.PartyRoomVisibilityChanged += HandlePartyRoomVisibilityChanged;
            EventBus.Subscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);

            ResolveReferences();
            BindView();
            BindLobbyState();
            RefreshView();
            TrySubmitLobbyPlayerState();
        }

        private void Start()
        {
            ResolveReferences();
            BindView();
            BindLobbyState();
            RefreshView();
            TrySubmitLobbyPlayerState();
        }

        private void OnDisable()
        {
            LobbyRoomController.PartyRoomVisibilityChanged -= HandlePartyRoomVisibilityChanged;
            EventBus.Unsubscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);

            UnbindLobbyState();
            UnbindView();
        }

        /// <summary>
        /// View에서 올라온 Ready 클릭 요청을 서버 RPC로 전달합니다.
        /// </summary>
        private void HandleReadyClicked(bool desiredReady)
        {
            if (lobbyState == null || !lobbyState.IsSpawned) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            lobbyState.SetReadyServerRpc(desiredReady);
        }

        private void ResolveReferences()
        {
            if (lobbyState == null)
                lobbyState = FindFirstObjectByType<NetworkLobbyState>();

            if (partyView == null)
                partyView = GetComponent<LobbyPartyView>();
        }

        private void BindView()
        {
            if (partyView == null || subscribedPartyView == partyView) return;

            UnbindView();

            subscribedPartyView = partyView;
            subscribedPartyView.ReadyClicked += HandleReadyClicked;
        }

        private void UnbindView()
        {
            if (subscribedPartyView == null) return;

            subscribedPartyView.ReadyClicked -= HandleReadyClicked;
            subscribedPartyView = null;
        }

        private void BindLobbyState()
        {
            if (lobbyState == null || subscribedLobbyState == lobbyState) return;

            if (subscribedLobbyState != null && subscribedLobbyState != lobbyState)
                ResetLobbyPlayerStateSubmission();

            UnbindLobbyState();

            subscribedLobbyState = lobbyState;
            subscribedLobbyState.NetworkSpawned += HandleLobbyStateSpawned;
            subscribedLobbyState.NetworkDespawned += HandleLobbyStateDespawned;

            // NetworkList 변경 이벤트는 NetworkObject Spawn 이후 구독합니다.
            // 이미 Spawn된 상태에서 늦게 바인딩되는 경우 현재 상태도 즉시 반영합니다.
            if (subscribedLobbyState.IsSpawned)
            {
                SubscribePlayersList();
                RefreshView();
                TrySubmitLobbyPlayerState();
            }
            else
            {
                RefreshView();
            }
        }

        /// <summary>
        /// NetworkLobbyState가 Spawn된 뒤 Players 목록 변경 이벤트를 구독합니다.
        /// </summary>
        private void SubscribePlayersList()
        {
            if (subscribedLobbyState == null || subscribedLobbyState.Players == null)
            {
                LogDebug("플레이어 목록 구독 실패. LobbyState 또는 Players가 없습니다.");
                return;
            }

            if (isPlayersListSubscribed) return;

            subscribedLobbyState.Players.OnListChanged += HandlePlayersChanged;
            isPlayersListSubscribed = true;

            LogDebug("플레이어 목록 변경 이벤트 구독 완료.");
        }

        /// <summary>
        /// Players 목록 변경 이벤트 구독을 해제합니다.
        /// </summary>
        private void UnsubscribePlayersList()
        {
            if (subscribedLobbyState != null
                && subscribedLobbyState.Players != null
                && isPlayersListSubscribed)
            {
                subscribedLobbyState.Players.OnListChanged -= HandlePlayersChanged;
                LogDebug("플레이어 목록 변경 이벤트 구독 해제.");
            }

            isPlayersListSubscribed = false;
        }

        private void UnbindLobbyState()
        {
            if (subscribedLobbyState == null) return;

            UnsubscribePlayersList();

            subscribedLobbyState.NetworkSpawned -= HandleLobbyStateSpawned;
            subscribedLobbyState.NetworkDespawned -= HandleLobbyStateDespawned;

            subscribedLobbyState = null;
        }

        private void HandleLobbyStateSpawned()
        {
            LogDebug("NetworkLobbyState 스폰 완료.");

            SubscribePlayersList();
            RefreshView();
            TrySubmitLobbyPlayerState();
        }

        private void HandleLobbyStateDespawned()
        {
            LogDebug("NetworkLobbyState 디스폰 완료.");

            UnsubscribePlayersList();
            ResetLobbyPlayerStateSubmission();

            PartyPlayerColorCache.Clear();
            LobbyTeamColorCache.Clear();

            if (partyView != null)
                partyView.RenderEmpty();
        }

        private void HandlePlayersChanged(NetworkListEvent<LobbyPlayerState> changeEvent)
        {
            LogDebug($"플레이어 목록 변경 감지. 변경종류={changeEvent.Type}, 인덱스={changeEvent.Index}");
            RefreshView();
        }

        private void HandlePartyRoomVisibilityChanged()
        {
            ResolveReferences();
            BindLobbyState();
            TrySubmitLobbyPlayerState();
            RefreshView();
        }

        private void HandleCloudSaveLoaded(CloudSaveLoadedEvent e)
        {
            // Cloud Save 로드 이후 표시 이름과 MapB 해금 상태를 다시 제출합니다.
            ResetLobbyPlayerStateSubmission();
            TrySubmitLobbyPlayerState();
            RefreshView();
        }

        /// <summary>
        /// NetworkList 상태를 UI 표시용 ViewData로 변환해 View에 전달합니다.
        /// </summary>
        private void RefreshView()
        {
            if (partyView == null)
            {
                LogDebug("Party View가 없어 파티 화면 갱신을 건너뜁니다.");
                return;
            }

            if (!LobbyRoomController.IsPartyRoomVisible)
            {
                PartyPlayerColorCache.Clear();
                LobbyTeamColorCache.Clear();
                partyView.RenderEmpty();
                return;
            }

            BuildSortedPlayerBuffer();

            ulong localClientId = ulong.MaxValue;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                localClientId = NetworkManager.Singleton.LocalClientId;

            BuildSlotViewData(localClientId, partyView.SlotCount);
            partyView.Render(slotViewDataBuffer);
        }

        private void BuildSortedPlayerBuffer()
        {
            sortedPlayers.Clear();

            if (lobbyState == null || lobbyState.Players == null)
            {
                LogDebug("플레이어 정렬 버퍼 생성 실패. LobbyState 또는 Players가 없습니다.");
                return;
            }

            foreach (LobbyPlayerState player in lobbyState.Players)
            {
                sortedPlayers.Add(player);
            }

            sortedPlayers.Sort(CompareLobbyPlayersForDisplay);
        }

        private int CompareLobbyPlayersForDisplay(LobbyPlayerState a, LobbyPlayerState b)
        {
            if (a.IsHost != b.IsHost)
                return a.IsHost ? -1 : 1;

            return a.ClientId.CompareTo(b.ClientId);
        }

        private void BuildSlotViewData(ulong localClientId, int slotCount)
        {
            slotViewDataBuffer.Clear();
            PartyPlayerColorCache.Clear();

            for (int i = 0; i < slotCount; i++)
            {
                if (i >= sortedPlayers.Count)
                {
                    slotViewDataBuffer.Add(LobbyPartySlotViewData.Empty);
                    continue;
                }

                LobbyPlayerState player = sortedPlayers[i];

                PartyPlayerColorCache.Set(player.ClientId, player.IconColorRgba);

                Color32 iconColor = PartyPlayerColorCache.ToColor32(player.IconColorRgba);
                LobbyTeamColorCache.SetColor(player.ClientId, iconColor);

                string displayName = player.DisplayName.ToString();

                if (player.ClientId == localClientId)
                {
                    string localDisplayName = ResolveLocalDisplayName();

                    if (!string.IsNullOrWhiteSpace(localDisplayName))
                        displayName = localDisplayName;
                }

                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = player.ClientId == localClientId ? ResolveLocalDisplayName() : fallbackDisplayName;

                slotViewDataBuffer.Add(new LobbyPartySlotViewData
                {
                    HasPlayer = true,
                    ClientId = player.ClientId,
                    DisplayName = displayName,
                    IsHost = player.IsHost,
                    IsReady = player.IsReady,
                    IsLocalPlayer = player.ClientId == localClientId,
                    IconColor = iconColor
                });
            }
        }

        private void TrySubmitLobbyPlayerState()
        {
            TrySubmitLocalPlayerInfo();
            TrySubmitMapBUnlockState();
        }

        /// <summary>
        /// Cloud Save 표시 이름을 서버에 제출합니다.
        /// </summary>
        private void TrySubmitLocalPlayerInfo()
        {
            if (lobbyState == null || !lobbyState.IsSpawned) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            string displayName = ResolveLocalDisplayName();

            if (hasSubmittedLocalInfo
                && lastSubmittedLobbyState == lobbyState
                && lastSubmittedDisplayName == displayName)
            {
                return;
            }

            lobbyState.SubmitLocalPlayerInfoServerRpc(new FixedString64Bytes(displayName));

            hasSubmittedLocalInfo = true;
            lastSubmittedDisplayName = displayName;
            lastSubmittedLobbyState = lobbyState;

            LogDebug($"로비 플레이어 정보 제출 완료. 표시이름={displayName}");
        }

        /// <summary>
        /// 로드된 Cloud Save 진행도에서 MapB 접근 여부를 계산해 서버에 제출합니다.
        /// </summary>
        private void TrySubmitMapBUnlockState()
        {
            if (lobbyState == null || !lobbyState.IsSpawned) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            if (!TryResolveMapBUnlockStateFromCloudSave(out bool hasUnlockedMapB))
                return;

            if (hasSubmittedMapBUnlockState
                && lastSubmittedMapBUnlockLobbyState == lobbyState
                && lastSubmittedMapBUnlockState == hasUnlockedMapB)
            {
                return;
            }

            lobbyState.SubmitMapBUnlockStateServerRpc(hasUnlockedMapB);

            hasSubmittedMapBUnlockState = true;
            lastSubmittedMapBUnlockState = hasUnlockedMapB;
            lastSubmittedMapBUnlockLobbyState = lobbyState;

            LogDebug($"MapB 해금 상태 제출 완료. HasUnlockedMapB={hasUnlockedMapB}");
        }

        private string ResolveLocalDisplayName()
        {
            CloudSaveSystem cloudSave = ServiceLocator.Get<CloudSaveSystem>();

            if (cloudSave != null
                && cloudSave.HasLoadedData
                && cloudSave.CurrentData != null
                && cloudSave.CurrentData.profile != null
                && !string.IsNullOrWhiteSpace(cloudSave.CurrentData.profile.displayName))
            {
                return SanitizeDisplayName(cloudSave.CurrentData.profile.displayName);
            }

            return SanitizeDisplayName(fallbackDisplayName);
        }

        private bool TryResolveMapBUnlockStateFromCloudSave(out bool hasUnlockedMapB)
        {
            hasUnlockedMapB = false;

            CloudSaveSystem cloudSave = ServiceLocator.Get<CloudSaveSystem>();

            if (cloudSave == null || !cloudSave.HasLoadedData)
                return false;

            PlayerCloudData currentData = cloudSave.CurrentData;

            if (currentData == null ||
                currentData.progress == null ||
                currentData.progress.unlockedZones == null)
            {
                return false;
            }

            hasUnlockedMapB = currentData.progress.unlockedZones.Contains(MapBUnlockZoneId);
            return true;
        }

        private string SanitizeDisplayName(string value)
        {
            string fallback = string.IsNullOrWhiteSpace(fallbackDisplayName)
                ? "Player"
                : fallbackDisplayName.Trim();

            string sanitized = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "Player";

            if (sanitized.Length > maxDisplayNameCharacters)
                sanitized = sanitized.Substring(0, maxDisplayNameCharacters);

            return sanitized;
        }

        private void ResetLobbyPlayerStateSubmission()
        {
            hasSubmittedLocalInfo = false;
            lastSubmittedDisplayName = string.Empty;
            lastSubmittedLobbyState = null;

            hasSubmittedMapBUnlockState = false;
            lastSubmittedMapBUnlockState = false;
            lastSubmittedMapBUnlockLobbyState = null;
        }

        private void LogDebug(string msg)
        {
            if (!logDebug) return;
            Debug.Log($"[LobbyPartyController] {msg}", this);
        }
    }
}

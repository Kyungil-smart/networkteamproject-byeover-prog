using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeadZone.Core;
using DeadZone.Systems.Raid;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace DeadZone.Network
{
    /// <summary>
    /// 로비의 맵 선택 상태와 레이드 시작 가능 조건을 서버 권위 기준으로 판단합니다.
    /// 실제 씬 전환은 서버의 NetworkSceneManager를 통해 처리합니다.
    /// </summary>
    public class LobbyRaidStartController : NetworkBehaviour
    {
        private const int RaidLoadoutSubmissionTimeoutMs = 2500;
        private const int RaidLoadoutSubmissionPollMs = 50;

        [Header("==== 로비 참조 ====")]
        [SerializeField] private NetworkLobbyState lobbyState;
        [SerializeField] private NetworkGameManager gameManager;

        [Header("==== 맵 씬 이름 ====")]
        [SerializeField] private string mapASceneName = "Game_Stage_1";
        [SerializeField] private string mapBSceneName = "Game_Stage_2";

        [Header("==== 싱글/테스트 ====")]
        [FormerlySerializedAs("offlineHasEscapedMapA")]
        [SerializeField] private bool offlineHasUnlockedMapB;

        [Header("==== 디버그 ====")]
        [SerializeField] private bool logDebug;

        private readonly NetworkVariable<LobbyRaidMap> selectedMap = new(
            LobbyRaidMap.MapA,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<ulong> expectedClientIdsBuffer = new();

        private LobbyRaidMap offlineSelectedMap = LobbyRaidMap.MapA;
        private bool hasRaidStartRequested;
        private bool isStartingSoloSession;

        public LobbyRaidMap SelectedMap => IsNetworkSessionActive() ? selectedMap.Value : offlineSelectedMap;
        public event Action<LobbyRaidMap, LobbyRaidMap> SelectedMapChanged;

        public override void OnNetworkSpawn()
        {
            ResolveReferences();
            hasRaidStartRequested = false;
            selectedMap.OnValueChanged += HandleSelectedMapChanged;

            if (IsServer)
                selectedMap.Value = LobbyRaidMap.MapA;
        }

        public override void OnNetworkDespawn()
        {
            selectedMap.OnValueChanged -= HandleSelectedMapChanged;
        }

        public void SelectMap(LobbyRaidMap map)
        {
            Debug.Log(
                $"[LobbyRaidStart] SelectMap requested. map={map}, object={name}, isSpawned={IsSpawned}, hasNetworkObject={NetworkObject != null}",
                this);

            if (IsNetworkSessionActive())
            {
                if (!CanSendSelectMapRpc(out string rpcBlockReason))
                {
                    LogDebug(rpcBlockReason);
                    return;
                }

                if (NetworkManager.Singleton.IsServer)
                {
                    ApplySelectedMapOnServer(map, NetworkManager.ServerClientId);
                    return;
                }

                SelectMapServerRpc(map);
                return;
            }

            if (!CanSelectMap(map, out string reason))
            {
                LogDebug(reason);
                return;
            }

            LobbyRaidMap previous = offlineSelectedMap;
            offlineSelectedMap = map;
            SelectedMapChanged?.Invoke(previous, offlineSelectedMap);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SelectMapServerRpc(LobbyRaidMap map, RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            Debug.Log($"[LobbyRaidStart] SelectMapServerRpc received. senderClientId={senderClientId}, map={map}", this);
            ApplySelectedMapOnServer(map, senderClientId);
        }

        private void ApplySelectedMapOnServer(LobbyRaidMap map, ulong senderClientId)
        {
            if (!IsServer) return;

            if (!IsHostClient(senderClientId))
            {
                LogDebug($"Map selection request ignored because sender is not host. ClientId={senderClientId}");
                return;
            }

            if (!CanSelectMap(map, out string reason))
            {
                LogDebug(reason);
                return;
            }

            selectedMap.Value = map;
        }

        private bool CanSendSelectMapRpc(out string reason)
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                reason = "[LobbyRaidStart] NetworkManager null. Cannot send ServerRpc.";
                Debug.LogWarning(reason, this);
                return false;
            }

            if (!networkManager.IsListening)
            {
                reason = "[LobbyRaidStart] NetworkManager not listening. Cannot send ServerRpc.";
                Debug.LogWarning(reason, this);
                return false;
            }

            if (NetworkObject == null)
            {
                reason = "[LobbyRaidStart] NetworkObject missing. Cannot send ServerRpc.";
                Debug.LogWarning(reason, this);
                return false;
            }

            if (!IsSpawned)
            {
                reason = "[LobbyRaidStart] Not spawned. Cannot send ServerRpc.";
                Debug.LogWarning(reason, this);
                return false;
            }

            if (!networkManager.IsServer && networkManager.LocalClientId != NetworkManager.ServerClientId)
            {
                reason = "Map selection is restricted to the party host.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        public void StartRaid()
        {
            _ = StartRaidAsync();
        }

        private async Task StartRaidAsync()
        {
            if (!IsNetworkSessionActive())
            {
                if (!await TryStartSoloSessionAsync())
                    return;
            }

            if (!CanStartRaid(out string reason))
            {
                LogDebug(reason);
                return;
            }

            await SubmitRaidLoadoutsBeforeStartAsync();
            StartRaidServerRpc();
        }

        private async Task SubmitRaidLoadoutsBeforeStartAsync()
        {
            RaidLoadoutTransferService.Clear();
            RaidLoadoutTransferService.StoreLocalLobbyLoadoutForLocalClient();
            LobbyPlayerCustomizeCache.Clear();
            LobbyPlayerCustomizeCache.StoreLocalCustomizeForLocalClient();

            if (!IsServer || NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
                return;

            RequestRaidLoadoutSubmissionClientRpc();

            string missingClientIds = string.Empty;
            int waitedMs = 0;
            while (waitedMs < RaidLoadoutSubmissionTimeoutMs &&
                   !HaveAllConnectedClientLoadouts(out missingClientIds))
            {
                await Task.Delay(RaidLoadoutSubmissionPollMs);
                waitedMs += RaidLoadoutSubmissionPollMs;
            }

            if (!HaveAllConnectedClientLoadouts(out missingClientIds))
            {
                Debug.LogWarning(
                    $"[RaidLoadout] Timed out waiting for lobby loadout submissions. MissingClientIds={missingClientIds}",
                    this);
            }
        }

        private bool HaveAllConnectedClientLoadouts(out string missingClientIds)
        {
            missingClientIds = string.Empty;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            List<ulong> missing = null;
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (RaidLoadoutTransferService.HasLoadoutForClient(clientId))
                    continue;

                missing ??= new List<ulong>();
                missing.Add(clientId);
            }

            if (missing == null || missing.Count == 0)
                return true;

            missingClientIds = string.Join(", ", missing);
            return false;
        }

        private async Task<bool> TryStartSoloSessionAsync()
        {
            if (isStartingSoloSession)
                return false;

            ResolveReferences();

            SessionManager sessionManager = ServiceLocator.Get<SessionManager>();
            if (sessionManager == null)
                sessionManager = FindFirstObjectByType<SessionManager>(FindObjectsInactive.Include);

            if (sessionManager == null)
            {
                Debug.LogWarning("[LobbyRaidStartController] 1인 출격을 시작할 SessionManager를 찾지 못했습니다.", this);
                return false;
            }

            isStartingSoloSession = true;

            try
            {
                if (!sessionManager.StartHost())
                {
                    Debug.LogWarning("[LobbyRaidStartController] 1인 출격용 로컬 호스트 시작에 실패했습니다.", this);
                    return false;
                }

                return await WaitForLobbyStateReadyAsync();
            }
            finally
            {
                isStartingSoloSession = false;
            }
        }

        private async Task<bool> WaitForLobbyStateReadyAsync()
        {
            for (int i = 0; i < 60; i++)
            {
                ResolveReferences();

                if (IsNetworkSessionActive() &&
                    lobbyState != null &&
                    lobbyState.IsSpawned &&
                    lobbyState.Players != null &&
                    lobbyState.Players.Count > 0)
                {
                    return true;
                }

                await Task.Delay(50);
            }

            Debug.LogWarning("[LobbyRaidStartController] 1인 출격용 로비 상태가 준비되지 않았습니다.", this);
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartRaidServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            if (hasRaidStartRequested)
            {
                Debug.LogWarning("[LobbyRaidStartController] 이미 레이드 시작 요청이 처리 중입니다.", this);
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!CanRequestRaidStart(senderClientId, out string reason))
            {
                Debug.LogWarning($"[LobbyRaidStartController] 레이드 시작 요청 거부: {reason}", this);
                return;
            }

            if (!TryGetSelectedRaidSceneName(out string sceneName, out reason))
            {
                Debug.LogWarning($"[LobbyRaidStartController] 레이드 씬 확인 실패: {reason}", this);
                return;
            }

            if (!TryCollectExpectedClientIds(expectedClientIdsBuffer, out reason))
            {
                Debug.LogWarning($"[LobbyRaidStartController] 출격 대상 확인 실패: {reason}", this);
                return;
            }

            CacheLobbyTeamColorsForRaid();
            LobbyPlayerCustomizeCache.SaveCustomizesForClients(expectedClientIdsBuffer);
            RaidLoadoutTransferService.SaveLoadoutsForClients(expectedClientIdsBuffer);

            if (!TryBeginLoadTracking(sceneName, expectedClientIdsBuffer, out reason))
            {
                Debug.LogWarning($"[LobbyRaidStartController] 로드 추적 시작 실패: {reason}", this);
                return;
            }

            hasRaidStartRequested = true;

            if (!TryLoadSelectedRaidScene(sceneName, out reason))
            {
                hasRaidStartRequested = false;
                CancelLoadTracking(reason);
                Debug.LogWarning($"[LobbyRaidStartController] 레이드 씬 로드 실패: {reason}", this);
            }
        }

        [ClientRpc]
        private void RequestRaidLoadoutSubmissionClientRpc()
        {
            string loadoutJson = RaidLoadoutTransferService.CreateLocalLobbyLoadoutJson();
            if (!string.IsNullOrWhiteSpace(loadoutJson))
                SubmitRaidLoadoutServerRpc(loadoutJson);

            string customizeJson = LobbyPlayerCustomizeCache.CreateLocalCustomizeJson();
            if (!string.IsNullOrWhiteSpace(customizeJson))
                SubmitRaidCustomizeServerRpc(customizeJson);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitRaidLoadoutServerRpc(string loadoutJson, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            RaidLoadoutTransferService.StoreSubmittedLobbyLoadout(rpcParams.Receive.SenderClientId, loadoutJson);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitRaidCustomizeServerRpc(string customizeJson, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            LobbyPlayerCustomizeCache.StoreSubmittedCustomize(rpcParams.Receive.SenderClientId, customizeJson);
        }

        public bool CanStartRaid()
        {
            return CanStartRaid(out _);
        }

        public bool CanStartRaid(out string reason)
        {
            LobbyRaidMap map = SelectedMap;

            if (!IsNetworkSessionActive())
            {
                reason = "네트워크 로비가 시작된 뒤 출격할 수 있습니다.";
                return false;
            }

            if (hasRaidStartRequested)
            {
                reason = "이미 출격을 시작했습니다.";
                return false;
            }

            ResolveReferences();

            if (lobbyState == null || lobbyState.Players == null || lobbyState.Players.Count == 0)
            {
                reason = "로비 파티 정보를 확인할 수 없습니다.";
                return false;
            }

            int partyCount = lobbyState.Players.Count;

            if (partyCount < 1 || partyCount > 4)
            {
                reason = "파티 인원은 1명 이상 4명 이하여야 합니다.";
                return false;
            }

            if (!CanSelectMap(map, out reason))
                return false;

            if (partyCount > 1 && !AreAllPlayersReady())
            {
                reason = "모든 파티원이 준비 완료 상태여야 합니다.";
                return false;
            }

            reason = "레이드 시작 가능";
            return true;
        }

        public bool CanSelectMap(LobbyRaidMap map)
        {
            return CanSelectMap(map, out _);
        }

        public bool CanSelectMap(LobbyRaidMap map, out string reason)
        {
            switch (map)
            {
                case LobbyRaidMap.MapA:
                    reason = "MapA 선택 가능";
                    return true;

                case LobbyRaidMap.MapB:
                    return CanEnterMapB(out reason);

                default:
                    reason = "알 수 없는 맵입니다.";
                    return false;
            }
        }

        public bool CanEnterMapB()
        {
            return CanEnterMapB(out _);
        }

        public bool CanEnterMapB(out string reason)
        {
            if (!IsNetworkSessionActive())
            {
                reason = offlineHasUnlockedMapB
                    ? "MapB 선택 가능"
                    : "MapB가 해금되어 있어야 선택할 수 있습니다.";
                return offlineHasUnlockedMapB;
            }

            ResolveReferences();

            if (lobbyState == null || lobbyState.Players == null || lobbyState.Players.Count == 0)
            {
                reason = "로비 파티 정보를 확인할 수 없습니다.";
                return false;
            }

            for (int i = 0; i < lobbyState.Players.Count; i++)
            {
                if (!lobbyState.Players[i].HasUnlockedMapB)
                {
                    reason = "현재 파티원 모두가 MapB를 해금해야 선택할 수 있습니다.";
                    return false;
                }
            }

            reason = "MapB 선택 가능";
            return true;
        }

        public string GetSceneName(LobbyRaidMap map)
        {
            switch (map)
            {
                case LobbyRaidMap.MapA:
                    return mapASceneName;

                case LobbyRaidMap.MapB:
                    return mapBSceneName;

                default:
                    return string.Empty;
            }
        }

        public string GetStatusMessage()
        {
            CanStartRaid(out string reason);
            return reason;
        }

        public void DebugUnlockMapBForLocalPlayer()
        {
            if (!IsNetworkSessionActive())
            {
                offlineHasUnlockedMapB = true;
                return;
            }

            ResolveReferences();

            if (lobbyState != null &&
                lobbyState.IsSpawned &&
                Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsClient)
            {
                lobbyState.SubmitMapBUnlockStateServerRpc(true);
            }
        }

#if ODIN_INSPECTOR
        [Button("테스트: B맵 선택 가능 처리")]
#endif
        private void DebugUnlockMapBForLocalPlayerButton()
        {
            DebugUnlockMapBForLocalPlayer();
        }

        private bool AreAllPlayersReady()
        {
            ResolveReferences();

            if (lobbyState == null || lobbyState.Players == null)
                return false;

            for (int i = 0; i < lobbyState.Players.Count; i++)
            {
                if (!lobbyState.Players[i].IsReady)
                    return false;
            }

            return true;
        }

        private bool CanRequestRaidStart(ulong senderClientId, out string reason)
        {
            if (!IsServer)
            {
                reason = "서버에서만 출격을 시작할 수 있습니다.";
                return false;
            }

            if (!IsHostClient(senderClientId))
            {
                reason = $"Host가 아닌 Client의 출격 요청입니다. ClientId={senderClientId}";
                return false;
            }

            if (!CanStartRaid(out reason))
                return false;

            reason = "출격 요청 가능";
            return true;
        }

        private bool TryGetSelectedRaidSceneName(out string sceneName, out string reason)
        {
            sceneName = GetSceneName(selectedMap.Value);

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reason = $"선택된 맵의 씬 이름이 비어 있습니다. Map={selectedMap.Value}";
                return false;
            }

            reason = "레이드 씬 확인 완료";
            return true;
        }

        private bool TryCollectExpectedClientIds(List<ulong> clientIds, out string reason)
        {
            clientIds.Clear();
            ResolveReferences();

            if (!IsServer)
            {
                reason = "서버에서만 출격 대상 clientId 목록을 수집할 수 있습니다.";
                return false;
            }

            if (lobbyState == null || lobbyState.Players == null || lobbyState.Players.Count == 0)
            {
                reason = "출격 대상 clientId 목록을 확인할 수 없습니다.";
                return false;
            }

            for (int i = 0; i < lobbyState.Players.Count; i++)
            {
                ulong clientId = lobbyState.Players[i].ClientId;

                if (!clientIds.Contains(clientId))
                    clientIds.Add(clientId);
            }

            if (clientIds.Count == 0)
            {
                reason = "유효한 출격 대상 clientId가 없습니다.";
                return false;
            }

            reason = $"출격 대상 clientId 목록 확인 완료. Count={clientIds.Count}";
            return true;
        }

        private void CacheLobbyTeamColorsForRaid()
        {
            if (!IsServer || lobbyState == null || lobbyState.Players == null)
                return;

            for (int i = 0; i < lobbyState.Players.Count; i++)
            {
                LobbyPlayerState player = lobbyState.Players[i];
                Color32 iconColor = PartyPlayerColorCache.ToColor32(player.IconColorRgba);
                LobbyTeamColorCache.SetColor(player.ClientId, iconColor);
            }
        }

        private bool TryBeginLoadTracking(string sceneName, IReadOnlyList<ulong> clientIds, out string reason)
        {
            if (!TryResolveGameSessionManager(out GameSessionManager gameSessionManager, out reason))
                return false;

            return gameSessionManager.BeginLoadTracking(sceneName, clientIds, out reason);
        }

        private void CancelLoadTracking(string reason)
        {
            if (!TryResolveGameSessionManager(out GameSessionManager gameSessionManager, out _))
                return;

            gameSessionManager.CancelLoadTracking(reason);
        }

        private bool TryResolveGameSessionManager(out GameSessionManager gameSessionManager, out string reason)
        {
            gameSessionManager = null;

            if (ServiceLocator.TryGet<GameSessionManager>(out GameSessionManager registeredManager))
                gameSessionManager = registeredManager;

            if (gameSessionManager == null)
                gameSessionManager = FindFirstObjectByType<GameSessionManager>();

            if (gameSessionManager == null)
            {
                reason = "GameSessionManager를 찾을 수 없습니다.";
                return false;
            }

            if (!gameSessionManager.IsSpawned)
            {
                reason = "GameSessionManager가 아직 Network Spawn되지 않았습니다.";
                return false;
            }

            reason = "GameSessionManager 확인 완료";
            return true;
        }

        private bool TryLoadSelectedRaidScene(string sceneName, out string reason)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reason = "로드할 씬 이름이 비어 있습니다.";
                return false;
            }

            if (Unity.Netcode.NetworkManager.Singleton == null)
            {
                reason = "NetworkManager.Singleton을 찾을 수 없습니다.";
                return false;
            }

            if (Unity.Netcode.NetworkManager.Singleton.SceneManager == null)
            {
                reason = "NetworkSceneManager를 찾을 수 없습니다.";
                return false;
            }

            SceneEventProgressStatus status =
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                reason = $"NetworkSceneManager.LoadScene을 시작하지 못했습니다. Status={status}";
                return false;
            }

            reason = $"레이드 씬 로드를 시작했습니다. Scene={sceneName}";
            return true;
        }

        private void ResolveReferences()
        {
            if (lobbyState == null)
                lobbyState = FindFirstObjectByType<NetworkLobbyState>();

            if (gameManager == null)
                gameManager = FindFirstObjectByType<NetworkGameManager>();
        }

        private bool IsNetworkSessionActive()
        {
            return Unity.Netcode.NetworkManager.Singleton != null &&
                   Unity.Netcode.NetworkManager.Singleton.IsListening;
        }

        private bool IsHostClient(ulong clientId)
        {
            return clientId == Unity.Netcode.NetworkManager.ServerClientId;
        }

        private void HandleSelectedMapChanged(LobbyRaidMap previous, LobbyRaidMap current)
        {
            SelectedMapChanged?.Invoke(previous, current);
        }

        private void LogDebug(string message)
        {
            if (!logDebug) return;
            Debug.Log($"[LobbyRaidStartController] {message}", this);
        }
    }
}

using System;
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

        private LobbyRaidMap offlineSelectedMap = LobbyRaidMap.MapA;
        private bool hasRaidStartRequested;

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
            if (IsNetworkSessionActive())
            {
                if (Unity.Netcode.NetworkManager.Singleton == null ||
                    !Unity.Netcode.NetworkManager.Singleton.IsHost)
                {
                    LogDebug("맵 선택은 Host만 변경할 수 있습니다.");
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

            if (!IsHostClient(senderClientId))
            {
                LogDebug($"Host가 아닌 Client의 맵 선택 요청을 무시합니다. ClientId={senderClientId}");
                return;
            }

            if (!CanSelectMap(map, out string reason))
            {
                LogDebug(reason);
                return;
            }

            selectedMap.Value = map;
        }

        public void StartRaid()
        {
            if (!IsNetworkSessionActive())
            {
                LogDebug("네트워크 로비가 시작된 뒤 출격할 수 있습니다.");
                return;
            }

            if (!CanStartRaid(out string reason))
            {
                LogDebug(reason);
                return;
            }

            StartRaidServerRpc();
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

            hasRaidStartRequested = true;

            if (!TryLoadSelectedRaidScene(sceneName, out reason))
            {
                hasRaidStartRequested = false;
                Debug.LogWarning($"[LobbyRaidStartController] 레이드 씬 로드 실패: {reason}", this);
            }
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

            if (!AreAllPlayersReady())
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

        private int GetPartyCount()
        {
            if (!IsNetworkSessionActive()) return 1;

            ResolveReferences();

            if (lobbyState == null || lobbyState.Players == null || lobbyState.Players.Count == 0)
                return 1;

            return lobbyState.Players.Count;
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
                lobbyState = FindObjectOfType<NetworkLobbyState>();

            if (gameManager == null)
                gameManager = FindObjectOfType<NetworkGameManager>();
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
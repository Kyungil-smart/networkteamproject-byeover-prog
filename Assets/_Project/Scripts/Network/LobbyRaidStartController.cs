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
    /// 실제 씬 전환은 레이드 시작 흐름에서 별도로 처리합니다.
    /// </summary>
    public class LobbyRaidStartController : NetworkBehaviour
    {
        [Header("==== 로비 참조 ====")]
        [SerializeField] private NetworkLobbyState lobbyState;
        [SerializeField] private NetworkGameManager gameManager;

        [Header("==== 맵 씬 이름 ====")]
        [SerializeField] private string mapASceneName = "LSH_Game_Scene";
        [SerializeField] private string mapBSceneName = "Stage2";

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
            if (!CanStartRaid(out string reason))
            {
                LogDebug(reason);
                return;
            }

            if (IsNetworkSessionActive())
            {
                StartRaidServerRpc();
                return;
            }

            SceneManager.LoadScene(GetSceneName(offlineSelectedMap), LoadSceneMode.Single);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartRaidServerRpc()
        {
            if (!IsServer) return;
            if (hasRaidStartRequested) return;

            if (!CanStartRaid(out string reason))
            {
                Debug.LogWarning($"[LobbyRaidStartController] 레이드 시작 실패: {reason}", this);
                return;
            }

            hasRaidStartRequested = true;
            string sceneName = GetSceneName(selectedMap.Value);

            ResolveReferences();

            if (gameManager != null)
            {
                gameManager.StartRaidOnServer(sceneName);
                return;
            }

            if (Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.SceneManager != null)
            {
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
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
            return map == LobbyRaidMap.MapB ? mapBSceneName : mapASceneName;
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
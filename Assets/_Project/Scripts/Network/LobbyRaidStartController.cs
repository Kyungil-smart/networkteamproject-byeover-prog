using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace DeadZone.Network
{
    /// <summary>
    /// 로비 맵 선택과 레이드 시작 검증을 서버 권위로 처리합니다.
    /// UI는 이 컴포넌트를 호출만 하고 네트워크 동기화 대상에 포함하지 않습니다.
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
        [SerializeField] private bool offlineHasEscapedMapA;

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
            if (!CanSelectMap(map, out string reason))
            {
                LogDebug(reason);
                return;
            }

            if (IsNetworkSessionActive())
            {
                SelectMapServerRpc(map);
                return;
            }

            LobbyRaidMap previous = offlineSelectedMap;
            offlineSelectedMap = map;
            SelectedMapChanged?.Invoke(previous, offlineSelectedMap);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SelectMapServerRpc(LobbyRaidMap map)
        {
            if (!IsServer) return;
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

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        public bool CanStartRaid()
        {
            return CanStartRaid(out _);
        }

        public bool CanStartRaid(out string reason)
        {
            LobbyRaidMap map = SelectedMap;

            if (!CanSelectMap(map, out reason))
                return false;

            int partyCount = GetPartyCount();

            if (partyCount >= 2 && !AreAllPlayersReady())
            {
                reason = "파티원이 2명 이상이면 모든 플레이어가 준비 완료 상태여야 합니다.";
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
                    reason = "Map A 선택 가능";
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
                reason = offlineHasEscapedMapA
                    ? "Map B 선택 가능"
                    : "Map A를 1회 이상 탈출해야 Map B를 선택할 수 있습니다.";
                return offlineHasEscapedMapA;
            }

            ResolveReferences();

            if (lobbyState == null || lobbyState.Players == null || lobbyState.Players.Count == 0)
            {
                reason = "로비 파티 정보를 확인할 수 없습니다.";
                return false;
            }

            for (int i = 0; i < lobbyState.Players.Count; i++)
            {
                if (!lobbyState.Players[i].HasEscapedMapA)
                {
                    reason = "현재 파티원 모두가 Map A를 1회 이상 탈출해야 Map B를 선택할 수 있습니다.";
                    return false;
                }
            }

            reason = "Map B 선택 가능";
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
                offlineHasEscapedMapA = true;
                return;
            }

            ResolveReferences();

            if (lobbyState != null && lobbyState.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                lobbyState.SetMapAEscapedServerRpc(true);
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
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
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

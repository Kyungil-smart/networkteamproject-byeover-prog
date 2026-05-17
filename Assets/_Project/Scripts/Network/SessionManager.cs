using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비 + 매칭메이킹. NetworkBootstrap에 부착.
    /// NetworkManager의 Host/Client/Server 시작 호출을 래핑한다.
    ///
    /// v1.3 (Part VII Addendum): Relay 통합. RelayManager와 협업하여
    /// UnityTransport를 Relay 모드로 설정한 후 Host/Client를 시작한다.
    /// </summary>
    /// <remarks>
    /// 책임 분리:
    ///  - RelayManager: Relay Allocation 생성/조회만 담당
    ///  - SessionManager: Transport 설정 + NetworkManager.StartHost/Client 호출
    /// </remarks>
    public class SessionManager : MonoBehaviour
    {
        private const int NetworkShutdownPollMilliseconds = 50;
        private const int NetworkShutdownTimeoutMilliseconds = 2500;
        private const int StaleLobbyObjectDestroyWaitFrames = 8;

        private static readonly string[] PartySessionPlayerPrefsKeys =
        {
            "partyId",
            "PartyId",
            "partyMembers",
            "PartyMembers",
            "readyState",
            "ReadyState",
            "selectedMap",
            "SelectedMap"
        };

        private static bool staleRuntimePartySessionCleared;

        private string currentPartyId = string.Empty;
        private string previousSceneName = string.Empty;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForPlayMode()
        {
            staleRuntimePartySessionCleared = false;
        }
#endif

        private void Awake()
        {
            ClearStaleRuntimePartySessionOnGameStart();
            ServiceLocator.Register(this);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ServiceLocator.Unregister(this);
        }

        private void OnApplicationQuit()
        {
            currentPartyId = string.Empty;
            ClearLocalPartyCache();
        }

        // =================================================================
        // LOCAL / DEV - 에디터 로컬 테스트나 Dedicated 이관 시 사용
        // =================================================================

        public bool StartHost()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton이 null이다");
                return false;
            }

            if (networkManager.IsListening)
            {
                if (networkManager.IsServer)
                {
                    Debug.LogWarning(
                        $"[SessionManager] Host start ignored because NetworkManager is already listening. partyId={currentPartyId}",
                        this);
                    return true;
                }

                Debug.LogWarning("[SessionManager] Host start blocked because this instance is already connected as a client.", this);
                return false;
            }

            bool started = networkManager.StartHost();
            if (started)
            {
                currentPartyId = "LocalHost";
                Debug.Log($"[PartySession] Create party. partyId={currentPartyId}, hostClientId={NetworkManager.ServerClientId}", this);
            }

            return started;
        }

        public bool StartClient()
        {
            if (NetworkManager.Singleton == null) return false;
            bool started = NetworkManager.Singleton.StartClient();
            if (started)
                currentPartyId = "LocalClient";

            return started;
        }

        public bool StartServer()
        {
            if (NetworkManager.Singleton == null) return false;
            return NetworkManager.Singleton.StartServer();
        }

        public void Disconnect()
        {
            DisconnectInternal("ManualLeave");
        }

        public void Disconnect(string reason)
        {
            DisconnectInternal(reason);
        }

        public static void DisconnectActiveSession(string reason)
        {
            string disconnectReason = string.IsNullOrWhiteSpace(reason)
                ? "SessionCleanup"
                : reason;

            SessionManager sessionManager = ServiceLocator.Get<SessionManager>();
            if (sessionManager == null)
                sessionManager = UnityEngine.Object.FindFirstObjectByType<SessionManager>(FindObjectsInactive.Include);

            if (sessionManager != null)
            {
                sessionManager.Disconnect(disconnectReason);
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (IsNetworkManagerActive(networkManager))
            {
                Debug.Log($"[PartySession] Disconnect active network session. reason={disconnectReason}");
                networkManager.Shutdown();
            }

            ClearLocalPartyCache();
        }

        // =================================================================
        // RELAY - itch.io 배포용. NAT 터널링으로 포트포워딩 불필요
        // =================================================================

        /// <summary>
        /// Relay로 호스트 시작. 반환된 JoinCode를 UI에 표시하고 친구에게 공유한다.
        /// 실패 시 빈 문자열 반환.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync(int maxPlayers = 4)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton is null");
                return string.Empty;
            }

            if (!await PrepareNetworkManagerForNewSessionAsync(networkManager, "StartHostWithRelay"))
                return string.Empty;

            await DestroyStaleLobbyNetworkObjectsBeforeStartAsync();

            var relay = ServiceLocator.Get<RelayManager>();
            if (relay == null)
            {
                Debug.LogError("[SessionManager] RelayManager가 등록되어 있지 않다");
                return string.Empty;
            }

            Allocation allocation = await relay.CreateAllocationAsync(maxPlayers);
            if (allocation == null) return string.Empty;

            string joinCode = await relay.GetJoinCodeAsync(allocation);
            if (string.IsNullOrEmpty(joinCode)) return string.Empty;

            if (!ConfigureTransportAsHost(allocation))
            {
                return string.Empty;
            }

            bool ok = networkManager.StartHost();
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartHost 실패");
                return string.Empty;
            }

            currentPartyId = joinCode;
            Debug.Log($"[PartySession] Create party. partyId={currentPartyId}, hostClientId={NetworkManager.ServerClientId}", this);

            EventBus.Publish(new RelayAllocationCreatedEvent
            {
                joinCode = joinCode,
                maxConnections = maxPlayers,
            });

            return joinCode;
        }

        /// <summary>
        /// 친구가 공유한 JoinCode로 Relay 접속. 실패 시 false.
        /// </summary>
        public async Task<bool> StartClientWithJoinCodeAsync(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode)) return false;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton is null");
                return false;
            }

            if (!await PrepareNetworkManagerForNewSessionAsync(networkManager, "StartClientWithRelay"))
                return false;

            await DestroyStaleLobbyNetworkObjectsBeforeStartAsync();

            var relay = ServiceLocator.Get<RelayManager>();
            if (relay == null)
            {
                Debug.LogError("[SessionManager] RelayManager가 등록되어 있지 않다");
                return false;
            }

            JoinAllocation joinAlloc = await relay.JoinAllocationAsync(joinCode);
            if (joinAlloc == null) return false;

            if (!ConfigureTransportAsClient(joinAlloc))
            {
                return false;
            }

            bool ok = networkManager.StartClient();
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartClient 실패");
                return false;
            }

            currentPartyId = joinCode;

            EventBus.Publish(new RelayJoinedEvent { joinCode = joinCode });
            return true;
        }

        // =================================================================
        // Transport 설정 헬퍼 - UTP에 Relay 서버 정보 주입
        // =================================================================

        private bool ConfigureTransportAsHost(Allocation a)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[SessionManager] NetworkManager에 UnityTransport가 없다");
                return false;
            }

            transport.SetRelayServerData(new Unity.Networking.Transport.Relay.RelayServerData(a, "dtls"));
            return true;
        }

        private bool ConfigureTransportAsClient(JoinAllocation a)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[SessionManager] NetworkManager에 UnityTransport가 없다");
                return false;
            }

            transport.SetRelayServerData(new Unity.Networking.Transport.Relay.RelayServerData(a, "dtls"));
            return true;
        }

        private void ClearStaleRuntimePartySessionOnGameStart()
        {
            if (staleRuntimePartySessionCleared)
                return;

            staleRuntimePartySessionCleared = true;
            Debug.Log("[PartySession] Game start. Clearing stale runtime party session.", this);
            ClearLocalPartyCache();

            NetworkManager networkManager = NetworkManager.Singleton;
            if (!IsNetworkManagerActive(networkManager))
                return;

            string storedPartyId = string.IsNullOrWhiteSpace(currentPartyId)
                ? "active-network-session"
                : currentPartyId;
            Debug.LogWarning($"[PartySession] Prevent restore stale party. storedPartyId={storedPartyId}", this);
            networkManager.Shutdown();
        }

        private void DisconnectInternal(string reason)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                ClearLocalPartyCache();
                return;
            }

            bool isListening = networkManager.IsListening;
            bool isServer = isListening && networkManager.IsServer;
            bool isClient = isListening && networkManager.IsClient;
            ulong localClientId = isListening ? networkManager.LocalClientId : 0;

            if (isServer)
            {
                string partyId = string.IsNullOrWhiteSpace(currentPartyId) ? "unknown" : currentPartyId;
                Debug.Log($"[PartySession] Disband party. partyId={partyId}, reason={reason}", this);
            }
            else if (isClient)
            {
                Debug.Log($"[PartySession] Leave party. clientId={localClientId}, reason={reason}", this);
            }

            ForceShutdownNetworkManager(networkManager, reason);

            currentPartyId = string.Empty;
            ClearLocalPartyCache();
        }

        private static void ClearLocalPartyCache()
        {
            for (int i = 0; i < PartySessionPlayerPrefsKeys.Length; i++)
            {
                string key = PartySessionPlayerPrefsKeys[i];
                if (PlayerPrefs.HasKey(key))
                    PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
            Debug.Log("[PartySession] Clear local party cache.");
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            bool keepPartySession = IsNetworkManagerActive(networkManager);
            Debug.Log($"[PartySession] Scene changed. keep party session={keepPartySession}. scene={scene.name}", this);

            if (ShouldDisconnectPartySessionAfterRaidReturn(scene.name, previousSceneName, networkManager))
                Disconnect("RaidReturnToLobby");

            previousSceneName = scene.name;
        }

        private static bool ShouldDisconnectPartySessionAfterRaidReturn(
            string currentSceneName,
            string previousSceneName,
            NetworkManager networkManager)
        {
            if (!IsNetworkManagerActive(networkManager))
                return false;

            if (!string.Equals(currentSceneName, "Lobby", StringComparison.Ordinal))
                return false;

            return IsRaidTerminalScene(previousSceneName);
        }

        private static bool IsRaidTerminalScene(string sceneName)
        {
            return string.Equals(sceneName, "Game_Stage_1", StringComparison.Ordinal)
                || string.Equals(sceneName, "Game_Stage_2", StringComparison.Ordinal)
                || string.Equals(sceneName, "RaidResult", StringComparison.Ordinal)
                || string.Equals(sceneName, "HJO_RaidResult", StringComparison.Ordinal)
                || string.Equals(sceneName, "Ending", StringComparison.Ordinal);
        }

        private static async Task<bool> PrepareNetworkManagerForNewSessionAsync(NetworkManager networkManager, string reason)
        {
            if (!IsNetworkManagerActive(networkManager))
                return true;

            ForceShutdownNetworkManager(networkManager, reason);
            bool inactive = await WaitForNetworkManagerInactiveAsync(networkManager);
            if (!inactive)
            {
                Debug.LogWarning($"[PartySession] NetworkManager did not become inactive. reason={reason}");
                return false;
            }

            return true;
        }

        private static async Task DestroyStaleLobbyNetworkObjectsBeforeStartAsync()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, "Lobby", StringComparison.Ordinal))
                return;

            NetworkLobbyState[] lobbyStates = UnityEngine.Object.FindObjectsByType<NetworkLobbyState>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (lobbyStates == null || lobbyStates.Length <= 1)
                return;

            NetworkLobbyState activeSceneState = null;
            for (int i = 0; i < lobbyStates.Length; i++)
            {
                NetworkLobbyState state = lobbyStates[i];
                if (state == null)
                    continue;

                if (state.gameObject.scene.handle == activeScene.handle)
                {
                    activeSceneState = state;
                    break;
                }
            }

            if (activeSceneState == null)
                return;

            int destroyedCount = 0;
            for (int i = 0; i < lobbyStates.Length; i++)
            {
                NetworkLobbyState state = lobbyStates[i];
                if (state == null || state == activeSceneState)
                    continue;

                Debug.LogWarning(
                    $"[PartySession] Destroy stale LobbyNetworkState before starting a new room. " +
                    $"Scene={state.gameObject.scene.name}, Object={state.name}");

                UnityEngine.Object.Destroy(state.gameObject);
                destroyedCount++;
            }

            if (destroyedCount <= 0)
                return;

            for (int i = 0; i < StaleLobbyObjectDestroyWaitFrames; i++)
            {
                await Task.Yield();

                NetworkLobbyState[] remainingStates = UnityEngine.Object.FindObjectsByType<NetworkLobbyState>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                if (remainingStates == null || remainingStates.Length <= 1)
                    return;
            }

            Debug.LogWarning("[PartySession] Stale LobbyNetworkState objects still exist after destroy wait. StartHost/StartClient may fail.");
        }

        private static async Task<bool> WaitForNetworkManagerInactiveAsync(NetworkManager networkManager)
        {
            int pollCount = Math.Max(1, NetworkShutdownTimeoutMilliseconds / NetworkShutdownPollMilliseconds);

            for (int i = 0; i < pollCount; i++)
            {
                if (!IsNetworkManagerActive(networkManager))
                    return true;

                await Task.Delay(NetworkShutdownPollMilliseconds);
            }

            return !IsNetworkManagerActive(networkManager);
        }

        private static void ForceShutdownNetworkManager(NetworkManager networkManager, string reason)
        {
            if (networkManager == null)
                return;

            if (!IsNetworkManagerActive(networkManager))
                return;

            Debug.Log($"[PartySession] Shutdown NetworkManager. reason={reason}");
            networkManager.Shutdown();
        }

        private static bool IsNetworkManagerActive(NetworkManager networkManager)
        {
            return networkManager != null
                && (networkManager.IsListening
                    || networkManager.IsHost
                    || networkManager.IsServer
                    || networkManager.IsClient);
        }
    }
}

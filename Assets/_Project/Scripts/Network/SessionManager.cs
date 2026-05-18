using System;
using System.Collections.Generic;
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
            RegisterNetworkCallbacks();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnregisterNetworkCallbacks();
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

            Debug.Log("[PartySession] CreateParty start. mode=LocalHost", this);
            bool started = networkManager.StartHost();
            LogNetworkState(networkManager, $"StartHost result={started}");
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
            Debug.Log("[PartySession] JoinParty start. mode=LocalClient", this);
            bool started = NetworkManager.Singleton.StartClient();
            LogNetworkState(NetworkManager.Singleton, $"StartClient result={started}");
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

            Debug.Log($"[PartySession] CreateParty start. maxPlayers={maxPlayers}", this);

            if (!await EnsureNetworkFullyStoppedAsync("CreateParty", true))
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
            LogNetworkState(networkManager, $"StartHost result={ok}");
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartHost 실패");
                return string.Empty;
            }

            currentPartyId = joinCode;
            Debug.Log($"[PartySession] Create party. partyId={currentPartyId}, hostClientId={NetworkManager.ServerClientId}", this);
            Debug.Log($"[PartySession] CreateParty success. partyId={currentPartyId}, hostClientId={NetworkManager.ServerClientId}", this);

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

            Debug.Log($"[PartySession] JoinParty start. joinCode={joinCode}", this);

            if (!await EnsureNetworkFullyStoppedAsync("JoinParty", true))
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
            LogNetworkState(networkManager, $"StartClient result={ok}");
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartClient 실패");
                return false;
            }

            currentPartyId = joinCode;

            EventBus.Publish(new RelayJoinedEvent { joinCode = joinCode });
            Debug.Log($"[PartySession] JoinParty success. partyId={currentPartyId}", this);
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
            Debug.Log($"[PartySession] LeaveParty start. reason={reason}", this);
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                ClearLocalPartyCache();
                Debug.Log($"[PartySession] LeaveParty complete. reason={reason}", this);
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
            Debug.Log($"[PartySession] LeaveParty complete. reason={reason}", this);
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

            previousSceneName = scene.name;
        }

        public async Task<bool> EnsureNetworkFullyStoppedAsync(string reason, bool clearPartyCache)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (!IsNetworkManagerActive(networkManager))
            {
                bool alreadyInactive = await WaitForNetworkManagerInactiveAsync(networkManager);
                if (!alreadyInactive)
                {
                    Debug.LogWarning($"[PartySession] NetworkManager shutdown is still in progress. reason={reason}");
                    return false;
                }

                if (clearPartyCache)
                {
                    currentPartyId = string.Empty;
                    ClearLocalPartyCache();
                }

                return true;
            }

            ForceShutdownNetworkManager(networkManager, reason);
            bool inactive = await WaitForNetworkManagerInactiveAsync(networkManager);
            if (!inactive)
            {
                Debug.LogWarning($"[PartySession] NetworkManager did not become inactive. reason={reason}");
                return false;
            }

            if (clearPartyCache)
            {
                currentPartyId = string.Empty;
                ClearLocalPartyCache();
            }

            Debug.Log($"[PartySession] NetworkManager Shutdown complete. reason={reason}", this);
            return true;
        }

        private static async Task DestroyStaleLobbyNetworkObjectsBeforeStartAsync()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, "Lobby", StringComparison.Ordinal))
                return;

            List<GameObject> staleObjects = new();

            AddStaleLobbyNetworkObjects(
                staleObjects,
                activeScene,
                UnityEngine.Object.FindObjectsByType<NetworkLobbyState>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));

            AddStaleLobbyNetworkObjects(
                staleObjects,
                activeScene,
                UnityEngine.Object.FindObjectsByType<LobbyRaidStartController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));

            if (staleObjects.Count <= 0)
                return;

            for (int i = 0; i < staleObjects.Count; i++)
            {
                GameObject staleObject = staleObjects[i];
                if (staleObject == null)
                    continue;

                Debug.LogWarning(
                    $"[PartySession] Destroy stale lobby network object before starting a new room. " +
                    $"Scene={staleObject.scene.name}, Object={staleObject.name}");

                UnityEngine.Object.Destroy(staleObject);
            }

            for (int i = 0; i < StaleLobbyObjectDestroyWaitFrames; i++)
            {
                await Task.Yield();

                if (!HasStaleLobbyNetworkObjects(activeScene))
                    return;
            }

            Debug.LogWarning("[PartySession] Stale lobby network objects still exist after destroy wait. StartHost/StartClient may fail.");
        }

        private static void AddStaleLobbyNetworkObjects<T>(List<GameObject> staleObjects, Scene activeScene, T[] candidates)
            where T : Component
        {
            if (staleObjects == null || candidates == null)
                return;

            for (int i = 0; i < candidates.Length; i++)
            {
                T candidate = candidates[i];
                if (candidate == null || candidate.gameObject == null)
                    continue;

                if (candidate.gameObject.scene.handle == activeScene.handle)
                    continue;

                if (!staleObjects.Contains(candidate.gameObject))
                    staleObjects.Add(candidate.gameObject);
            }
        }

        private static bool HasStaleLobbyNetworkObjects(Scene activeScene)
        {
            List<GameObject> staleObjects = new();
            AddStaleLobbyNetworkObjects(
                staleObjects,
                activeScene,
                UnityEngine.Object.FindObjectsByType<NetworkLobbyState>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
            AddStaleLobbyNetworkObjects(
                staleObjects,
                activeScene,
                UnityEngine.Object.FindObjectsByType<LobbyRaidStartController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
            return staleObjects.Count > 0;
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

            Debug.Log($"[PartySession] NetworkManager Shutdown start. reason={reason}");
            LogNetworkState(networkManager, "Before Shutdown");
            networkManager.Shutdown();
        }

        private static bool IsNetworkManagerActive(NetworkManager networkManager)
        {
            return networkManager != null
                && (networkManager.IsListening
                    || networkManager.ShutdownInProgress
                    || networkManager.IsHost
                    || networkManager.IsServer
                    || networkManager.IsClient);
        }

        private void RegisterNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void UnregisterNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[PartySession] OnClientConnectedCallback clientId={clientId}", this);
            LogNetworkState(NetworkManager.Singleton, "OnClientConnectedCallback");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[PartySession] OnClientDisconnectCallback clientId={clientId}", this);
            LogNetworkState(NetworkManager.Singleton, "OnClientDisconnectCallback");
        }

        private static void LogNetworkState(NetworkManager networkManager, string context)
        {
            if (networkManager == null)
            {
                Debug.Log($"[PartySession] {context}. NetworkManager=null");
                return;
            }

            Debug.Log(
                $"[PartySession] {context}. " +
                $"IsHost={networkManager.IsHost}, IsServer={networkManager.IsServer}, IsClient={networkManager.IsClient}, " +
                $"IsListening={networkManager.IsListening}, LocalClientId={networkManager.LocalClientId}");
        }
    }
}

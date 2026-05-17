using System.Collections;
using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Systems.Raid;

namespace DeadZone.Network
{
    /// <summary>
    /// 레이드 진행 중 남은 시간과 씬 전환 완료 이벤트 발행을 담당한다.
    /// 레이드 시작 요청은 로비의 LobbyRaidStartController에서 처리한다.
    /// </summary>
    public class NetworkGameManager : NetworkBehaviour
    {
        private const float TerminalSessionDisconnectClientDelaySeconds = 0.1f;
        private const float TerminalSessionDisconnectHostDelaySeconds = 0.25f;

        private static readonly string[] TerminalSessionSceneNames =
        {
            "RaidResult",
            "HJO_RaidResult",
            "Ending",
            "Lobby"
        };

        public NetworkVariable<float> RaidTimeRemaining = new(0f);

        [Header("Loading")]
        [SerializeField, Min(0f)] private float minimumLoadingTime = 1.5f;
        [SerializeField, Min(0.1f)] private float localReadyTimeoutSeconds = 5f;
        [SerializeField, Min(0)] private int localReadyDelayFrames = 2;
        [SerializeField, Min(1f)] private float networkLoadingTimeoutSeconds = 8f;

        private readonly Dictionary<ulong, bool> sceneReadyClients = new();
        private readonly List<ulong> terminalSessionDisconnectTargets = new();

        private bool isNetworkLoading;
        private string loadingSceneName;
        private float loadingStartedAt;
        private Coroutine hideLoadingRoutine;
        private Coroutine localReadyRoutine;
        private Coroutine terminalSessionDisconnectRoutine;
        private bool destroyPersistentLobbyObjectOnDespawn;

        public override void OnNetworkSpawn()
        {
            destroyPersistentLobbyObjectOnDespawn =
                TryGetComponent<NetworkLobbyState>(out _)
                || TryGetComponent<LobbyRaidStartController>(out _);

            DontDestroyOnLoad(gameObject);

            Debug.Log($"[NetworkGameManager] OnNetworkSpawn. isServer={IsServer}, isClient={IsClient}, scene={gameObject.scene.name}");

            ServiceLocator.Register(this);
            SceneManager.sceneLoaded += OnLocalSceneLoaded;

            if (IsServer && NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.SceneManager != null)
                    NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;

                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;

            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            SceneManager.sceneLoaded -= OnLocalSceneLoaded;

            if (localReadyRoutine != null)
            {
                StopCoroutine(localReadyRoutine);
                localReadyRoutine = null;
            }

            if (terminalSessionDisconnectRoutine != null)
            {
                StopCoroutine(terminalSessionDisconnectRoutine);
                terminalSessionDisconnectRoutine = null;
            }

            if (hideLoadingRoutine != null)
            {
                StopCoroutine(hideLoadingRoutine);
                hideLoadingRoutine = null;
            }

            ServiceLocator.Unregister<NetworkGameManager>();

            if (destroyPersistentLobbyObjectOnDespawn)
            {
                destroyPersistentLobbyObjectOnDespawn = false;
                Scene activeScene = SceneManager.GetActiveScene();
                if (string.Equals(activeScene.name, "Lobby", System.StringComparison.Ordinal) &&
                    !HasLobbyNetworkObjectInScene(activeScene, gameObject))
                {
                    SceneManager.MoveGameObjectToScene(gameObject, activeScene);
                    Debug.Log("[NetworkGameManager] Keep lobby network object after despawn because active scene is Lobby.", this);
                    return;
                }

                Debug.Log("[NetworkGameManager] Destroy persistent lobby network object after despawn.", this);
                Destroy(gameObject);
            }
        }

        private static bool HasLobbyNetworkObjectInScene(Scene scene, GameObject ignoredObject)
        {
            NetworkLobbyState[] lobbyStates = FindObjectsByType<NetworkLobbyState>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < lobbyStates.Length; i++)
            {
                NetworkLobbyState lobbyState = lobbyStates[i];
                if (lobbyState != null &&
                    lobbyState.gameObject != ignoredObject &&
                    lobbyState.gameObject.scene.handle == scene.handle)
                {
                    return true;
                }
            }

            LobbyRaidStartController[] raidStartControllers = FindObjectsByType<LobbyRaidStartController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < raidStartControllers.Length; i++)
            {
                LobbyRaidStartController raidStartController = raidStartControllers[i];
                if (raidStartController != null &&
                    raidStartController.gameObject != ignoredObject &&
                    raidStartController.gameObject.scene.handle == scene.handle)
                {
                    return true;
                }
            }

            return false;
        }

        public static SceneEventProgressStatus LoadSceneWithLoading(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            NetworkGameManager manager = ServiceLocator.Get<NetworkGameManager>();

            if (manager == null)
                manager = FindFirstObjectByType<NetworkGameManager>();

            if (manager != null && manager.IsSpawned && manager.IsServer)
                return manager.LoadNetworkSceneWithLoading(sceneName, mode);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            {
                Debug.LogWarning($"[Loading] Client cannot start network scene load directly. Waiting for server. scene={sceneName}");
                LoadingScreenService.ShowForNetworkLoadOrFallback(sceneName);
                return SceneEventProgressStatus.SceneEventInProgress;
            }

            if (networkManager == null || networkManager.SceneManager == null)
            {
                Debug.LogWarning($"[Loading] NetworkManager or SceneManager missing. Show fallback only. scene={sceneName}");
                LoadingScreenService.ShowForNetworkLoadOrFallback(sceneName);
                return SceneEventProgressStatus.SceneEventInProgress;
            }

            LoadingScreenService.ShowForNetworkLoadOrFallback(sceneName);
            return networkManager.SceneManager.LoadScene(sceneName, mode);
        }

        public static void RequestReturnToLobbyAfterRaid(string lobbySceneName)
        {
            string targetScene = string.IsNullOrWhiteSpace(lobbySceneName) ? "Lobby" : lobbySceneName;
            NetworkGameManager manager = ServiceLocator.Get<NetworkGameManager>();

            if (manager == null)
                manager = FindFirstObjectByType<NetworkGameManager>();

            NetworkManager networkManager = NetworkManager.Singleton;
            Debug.Log(
                $"[PartySession] ReturnToLobbyAfterRaid start. scene={targetScene}, " +
                $"IsHost={networkManager != null && networkManager.IsHost}, IsServer={networkManager != null && networkManager.IsServer}, " +
                $"IsClient={networkManager != null && networkManager.IsClient}, IsListening={networkManager != null && networkManager.IsListening}");

            RaidResultData.ClearLocalResult();

            if (manager != null && manager.IsSpawned)
            {
                if (manager.IsServer)
                {
                    manager.LoadNetworkSceneWithLoading(targetScene, LoadSceneMode.Single);
                    Debug.Log($"[PartySession] ReturnToLobbyAfterRaid complete. requestedBy=Server, scene={targetScene}");
                    return;
                }

                if (manager.IsClient)
                {
                    LoadingScreenService.ShowForNetworkLoadOrFallback(targetScene);
                    manager.RequestReturnToLobbyAfterRaidServerRpc(targetScene);
                    Debug.Log($"[PartySession] ReturnToLobbyAfterRaid requested. requestedBy=Client, scene={targetScene}");
                    return;
                }
            }

            if (networkManager != null && networkManager.IsListening)
            {
                Debug.LogWarning("[PartySession] ReturnToLobbyAfterRaid failed. NetworkGameManager is not available while network is active.");
                LoadingScreenService.Instance?.HideAsync();
                return;
            }

            LoadingScreenService.LoadSceneOrFallback(targetScene, LoadSceneMode.Single, "Lobby");
            Debug.Log($"[PartySession] ReturnToLobbyAfterRaid complete. mode=Offline, scene={targetScene}");
        }

        public SceneEventProgressStatus LoadNetworkSceneWithLoading(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!IsServer || NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
            {
                Debug.LogWarning($"[Loading] LoadNetworkSceneWithLoading blocked. isServer={IsServer}, scene={sceneName}");
                return SceneEventProgressStatus.SceneEventInProgress;
            }

            BeginNetworkLoading(sceneName);

            SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, mode);

            Debug.Log($"[Loading] LoadScene requested. scene={sceneName}, status={status}, clients={NetworkManager.Singleton.ConnectedClientsIds.Count}");

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[Loading] LoadScene did not start. Cancel network loading. scene={sceneName}, status={status}");
                CancelNetworkLoading();
            }

            return status;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestReturnToLobbyAfterRaidServerRpc(string lobbySceneName, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            Debug.Log($"[PartySession] ReturnToLobbyAfterRaid request received. sender={rpcParams.Receive.SenderClientId}, scene={lobbySceneName}");
            LoadNetworkSceneWithLoading(string.IsNullOrWhiteSpace(lobbySceneName) ? "Lobby" : lobbySceneName, LoadSceneMode.Single);
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (isNetworkLoading)
                TryForceHideLoadingOnTimeout();

            if (RaidTimeRemaining.Value > 0f)
            {
                RaidTimeRemaining.Value = Mathf.Max(0f, RaidTimeRemaining.Value - Time.deltaTime);

                if (RaidTimeRemaining.Value <= 0f)
                    OnRaidTimeExpired();
            }
        }

        private void TryForceHideLoadingOnTimeout()
        {
            float elapsed = Time.realtimeSinceStartup - loadingStartedAt;

            if (elapsed < networkLoadingTimeoutSeconds)
                return;

            string sceneName = loadingSceneName;

            Debug.LogWarning($"[Loading] Timeout. Force hide loading UI. scene={sceneName}, elapsed={elapsed:0.00}");

            HideLoadingClientRpc(sceneName);
            TryDisconnectTerminalPartySession(sceneName);
            ClearNetworkLoadingState();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToHideoutServerRpc()
        {
            RaidLoadoutTransferService.SaveCurrentRaidLoadoutsForConnectedClients();
            RaidTimeRemaining.Value = 0f;
            LoadNetworkSceneWithLoading("Hideout");
        }

        private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;

            EventBus.Publish(new SceneChangedEvent { sceneName = sceneName });

            Debug.Log($"[Loading] NGO load complete. clientId={clientId}, scene={sceneName}, loadingScene={loadingSceneName}, isNetworkLoading={isNetworkLoading}");

            if (!IsServer || !isNetworkLoading)
                return;

            if (!string.Equals(sceneName, loadingSceneName, System.StringComparison.Ordinal))
                return;

            MarkClientReady(clientId, $"NGO load complete: {sceneName}");
        }

        private void BeginNetworkLoading(string sceneName)
        {
            isNetworkLoading = true;
            loadingSceneName = sceneName;
            loadingStartedAt = Time.realtimeSinceStartup;
            sceneReadyClients.Clear();

            if (hideLoadingRoutine != null)
            {
                StopCoroutine(hideLoadingRoutine);
                hideLoadingRoutine = null;
            }

            if (localReadyRoutine != null)
            {
                StopCoroutine(localReadyRoutine);
                localReadyRoutine = null;
            }

            if (NetworkManager.Singleton != null)
            {
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                    sceneReadyClients[clientId] = false;
            }

            Debug.Log($"[Loading] Begin. scene={sceneName}, clients={sceneReadyClients.Count}");

            ShowLoadingClientRpc(sceneName);
        }

        private void CancelNetworkLoading()
        {
            Debug.LogWarning($"[Loading] Cancel network loading. scene={loadingSceneName}");

            LoadingScreenService service = LoadingScreenService.Instance;
            if (service != null)
                _ = service.HideAsync();

            ClearNetworkLoadingState();
        }

        [ClientRpc]
        private void ShowLoadingClientRpc(string sceneName)
        {
            loadingSceneName = sceneName;

            Debug.Log($"[Loading] ShowLoadingClientRpc received. scene={sceneName}, activeScene={SceneManager.GetActiveScene().name}");

            LoadingScreenService.ShowForNetworkLoadOrFallback(sceneName);

            if (SceneManager.GetActiveScene().name == sceneName)
                StartLocalReadyRoutine(sceneName);
        }

        [ClientRpc]
        private void HideLoadingClientRpc(string sceneName)
        {
            Debug.Log($"[Loading] HideLoadingClientRpc received. rpcScene={sceneName}, localLoadingScene={loadingSceneName}");

            if (!string.IsNullOrEmpty(sceneName) &&
                !string.IsNullOrEmpty(loadingSceneName) &&
                !string.Equals(sceneName, loadingSceneName, System.StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Loading] Hide ignored by scene mismatch. rpcScene={sceneName}, localScene={loadingSceneName}");
                return;
            }

            loadingSceneName = string.Empty;

            LoadingScreenService service = LoadingScreenService.Instance;

            if (service != null)
            {
                Debug.Log("[Loading] Call LoadingScreenService.HideAsync");
                _ = service.HideAsync();
            }
            else
            {
                Debug.LogWarning("[Loading] LoadingScreenService.Instance is null.");
            }
        }

        private void OnLocalSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[Loading] Local scene loaded. scene={scene.name}, loadingScene={loadingSceneName}, isServer={IsServer}, isClient={IsClient}, isNetworkLoading={isNetworkLoading}");

            if (string.IsNullOrEmpty(loadingSceneName))
                return;

            if (!string.Equals(scene.name, loadingSceneName, System.StringComparison.Ordinal))
                return;

            if (IsServer && isNetworkLoading && NetworkManager.Singleton != null)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;
                MarkClientReady(localClientId, $"Local sceneLoaded: {scene.name}");

                // 싱글/호스트 1인 테스트에서는 ready 동기화 대기 없이 직접 종료한다.
                if (NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
                {
                    Debug.Log($"[Loading] Single/host local load complete. Hide loading directly. scene={scene.name}");

                    StartHideLoadingRoutineIfNeeded();
                    return;
                }
            }

            StartLocalReadyRoutine(scene.name);
        }

        private void StartLocalReadyRoutine(string sceneName)
        {
            if (!IsClient)
                return;

            if (localReadyRoutine != null)
                StopCoroutine(localReadyRoutine);

            localReadyRoutine = StartCoroutine(NotifySceneReadyWhenLocalInitialized(sceneName));
        }

        private IEnumerator NotifySceneReadyWhenLocalInitialized(string sceneName)
        {
            for (int i = 0; i < localReadyDelayFrames; i++)
                yield return null;

            float startTime = Time.realtimeSinceStartup;

            while (!IsLocalSceneInitialized() &&
                   Time.realtimeSinceStartup - startTime < localReadyTimeoutSeconds)
            {
                yield return null;
            }

            Debug.Log($"[Loading] Local ready routine completed. scene={sceneName}, initialized={IsLocalSceneInitialized()}");

            NotifySceneReadyServerRpc(sceneName);
            localReadyRoutine = null;
        }

        private bool IsLocalSceneInitialized()
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening || !networkManager.IsClient)
                return true;

            if (networkManager.LocalClient == null)
                return true;

            return networkManager.LocalClient.PlayerObject != null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void NotifySceneReadyServerRpc(string sceneName, RpcParams rpcParams = default)
        {
            if (!IsServer || !isNetworkLoading)
            {
                Debug.Log($"[Loading] Ready ignored. isServer={IsServer}, isNetworkLoading={isNetworkLoading}, scene={sceneName}");
                return;
            }

            if (!string.Equals(sceneName, loadingSceneName, System.StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Loading] Ready ignored by scene mismatch. readyScene={sceneName}, loadingScene={loadingSceneName}");
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            MarkClientReady(senderClientId, $"Client ready RPC: {sceneName}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer || !isNetworkLoading)
                return;

            sceneReadyClients.Remove(clientId);
            Debug.Log($"[Loading] Client disconnected during loading. clientId={clientId}");

            TryHideLoadingWhenReady();
        }

        private void MarkClientReady(ulong clientId, string reason)
        {
            if (!IsServer || !isNetworkLoading)
                return;

            if (!sceneReadyClients.ContainsKey(clientId))
                sceneReadyClients[clientId] = false;

            sceneReadyClients[clientId] = true;

            Debug.Log($"[Loading] Client marked ready. clientId={clientId}, scene={loadingSceneName}, reason={reason}");

            TryHideLoadingWhenReady();
        }

        private void TryHideLoadingWhenReady()
        {
            if (!IsServer || !isNetworkLoading)
                return;

            if (NetworkManager.Singleton == null)
                return;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                bool ready = sceneReadyClients.TryGetValue(clientId, out bool value) && value;

                Debug.Log($"[Loading] Ready check. clientId={clientId}, ready={ready}, scene={loadingSceneName}");

                if (!ready)
                    return;
            }

            StartHideLoadingRoutineIfNeeded();
        }

        private void StartHideLoadingRoutineIfNeeded()
        {
            if (hideLoadingRoutine != null)
                return;

            Debug.Log($"[Loading] All clients ready. Start hide routine. scene={loadingSceneName}");
            hideLoadingRoutine = StartCoroutine(HideLoadingAfterMinimumTime());
        }

        private IEnumerator HideLoadingAfterMinimumTime()
        {
            float remaining = minimumLoadingTime - (Time.realtimeSinceStartup - loadingStartedAt);

            if (remaining > 0f)
                yield return new WaitForSecondsRealtime(remaining);

            string sceneName = loadingSceneName;

            Debug.Log($"[Loading] Hide loading. scene={sceneName}");

            HideLoadingClientRpc(sceneName);
            TryDisconnectTerminalPartySession(sceneName);
            ClearNetworkLoadingState();
        }

        private void ClearNetworkLoadingState()
        {
            isNetworkLoading = false;
            loadingSceneName = string.Empty;
            sceneReadyClients.Clear();

            if (hideLoadingRoutine != null)
            {
                StopCoroutine(hideLoadingRoutine);
                hideLoadingRoutine = null;
            }

            if (localReadyRoutine != null)
            {
                StopCoroutine(localReadyRoutine);
                localReadyRoutine = null;
            }
        }

        private void TryDisconnectTerminalPartySession(string sceneName)
        {
            Debug.Log($"[PartySession] Keep party session after network scene load. scene={sceneName}");
        }

        private IEnumerator DisconnectTerminalPartySession(string sceneName)
        {
            yield return new WaitForSecondsRealtime(TerminalSessionDisconnectClientDelaySeconds);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                terminalSessionDisconnectRoutine = null;
                yield break;
            }

            terminalSessionDisconnectTargets.Clear();

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId == networkManager.LocalClientId)
                    continue;

                terminalSessionDisconnectTargets.Add(clientId);
            }

            if (terminalSessionDisconnectTargets.Count > 0)
            {
                DisconnectTerminalPartySessionClientRpc(
                    sceneName,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = terminalSessionDisconnectTargets.ToArray()
                        }
                    });
            }

            yield return new WaitForSecondsRealtime(TerminalSessionDisconnectHostDelaySeconds);

            Debug.Log($"[PartySession] Terminal scene reached without leaving party. scene={sceneName}");
            terminalSessionDisconnectRoutine = null;
        }

        [ClientRpc]
        private void DisconnectTerminalPartySessionClientRpc(
            string sceneName,
            ClientRpcParams clientRpcParams = default)
        {
            Debug.Log($"[PartySession] Terminal scene reached on client without leaving party. scene={sceneName}");
        }

        private static bool IsTerminalSessionScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            for (int i = 0; i < TerminalSessionSceneNames.Length; i++)
            {
                if (string.Equals(sceneName, TerminalSessionSceneNames[i], System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void OnRaidTimeExpired()
        {
            Debug.Log("[NetworkGameManager] Raid time expired -> return to hideout");
            ReturnToHideoutServerRpc();
        }
    }
}

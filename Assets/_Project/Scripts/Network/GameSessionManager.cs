using System.Collections.Generic;
using System.Text;
using DeadZone.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Network
{
    /// <summary>
    /// 게임 씬 이동 이후 expected client들의 로드 완료 상태를 서버 기준으로 추적한다.
    /// Player Spawn은 담당하지 않으며, 후속 Spawn 시스템이 사용할 수 있는 완료 상태만 제공한다.
    /// </summary>
    public sealed class GameSessionManager : NetworkBehaviour
    {
        private static GameSessionManager instance;

        // 클라이언트는 현재 로딩 진행률만 읽는다.
        // expected / loaded clientId 목록 자체는 서버 내부 상태로만 관리한다.
        private readonly NetworkVariable<int> expectedCount = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> loadedCount = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> isAllClientsLoaded = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("==== 디버그 ====")]
        [Tooltip("로드 추적 시작, 개별 Client 로드 완료, 전원 로드 완료 로그를 출력합니다.")]
        [SerializeField] private bool logDebug;

        // 서버 내부 추적용 목록이다.
        // HashSet을 사용해 같은 clientId의 중복 LoadComplete 처리를 방어한다.
        private readonly HashSet<ulong> expectedClientIds = new();
        private readonly HashSet<ulong> loadedClientIds = new();

        private string targetSceneName;
        private bool isTracking;
        private bool isSceneEventSubscribed;
        private bool isRegisteredToServiceLocator;

        public int ExpectedCountValue => expectedCount.Value;
        public int LoadedCountValue => loadedCount.Value;
        public bool IsAllClientsLoaded => isAllClientsLoaded.Value;

        public NetworkVariable<int> ExpectedCount => expectedCount;
        public NetworkVariable<int> LoadedCount => loadedCount;
        public NetworkVariable<bool> AllClientsLoadedState => isAllClientsLoaded;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            RegisterToServiceLocator();
            SubscribeSceneEventsIfServer();

            if (IsServer)
                ResetState();

            LogDebug($"OnNetworkSpawn. IsServer={IsServer}, IsClient={IsClient}, IsSpawned={IsSpawned}");
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeSceneEvents();
            ClearLocalTrackingCollections();
            UnregisterFromServiceLocator();

            LogDebug("OnNetworkDespawn.");
        }

        private void OnDestroy()
        {
            UnsubscribeSceneEvents();
            UnregisterFromServiceLocator();

            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// 씬 로드 시작 직전에 호출한다.
        /// 이 메서드가 먼저 실행되어야 이후 OnLoadComplete 이벤트를 놓치지 않는다.
        /// </summary>
        public bool BeginLoadTracking(string sceneName, IReadOnlyList<ulong> clientIds, out string reason)
        {
            if (!IsServer)
            {
                reason = "서버에서만 로드 완료 추적을 시작할 수 있습니다.";
                return false;
            }

            if (!IsSpawned)
            {
                reason = "GameSessionManager가 아직 Network Spawn되지 않았습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reason = "로드 완료를 추적할 대상 씬 이름이 비어 있습니다.";
                return false;
            }

            if (clientIds == null || clientIds.Count == 0)
            {
                reason = "로드 완료를 기다릴 clientId 목록이 비어 있습니다.";
                return false;
            }

            SubscribeSceneEventsIfServer();

            expectedClientIds.Clear();
            loadedClientIds.Clear();

            for (int i = 0; i < clientIds.Count; i++)
                expectedClientIds.Add(clientIds[i]);

            if (expectedClientIds.Count == 0)
            {
                reason = "유효한 expected clientId가 없습니다.";
                return false;
            }

            targetSceneName = sceneName;
            isTracking = true;

            expectedCount.Value = expectedClientIds.Count;
            loadedCount.Value = 0;
            isAllClientsLoaded.Value = false;

            reason = $"로드 완료 추적 시작. Scene={targetSceneName}, Expected={expectedClientIds.Count}";
            LogDebug($"{reason}, ClientIds={FormatClientIds(expectedClientIds)}");
            return true;
        }

        /// <summary>
        /// 씬 로드 요청 실패처럼 추적을 유지하면 안 되는 상황에서 호출한다.
        /// </summary>
        public void CancelLoadTracking(string reason)
        {
            if (!IsServer) return;

            ClearLocalTrackingCollections();

            targetSceneName = string.Empty;
            isTracking = false;

            expectedCount.Value = 0;
            loadedCount.Value = 0;
            isAllClientsLoaded.Value = false;

            LogDebug($"로드 완료 추적 취소: {reason}");
        }

        private void HandleLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
        {
            if (!IsServer) return;
            if (!isTracking) return;

            if (loadSceneMode != LoadSceneMode.Single)
            {
                LogDebug($"LoadSceneMode가 Single이 아니므로 무시합니다. ClientId={clientId}, Mode={loadSceneMode}");
                return;
            }

            if (sceneName != targetSceneName)
            {
                LogDebug($"대상 씬과 다른 LoadComplete를 무시합니다. ClientId={clientId}, Scene={sceneName}, Target={targetSceneName}");
                return;
            }

            if (!expectedClientIds.Contains(clientId))
            {
                LogDebug($"expected 목록에 없는 Client LoadComplete를 무시합니다. ClientId={clientId}");
                return;
            }

            if (!loadedClientIds.Add(clientId))
            {
                LogDebug($"이미 로드 완료 처리된 Client입니다. ClientId={clientId}");
                return;
            }

            loadedCount.Value = loadedClientIds.Count;

            LogDebug($"Client 로드 완료. ClientId={clientId}, Loaded={loadedCount.Value}/{expectedCount.Value}");

            if (loadedClientIds.Count < expectedClientIds.Count)
                return;

            isAllClientsLoaded.Value = true;
            isTracking = false;

            LogDebug($"모든 expected Client가 로드 완료했습니다. Scene={targetSceneName}");
        }

        private void SubscribeSceneEventsIfServer()
        {
            if (!IsServer) return;
            if (isSceneEventSubscribed) return;

            Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;

            if (networkManager == null || networkManager.SceneManager == null)
            {
                LogDebug("NetworkSceneManager를 찾을 수 없어 OnLoadComplete를 구독하지 못했습니다.");
                return;
            }

            networkManager.SceneManager.OnLoadComplete += HandleLoadComplete;
            isSceneEventSubscribed = true;

            LogDebug("NetworkSceneManager.OnLoadComplete 구독 완료.");
        }

        private void UnsubscribeSceneEvents()
        {
            if (!isSceneEventSubscribed) return;

            Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;

            if (networkManager != null && networkManager.SceneManager != null)
                networkManager.SceneManager.OnLoadComplete -= HandleLoadComplete;

            isSceneEventSubscribed = false;
        }

        private void RegisterToServiceLocator()
        {
            if (isRegisteredToServiceLocator) return;

            ServiceLocator.Register(this);
            isRegisteredToServiceLocator = true;

            LogDebug("ServiceLocator 등록 완료.");
        }

        private void UnregisterFromServiceLocator()
        {
            if (!isRegisteredToServiceLocator) return;

            ServiceLocator.Unregister<GameSessionManager>();
            isRegisteredToServiceLocator = false;
        }

        private void ResetState()
        {
            ClearLocalTrackingCollections();

            targetSceneName = string.Empty;
            isTracking = false;

            expectedCount.Value = 0;
            loadedCount.Value = 0;
            isAllClientsLoaded.Value = false;
        }

        private void ClearLocalTrackingCollections()
        {
            expectedClientIds.Clear();
            loadedClientIds.Clear();
        }

        private string FormatClientIds(IEnumerable<ulong> clientIds)
        {
            StringBuilder builder = new();

            foreach (ulong clientId in clientIds)
            {
                if (builder.Length > 0)
                    builder.Append(", ");

                builder.Append(clientId);
            }

            return builder.ToString();
        }

        private void LogDebug(string message)
        {
            if (!logDebug) return;
            Debug.Log($"[GameSessionManager] {message}", this);
        }
    }
}
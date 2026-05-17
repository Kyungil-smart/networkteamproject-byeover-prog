using System;
using System.Collections;
using System.Collections.Generic;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Raid;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Network
{
    /// <summary>
    /// 출격 씬으로 이동한 뒤 expected client들이 모두 로드를 완료했는지 서버에서 추적한다.
    /// 클라이언트는 인원 수와 완료 여부만 읽고, 실제 clientId 목록은 서버 내부에서만 관리한다.
    /// </summary>
    public sealed class GameSessionManager : NetworkBehaviour
    {
        private static GameSessionManager instance;

        [Header("==== 디버그 ====")]
        [Tooltip("로드 추적 시작, 개별 Client LoadComplete 수신, 전원 로드 완료 로그를 출력합니다.")]
        [SerializeField] private bool logDebug;

        [Header("==== 레이드 결과 ====")]
        [SerializeField] private string raidResultSceneName = "RaidResult";

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

        private readonly HashSet<ulong> expectedClientIds = new();
        private readonly HashSet<ulong> loadedClientIds = new();

        // SpawnPoint 배정은 출격 직전 로비에서 수집한 clientId 순서를 기준으로 한다.
        // HashSet은 포함 여부 검증용, List는 순서 보존용으로 역할을 분리한다.
        private readonly List<ulong> expectedClientIdOrder = new();

        private string targetSceneName;
        private bool isTracking;
        private bool subscribedToSceneEvents;
        private bool isRegisteredToServiceLocator;
        private bool isRaidActive;
        private bool isRaidResultFinalized;
        private float raidStartedAt;

        public NetworkVariable<int> ExpectedCount => expectedCount;
        public NetworkVariable<int> LoadedCount => loadedCount;
        public NetworkVariable<bool> AllClientsLoadedState => isAllClientsLoaded;

        public bool IsAllClientsLoaded => isAllClientsLoaded.Value;
        public int ExpectedClientCount => expectedCount.Value;
        public int LoadedClientCount => loadedCount.Value;

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

            if (IsServer)
            {
                ResetTrackingState();
                SubscribeSceneEvents();
                SubscribeRaidEvents();
            }

            LogDebug($"네트워크 Spawn 완료. 서버={IsServer}, 클라이언트={IsClient}, Spawn상태={IsSpawned}");
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                UnsubscribeSceneEvents();
                UnsubscribeRaidEvents();
            }

            UnregisterFromServiceLocator();

            LogDebug("네트워크 Despawn 완료.");
        }

        public override void OnDestroy()
        {
            if (instance == this)
                instance = null;

            UnsubscribeSceneEvents();
            UnsubscribeRaidEvents();
            UnregisterFromServiceLocator();
            base.OnDestroy();
        }

        /// <summary>
        /// 서버가 출격 씬 로드를 시작하기 직전에 호출한다.
        /// 전달받은 clientId 순서는 이후 SpawnPoint 배정 순서로 사용된다.
        /// </summary>
        public bool BeginLoadTracking(string sceneName, IReadOnlyList<ulong> clientIds, out string reason)
        {
            reason = string.Empty;

            if (!IsServer)
            {
                reason = "서버에서만 로드 완료 추적을 시작할 수 있습니다.";
                LogDebug(reason);
                return false;
            }

            if (!IsSpawned)
            {
                reason = "GameSessionManager가 아직 Network Spawn되지 않았습니다.";
                LogDebug(reason);
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reason = "대상 씬 이름이 비어 있습니다.";
                LogDebug(reason);
                return false;
            }

            if (clientIds == null || clientIds.Count == 0)
            {
                reason = "로드 완료를 기다릴 clientId 목록이 비어 있습니다.";
                LogDebug(reason);
                return false;
            }

            ClearLocalTrackingCollections();

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];

                if (expectedClientIds.Add(clientId))
                    expectedClientIdOrder.Add(clientId);
            }

            if (expectedClientIds.Count == 0)
            {
                reason = "중복 제거 후 로드 완료를 기다릴 clientId 목록이 비어 있습니다.";
                LogDebug(reason);
                return false;
            }

            targetSceneName = sceneName;
            isTracking = true;

            expectedCount.Value = expectedClientIds.Count;
            loadedCount.Value = 0;
            isAllClientsLoaded.Value = false;
            TryBeginRaidResultTracking(sceneName);

            reason = "로드 완료 추적을 시작했습니다.";

            LogDebug(
                $"로드 완료 추적 시작. 대상씬={targetSceneName}, " +
                $"기대인원={expectedCount.Value}, 대상ClientIds={FormatClientIds(expectedClientIdOrder)}");

            return true;
        }

        /// <summary>
        /// 씬 로드 요청이 실패했거나 출격 흐름을 되돌려야 할 때 서버 상태를 초기화한다.
        /// </summary>
        public void CancelLoadTracking(string reason)
        {
            if (!IsServer)
                return;

            LogDebug($"로드 완료 추적 취소. 사유={reason}");
            ResetTrackingState();
        }

        /// <summary>
        /// PlayerSpawnManager가 Spawn 대상 순서를 읽을 때 사용한다.
        /// 내부 List를 직접 노출하지 않기 위해 호출자가 넘긴 버퍼에 복사한다.
        /// </summary>
        public void GetExpectedClientIdOrder(List<ulong> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            for (int i = 0; i < expectedClientIdOrder.Count; i++)
                destination.Add(expectedClientIdOrder[i]);
        }

        public void CaptureRaidStartInventorySnapshot(ulong clientId, GameObject playerObject)
        {
            if (!IsServer || !isRaidActive || isRaidResultFinalized)
                return;

            RaidResultData.CaptureStartInventorySnapshot(clientId, playerObject);
        }

        public bool TryCompleteRaidByExtraction(IReadOnlyList<ulong> extractedClientIds)
        {
            if (!IsServer || !isRaidActive || isRaidResultFinalized)
                return false;

            if (extractedClientIds == null || extractedClientIds.Count == 0)
                return false;

            RaidResultData.MarkPlayersExtracted(extractedClientIds, GetRaidElapsedSeconds());
            FinalizeRaidResultsAndLoadScene("Extraction");
            return true;
        }

        private void HandleLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
        {
            if (!IsServer)
                return;

            if (!isTracking)
                return;

            if (loadSceneMode != LoadSceneMode.Single)
                return;

            if (!string.Equals(sceneName, targetSceneName, StringComparison.Ordinal))
                return;

            if (!expectedClientIds.Contains(clientId))
            {
                LogDebug($"로드 완료 이벤트 무시. 출격 대상이 아닌 ClientId입니다. ClientId={clientId}");
                return;
            }

            if (!loadedClientIds.Add(clientId))
            {
                LogDebug($"중복 로드 완료 이벤트 무시. ClientId={clientId}");
                return;
            }

            loadedCount.Value = loadedClientIds.Count;

            LogDebug(
                $"Client 로드 완료 집계. ClientId={clientId}, " +
                $"로드완료={loadedCount.Value}/{expectedCount.Value}");

            if (loadedClientIds.Count < expectedClientIds.Count)
                return;

            isAllClientsLoaded.Value = true;
            isTracking = false;

            LogDebug(
                $"모든 출격 대상 Client 로드 완료. " +
                $"로드완료={loadedCount.Value}/{expectedCount.Value}");
        }

        private void ResetTrackingState()
        {
            ClearLocalTrackingCollections();

            targetSceneName = string.Empty;
            isTracking = false;

            if (!IsServer || !IsSpawned)
                return;

            expectedCount.Value = 0;
            loadedCount.Value = 0;
            isAllClientsLoaded.Value = false;

            LogDebug("로드 추적 상태 초기화 완료.");
        }

        private void ClearLocalTrackingCollections()
        {
            expectedClientIds.Clear();
            loadedClientIds.Clear();
            expectedClientIdOrder.Clear();
        }

        private void TryBeginRaidResultTracking(string sceneName)
        {
            if (!IsServer)
                return;

            if (!IsRaidScene(sceneName))
            {
                isRaidActive = false;
                isRaidResultFinalized = false;
                return;
            }

            isRaidActive = true;
            isRaidResultFinalized = false;
            raidStartedAt = Time.time;

            RaidResultData.BeginRaid(sceneName, expectedClientIdOrder);
            LogDebug($"레이드 결과 추적 시작. Scene={sceneName}, Clients={FormatClientIds(expectedClientIdOrder)}");
        }

        private void SubscribeRaidEvents()
        {
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void UnsubscribeRaidEvents()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            if (!IsServer || !isRaidActive || isRaidResultFinalized)
                return;

            if (e.attackerClientId == DamageSystem.AI_SHOOTER_ID)
                return;

            RaidResultData.AddKillForPlayer(e.attackerClientId);
        }

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            if (!IsServer || !isRaidActive || isRaidResultFinalized)
                return;

            RaidResultData.MarkPlayerDead(e.victimClientId, GetRaidElapsedSeconds());

            if (AreAllExpectedPlayersDead())
                FinalizeRaidResultsAndLoadScene("AllDead");
        }

        private bool AreAllExpectedPlayersDead()
        {
            if (!IsServer || expectedClientIdOrder.Count == 0)
                return false;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            for (int i = 0; i < expectedClientIdOrder.Count; i++)
            {
                ulong clientId = expectedClientIdOrder[i];
                if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                    client.PlayerObject == null)
                {
                    continue;
                }

                PlayerHealthSystem health = client.PlayerObject.GetComponent<PlayerHealthSystem>();
                if (health == null || !health.IsDead)
                    return false;
            }

            return true;
        }

        private void FinalizeRaidResultsAndLoadScene(string reason)
        {
            if (!IsServer || isRaidResultFinalized)
                return;

            isRaidResultFinalized = true;
            SendRaidResultsToClients();
            StartCoroutine(LoadRaidResultSceneNextFrame(reason));
        }

        private void SendRaidResultsToClients()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return;

            for (int i = 0; i < expectedClientIdOrder.Count; i++)
            {
                ulong clientId = expectedClientIdOrder[i];
                GameObject playerObject = null;

                if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                    client.PlayerObject != null)
                {
                    playerObject = client.PlayerObject.gameObject;
                }

                RaidPlayerResult result = RaidResultData.BuildResultForPlayer(clientId, targetSceneName, playerObject);
                string json = RaidResultData.ToJson(result);

                if (clientId == NetworkManager.LocalClientId)
                    RaidResultData.SetLocalResult(result);

                ReceiveRaidResultClientRpc(
                    json,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new[] { clientId }
                        }
                    });
            }
        }

        [ClientRpc]
        private void ReceiveRaidResultClientRpc(string resultJson, ClientRpcParams rpcParams = default)
        {
            RaidResultData.SetLocalResultFromJson(resultJson);
        }

        private void LoadRaidResultScene(string reason)
        {
            if (string.IsNullOrWhiteSpace(raidResultSceneName))
            {
                Debug.LogWarning("[GameSessionManager] RaidResult 씬 이름이 비어 있어 결과 씬으로 이동할 수 없습니다.", this);
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.SceneManager == null)
            {
                Debug.LogWarning("[GameSessionManager] NetworkSceneManager를 찾을 수 없어 RaidResult 씬 이동을 시작하지 못했습니다.", this);
                return;
            }

            CancelLoadTracking($"RaidResult 전환: {reason}");

            SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(raidResultSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[GameSessionManager] RaidResult 씬 로드 요청 실패. Scene={raidResultSceneName}, Status={status}", this);
                return;
            }

            isRaidActive = false;
            LogDebug($"RaidResult 씬 로드 요청. Scene={raidResultSceneName}, Reason={reason}");
        }

        private IEnumerator LoadRaidResultSceneNextFrame(string reason)
        {
            yield return null;
            LoadRaidResultScene(reason);
        }

        private float GetRaidElapsedSeconds()
        {
            return Mathf.Max(0f, Time.time - raidStartedAt);
        }

        private static bool IsRaidScene(string sceneName)
        {
            return string.Equals(sceneName, "Game_Stage_1", StringComparison.Ordinal) ||
                   string.Equals(sceneName, "Game_Stage_2", StringComparison.Ordinal);
        }

        private void SubscribeSceneEvents()
        {
            if (subscribedToSceneEvents)
                return;

            if (NetworkManager == null || NetworkManager.SceneManager == null)
            {
                LogDebug("NetworkSceneManager를 찾을 수 없어 로드 완료 이벤트를 구독하지 못했습니다.");
                return;
            }

            NetworkManager.SceneManager.OnLoadComplete += HandleLoadComplete;
            subscribedToSceneEvents = true;

            LogDebug("NetworkSceneManager 로드 완료 이벤트 구독 완료.");
        }

        private void UnsubscribeSceneEvents()
        {
            if (!subscribedToSceneEvents)
                return;

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager != null && networkManager.SceneManager != null)
                networkManager.SceneManager.OnLoadComplete -= HandleLoadComplete;

            subscribedToSceneEvents = false;

            LogDebug("NetworkSceneManager 로드 완료 이벤트 구독 해제 완료.");
        }

        private void RegisterToServiceLocator()
        {
            if (isRegisteredToServiceLocator)
                return;

            ServiceLocator.Register(this);
            isRegisteredToServiceLocator = true;

            LogDebug("ServiceLocator 등록 완료.");
        }

        private void UnregisterFromServiceLocator()
        {
            if (!isRegisteredToServiceLocator)
                return;

            ServiceLocator.Unregister(this);
            isRegisteredToServiceLocator = false;

            LogDebug("ServiceLocator 등록 해제 완료.");
        }

        private string FormatClientIds(IReadOnlyList<ulong> clientIds)
        {
            if (clientIds == null || clientIds.Count == 0)
                return "(비어 있음)";

            return string.Join(", ", clientIds);
        }

        private void LogDebug(string message)
        {
            if (!logDebug)
                return;

            Debug.Log($"[게임 세션] {message}", this);
        }
    }
}

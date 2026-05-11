using System.Collections.Generic;
using DeadZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Network
{
    /// <summary>
    /// 게임 씬 로드 완료 이후 서버가 expected client들에게 PlayerObject를 수동 Spawn한다.
    /// 카메라, 입력, 이동 동기화는 담당하지 않는다.
    /// </summary>
    public sealed class PlayerSpawnManager : MonoBehaviour
    {
        [Header("==== Spawn 대상 ====")]
        [Tooltip("서버가 수동 Spawn할 Player Prefab입니다. " +
                 "NetworkObject가 있어야 하며 NetworkPrefabs 목록에 등록되어 있어야 합니다.")]
        [SerializeField] private GameObject playerPrefab;

        [Tooltip("SpawnPoint들을 자식으로 가진 Root Transform입니다. " +
                 "자식 순서가 expected clientId 순서와 매칭됩니다.")]
        [SerializeField] private Transform spawnPointsRoot;

        [Header("==== 디버그 ====")]
        [Tooltip("Spawn 준비, 조건 확인, 개별 Spawn 결과 로그를 출력합니다.")]
        [SerializeField] private bool logDebug = true;

        private readonly List<ulong> spawnTargetBuffer = new();

        private GameSessionManager gameSessionManager;
        private bool hasSpawnedPlayers;
        private bool subscribedToLoadState;

        private void Start()
        {
            if (!TryGetNetworkManager(out NetworkManager networkManager))
                return;

            if (!networkManager.IsServer)
                return;

            TryBindGameSessionManager();
        }

        private void OnDestroy()
        {
            UnsubscribeLoadState();
        }

        private void HandleAllClientsLoadedChanged(bool previousValue, bool currentValue)
        {
            LogDebug($"전원 로드 완료 상태 변경 수신. 이전값={previousValue}, 현재값={currentValue}");

            if (!currentValue)
                return;

            TrySpawnPlayersOnce();
        }

        private void TryBindGameSessionManager()
        {
            if (gameSessionManager != null)
                return;

            if (ServiceLocator.TryGet(out GameSessionManager registeredManager))
                gameSessionManager = registeredManager;

            if (gameSessionManager == null)
                gameSessionManager = FindFirstObjectByType<GameSessionManager>();

            if (gameSessionManager == null)
            {
                Debug.LogWarning(
                    "[플레이어 스폰] GameSessionManager를 찾을 수 없어 Player Spawn 대기를 시작하지 못했습니다.", this);
                return;
            }

            SubscribeLoadState();

            LogDebug($"GameSessionManager 연결 완료. 전원로드완료={gameSessionManager.IsAllClientsLoaded}");

            if (gameSessionManager.IsAllClientsLoaded)
                TrySpawnPlayersOnce();
        }

        private void SubscribeLoadState()
        {
            if (subscribedToLoadState)
                return;

            if (gameSessionManager == null)
                return;

            gameSessionManager.AllClientsLoadedState.OnValueChanged += HandleAllClientsLoadedChanged;
            subscribedToLoadState = true;

            LogDebug("전원 로드 완료 상태 변경 이벤트 구독 완료.");
        }

        private void UnsubscribeLoadState()
        {
            if (!subscribedToLoadState)
                return;

            if (gameSessionManager != null)
                gameSessionManager.AllClientsLoadedState.OnValueChanged -= HandleAllClientsLoadedChanged;

            subscribedToLoadState = false;

            LogDebug("전원 로드 완료 상태 변경 이벤트 구독 해제 완료.");
        }

        private void TrySpawnPlayersOnce()
        {
            if (hasSpawnedPlayers)
            {
                LogDebug("이미 PlayerObject Spawn이 완료되어 추가 Spawn을 건너뜁니다.");
                return;
            }

            if (!TryGetNetworkManager(out NetworkManager networkManager))
                return;

            if (!networkManager.IsServer)
                return;

            if (!ValidateManualSpawnMode(networkManager))
                return;

            if (!TryCollectSpawnTargets(out int targetCount))
                return;

            LogDebug($"PlayerObject Spawn 시도 시작. 대상인원={targetCount}");

            if (!ValidateSpawnSetup(targetCount))
                return;

            if (!ValidateConnectedClients(networkManager))
                return;

            NetworkObject prefabNetworkObject = playerPrefab.GetComponent<NetworkObject>();

            if (prefabNetworkObject == null)
            {
                Debug.LogError("[플레이어 스폰] Player Prefab에 NetworkObject가 없어 Spawn을 중단합니다.", this);
                return;
            }

            for (int i = 0; i < spawnTargetBuffer.Count; i++)
            {
                ulong clientId = spawnTargetBuffer[i];

                if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                {
                    Debug.LogWarning(
                        $"[플레이어 스폰] 연결되지 않은 ClientId라 Spawn을 건너뜁니다. ClientId={clientId}", this);
                    continue;
                }

                if (client.PlayerObject != null)
                {
                    LogDebug($"이미 PlayerObject가 있어 Spawn을 건너뜁니다. ClientId={clientId}");
                    continue;
                }

                Transform spawnPoint = spawnPointsRoot.GetChild(i);
                SpawnPlayerForClient(clientId, spawnPoint);
            }

            hasSpawnedPlayers = true;

            LogDebug("PlayerObject Spawn 처리 완료.");
        }

        private bool TryCollectSpawnTargets(out int targetCount)
        {
            targetCount = 0;

            if (gameSessionManager == null)
            {
                Debug.LogWarning("[플레이어 스폰] GameSessionManager가 없어 Spawn 대상 clientId를 확인할 수 없습니다.", this);
                return false;
            }

            spawnTargetBuffer.Clear();
            gameSessionManager.GetExpectedClientIdOrder(spawnTargetBuffer);

            targetCount = spawnTargetBuffer.Count;

            if (targetCount <= 0)
            {
                Debug.LogWarning("[플레이어 스폰] Spawn 대상 clientId 목록이 비어 있습니다.", this);
                return false;
            }

            LogDebug($"Spawn 대상 clientId 수집 완료. 대상ClientIds={FormatClientIds(spawnTargetBuffer)}");

            return true;
        }

        private bool ValidateSpawnSetup(int targetCount)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[플레이어 스폰] Player Prefab이 연결되어 있지 않습니다.", this);
                return false;
            }

            if (spawnPointsRoot == null)
            {
                Debug.LogError("[플레이어 스폰] SpawnPoints Root가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (targetCount > spawnPointsRoot.childCount)
            {
                Debug.LogError(
                    $"[플레이어 스폰] SpawnPoint 수가 부족합니다. 대상인원={targetCount}, SpawnPoint수={spawnPointsRoot.childCount}",
                    this);
                return false;
            }

            return true;
        }

        private bool ValidateConnectedClients(NetworkManager networkManager)
        {
            for (int i = 0; i < spawnTargetBuffer.Count; i++)
            {
                ulong clientId = spawnTargetBuffer[i];

                if (!networkManager.ConnectedClients.ContainsKey(clientId))
                {
                    Debug.LogWarning(
                        $"[플레이어 스폰] Spawn 대상 Client가 현재 연결되어 있지 않습니다. ClientId={clientId}", this);
                    return false;
                }
            }

            return true;
        }

        private bool ValidateManualSpawnMode(NetworkManager networkManager)
        {
            if (networkManager.NetworkConfig == null)
            {
                Debug.LogWarning("[플레이어 스폰] NetworkConfig를 확인할 수 없어 수동 Spawn을 중단합니다.", this);
                return false;
            }

            if (networkManager.NetworkConfig.PlayerPrefab != null)
            {
                Debug.LogWarning(
                    "[플레이어 스폰] NetworkManager.PlayerPrefab이 설정되어 있어 수동 Spawn을 중단합니다. 자동 Spawn과 중복될 수 있습니다.",
                    this);
                return false;
            }

            return true;
        }

        private void SpawnPlayerForClient(ulong clientId, Transform spawnPoint)
        {
            GameObject instance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();

            if (networkObject == null)
            {
                Destroy(instance);
                Debug.LogError(
                    $"[플레이어 스폰] 생성된 Player 인스턴스에 NetworkObject가 없습니다. ClientId={clientId}", this);
                return;
            }

            networkObject.SpawnAsPlayerObject(clientId, true);

            // Owner 권위 NetworkTransform 보정: 서버가 정한 SpawnPoint를 Owner Client에 전달.
            PlayerSpawnInitializer initializer = instance.GetComponent<PlayerSpawnInitializer>();
            if (initializer != null)
            {
                initializer.SendSpawnPointToOwner(spawnPoint.position, spawnPoint.rotation, clientId);
            }
            else
            {
                Debug.LogError(
                    $"[플레이어 스폰] Player Prefab Root에 PlayerSpawnInitializer가 없습니다. ClientId={clientId}",
                    this);
            }

            LogDebug(
                $"PlayerObject Spawn 완료. ClientId={clientId}, " +
                $"SpawnPoint={spawnPoint.name}, OwnerClientId={networkObject.OwnerClientId}");
        }

        private bool TryGetNetworkManager(out NetworkManager networkManager)
        {
            networkManager = NetworkManager.Singleton;

            if (networkManager != null)
                return true;

            Debug.LogWarning("[플레이어 스폰] NetworkManager.Singleton을 찾을 수 없습니다.", this);
            return false;
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

            Debug.Log($"[플레이어 스폰] {message}", this);
        }
    }
}
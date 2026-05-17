using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeadZone.Core;
using DeadZone.Systems.Raid;
using DeadZone.Systems.Save;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Network
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RaidClearCoordinator : NetworkBehaviour
    {
        [Header("==== 저장 요청 ====")]
        [Tooltip("클리어 후 각 클라이언트가 저장할 해금 ID입니다.")]
        [SerializeField] private string unlockZoneId = "MapB_All";

        [Header("==== Lobby 복귀 ====")]
        [Tooltip("저장 결과를 기다릴 최대 시간입니다. 단위: 초. 모든 대상이 먼저 응답하면 즉시 Lobby 복귀를 시작합니다.")]
        [SerializeField, Min(0.1f)] private float saveResultTimeoutSeconds = 10f;

        [Tooltip("레이드 종료 후 복귀할 Lobby 씬 이름입니다. Build Settings에 등록된 씬 이름과 일치해야 합니다.")]
        [SerializeField] private string lobbySceneName = "Lobby";

        [Header("==== 로그 ====")]
        [Tooltip("클리어 감지, 저장 요청, 저장 결과 수신, Lobby 복귀 로그를 출력합니다.")]
        [SerializeField] private bool logDebug = true;

        private readonly List<ulong> expectedClientIdsBuffer = new();
        private readonly List<ulong> extractedClientIdsBuffer = new();
        private readonly Dictionary<ulong, bool> saveResultsByClientId = new();
        private readonly HashSet<ulong> receivedSaveResultClientIds = new();

        private readonly List<ulong> successfulClientIdsBuffer = new();
        private readonly List<ulong> failedClientIdsBuffer = new();
        private readonly List<ulong> missingClientIdsBuffer = new();

        private Coroutine saveResultWaitCoroutine;

        private bool hasHandledClear;
        private bool hasCompletedSaveResultWait;
        private bool hasRequestedLobbyReturn;

        public bool HasClearHandled => hasHandledClear;

        public bool TryRequestPartyClear(ulong triggeringClientId)
        {
            return TryRequestPartyClear(triggeringClientId, null);
        }

        public bool TryRequestPartyClear(ulong triggeringClientId, IReadOnlyList<ulong> extractedClientIds)
        {
            if (!IsServer) return false;
            if (!IsSpawned) return false;
            if (hasHandledClear) return false;

            hasHandledClear = true;

            if (!TryResolveGameSessionManager(out GameSessionManager gameSessionManager))
            {
                Debug.LogWarning(
                    $"[레이드 클리어] GameSessionManager를 찾지 못했습니다. TriggerClientId={triggeringClientId}",
                    this);

                return true;
            }

            expectedClientIdsBuffer.Clear();
            gameSessionManager.GetExpectedClientIdOrder(expectedClientIdsBuffer);

            if (expectedClientIdsBuffer.Count == 0)
            {
                Debug.LogWarning(
                    $"[레이드 클리어] 출격 대상 ClientId 목록이 비어 있습니다. TriggerClientId={triggeringClientId}",
                    this);

                return true;
            }

            BuildExtractionClientIds(extractedClientIds, triggeringClientId);
            PrepareSaveResultTracking(extractedClientIdsBuffer);

            LogDebug(
                $"Map 1 Clear 감지. TriggerClientId={triggeringClientId}, " +
                $"ExpectedClients={FormatClientIds(expectedClientIdsBuffer)}");

            SendZoneUnlockRequestClientRpc(
                new FixedString64Bytes(unlockZoneId),
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = extractedClientIdsBuffer.ToArray()
                    }
                });

            LogDebug(
                $"Zone 저장 요청 전송. ZoneId={unlockZoneId}, " +
                $"Targets={FormatClientIds(extractedClientIdsBuffer)}");

            StartSaveResultWait();

            return true;
        }

        [ClientRpc]
        private void SendZoneUnlockRequestClientRpc(
            FixedString64Bytes requestedZoneId,
            ClientRpcParams rpcParams = default)
        {
            _ = HandleZoneUnlockRequestAsync(requestedZoneId);
        }

        private async Task HandleZoneUnlockRequestAsync(FixedString64Bytes requestedZoneId)
        {
            string zoneId = requestedZoneId.ToString();

            ulong localClientId = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : ulong.MaxValue;

            try
            {
                if (!TryResolveCloudSaveSystem(out CloudSaveSystem cloudSaveSystem))
                {
                    Debug.LogWarning(
                        $"[레이드 클리어] Zone 저장 실패. ClientId={localClientId}, ZoneId={zoneId}, Reason=CloudSaveSystem 없음",
                        this);

                    ReportZoneUnlockSaveResultRpc(
                        new FixedString64Bytes(zoneId),
                        false,
                        new FixedString128Bytes("CloudSaveSystem 없음"));

                    return;
                }

                if (!RaidLoadoutTransferService.TryCreateLocalRaidReturnLobbySaveDTO(out LobbySaveDTO extractionSaveDto))
                {
                    Debug.LogWarning(
                        $"[RaidClearCoordinator] Extraction save DTO failed. ClientId={localClientId}, ZoneId={zoneId}",
                        this);

                    ReportZoneUnlockSaveResultRpc(
                        new FixedString64Bytes(zoneId),
                        false,
                        new FixedString128Bytes("Extraction save DTO failed"));

                    return;
                }

                bool loadoutSaved = await cloudSaveSystem.SaveLobbyDataAsync(extractionSaveDto);
                bool zoneUnlocked = await cloudSaveSystem.UnlockZoneAndUploadAsync(zoneId);
                bool success = loadoutSaved && zoneUnlocked;

                if (success)
                {
                    LogDebug($"Zone 저장 완료. ClientId={localClientId}, ZoneId={zoneId}");

                    ReportZoneUnlockSaveResultRpc(
                        new FixedString64Bytes(zoneId),
                        true,
                        default);

                    return;
                }

                Debug.LogWarning(
                    $"[레이드 클리어] Zone 저장 실패. ClientId={localClientId}, ZoneId={zoneId}",
                    this);

                ReportZoneUnlockSaveResultRpc(
                    new FixedString64Bytes(zoneId),
                    false,
                    new FixedString128Bytes("Upload 실패"));
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[레이드 클리어] Zone 저장 중 예외 발생. ClientId={localClientId}, ZoneId={zoneId}, Error={ex}",
                    this);

                ReportZoneUnlockSaveResultRpc(
                    new FixedString64Bytes(zoneId),
                    false,
                    new FixedString128Bytes("예외 발생"));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportZoneUnlockSaveResultRpc(
            FixedString64Bytes savedZoneId,
            bool success,
            FixedString128Bytes reason,
            RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            string zoneId = savedZoneId.ToString();

            if (!saveResultsByClientId.ContainsKey(senderClientId))
            {
                Debug.LogWarning(
                    $"[레이드 클리어] 저장 대상이 아닌 ClientId의 결과를 무시합니다. " +
                    $"ClientId={senderClientId}, ZoneId={zoneId}",
                    this);

                return;
            }

            if (zoneId != unlockZoneId)
            {
                Debug.LogWarning(
                    $"[레이드 클리어] 요청과 다른 Zone 저장 결과를 무시합니다. " +
                    $"ClientId={senderClientId}, ExpectedZoneId={unlockZoneId}, ReportedZoneId={zoneId}",
                    this);

                return;
            }

            if (!receivedSaveResultClientIds.Add(senderClientId))
            {
                LogDebug(
                    $"Zone 저장 결과 중복 보고를 무시합니다. " +
                    $"ClientId={senderClientId}, ZoneId={zoneId}");

                return;
            }

            saveResultsByClientId[senderClientId] = success;

            if (success)
            {
                LogDebug(
                    $"Zone 저장 결과 수신. ClientId={senderClientId}, " +
                    $"ZoneId={zoneId}, Success=True");
            }
            else
            {
                Debug.LogWarning(
                    $"[레이드 클리어] Zone 저장 결과 수신. ClientId={senderClientId}, " +
                    $"ZoneId={zoneId}, Success=False, Reason={reason}",
                    this);
            }

            LogDebug(
                $"Zone 저장 결과 진행 상황. " +
                $"Received={receivedSaveResultClientIds.Count}/{saveResultsByClientId.Count}");

            if (receivedSaveResultClientIds.Count >= saveResultsByClientId.Count)
                CompleteSaveResultWait(false);
        }

        private void StartSaveResultWait()
        {
            if (!IsServer) return;

            if (saveResultWaitCoroutine != null)
                StopCoroutine(saveResultWaitCoroutine);

            saveResultWaitCoroutine = StartCoroutine(WaitForSaveResults());
        }

        private IEnumerator WaitForSaveResults()
        {
            float endTime = Time.unscaledTime + saveResultTimeoutSeconds;

            while (!hasCompletedSaveResultWait &&
                   receivedSaveResultClientIds.Count < saveResultsByClientId.Count &&
                   Time.unscaledTime < endTime)
            {
                yield return null;
            }

            saveResultWaitCoroutine = null;

            if (hasCompletedSaveResultWait)
                yield break;

            bool timedOut = receivedSaveResultClientIds.Count < saveResultsByClientId.Count;
            CompleteSaveResultWait(timedOut);
        }

        private void CompleteSaveResultWait(bool timedOut)
        {
            if (!IsServer) return;
            if (hasCompletedSaveResultWait) return;

            hasCompletedSaveResultWait = true;

            if (saveResultWaitCoroutine != null)
            {
                StopCoroutine(saveResultWaitCoroutine);
                saveResultWaitCoroutine = null;
            }

            BuildSaveResultSummary();

            if (timedOut || failedClientIdsBuffer.Count > 0 || missingClientIdsBuffer.Count > 0)
            {
                Debug.LogWarning(
                    $"[레이드 클리어] Zone 저장 결과 대기 완료. " +
                    $"TimedOut={timedOut}, " +
                    $"Success={FormatClientIds(successfulClientIdsBuffer)}, " +
                    $"Failed={FormatClientIds(failedClientIdsBuffer)}, " +
                    $"Missing={FormatClientIds(missingClientIdsBuffer)}",
                    this);
            }
            else
            {
                Debug.Log(
                    $"[레이드 클리어] Zone 저장 결과 대기 완료. " +
                    $"Success={FormatClientIds(successfulClientIdsBuffer)}",
                    this);
            }

            if (!TryRequestRaidResultScene())
            {
                ResetGameSessionTracking();
                RequestLobbyReturn();
            }
        }

        private void BuildSaveResultSummary()
        {
            successfulClientIdsBuffer.Clear();
            failedClientIdsBuffer.Clear();
            missingClientIdsBuffer.Clear();

            for (int i = 0; i < extractedClientIdsBuffer.Count; i++)
            {
                ulong clientId = extractedClientIdsBuffer[i];

                if (!receivedSaveResultClientIds.Contains(clientId))
                {
                    missingClientIdsBuffer.Add(clientId);
                    continue;
                }

                if (saveResultsByClientId.TryGetValue(clientId, out bool success) && success)
                    successfulClientIdsBuffer.Add(clientId);
                else
                    failedClientIdsBuffer.Add(clientId);
            }
        }

        private void BuildExtractionClientIds(IReadOnlyList<ulong> extractedClientIds, ulong fallbackClientId)
        {
            extractedClientIdsBuffer.Clear();

            if (extractedClientIds != null)
            {
                for (int i = 0; i < extractedClientIds.Count; i++)
                    AddExtractionClientId(extractedClientIds[i]);
            }

            if (extractedClientIdsBuffer.Count == 0)
            {
                for (int i = 0; i < expectedClientIdsBuffer.Count; i++)
                {
                    ulong clientId = expectedClientIdsBuffer[i];
                    if (IsClientLiving(clientId))
                        AddExtractionClientId(clientId);
                }
            }

            if (extractedClientIdsBuffer.Count == 0)
                AddExtractionClientId(fallbackClientId);
        }

        private void AddExtractionClientId(ulong clientId)
        {
            if (extractedClientIdsBuffer.Contains(clientId))
                return;

            extractedClientIdsBuffer.Add(clientId);
        }

        private bool TryRequestRaidResultScene()
        {
            if (!TryResolveGameSessionManager(out GameSessionManager gameSessionManager))
                return false;

            return gameSessionManager.TryCompleteRaidByExtraction(extractedClientIdsBuffer);
        }

        private static bool IsClientLiving(ulong clientId)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null ||
                !networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                client.PlayerObject == null)
            {
                return true;
            }

            DeadZone.Actors.PlayerHealthSystem health = client.PlayerObject.GetComponent<DeadZone.Actors.PlayerHealthSystem>();
            return health == null || !health.IsDead;
        }

        private void RequestLobbyReturn()
        {
            if (!IsServer) return;
            if (hasRequestedLobbyReturn) return;

            if (string.IsNullOrWhiteSpace(lobbySceneName))
            {
                Debug.LogWarning("[레이드 클리어] Lobby 씬 이름이 비어 있어 복귀할 수 없습니다.", this);
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null || networkManager.SceneManager == null)
            {
                Debug.LogWarning("[레이드 클리어] NetworkSceneManager를 찾을 수 없어 Lobby 복귀를 시작하지 못했습니다.", this);
                return;
            }

            SceneEventProgressStatus status =
                networkManager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning(
                    $"[레이드 클리어] Lobby 씬 로드 요청 실패. Scene={lobbySceneName}, Status={status}",
                    this);

                return;
            }

            hasRequestedLobbyReturn = true;

            Debug.Log($"[레이드 클리어] Lobby 씬 로드 요청. Scene={lobbySceneName}", this);
        }

        private void ResetGameSessionTracking()
        {
            if (!TryResolveGameSessionManager(out GameSessionManager gameSessionManager))
                return;

            if (!gameSessionManager.IsSpawned)
                return;

            gameSessionManager.CancelLoadTracking("레이드 클리어 처리 완료");
        }

        private void PrepareSaveResultTracking(IReadOnlyList<ulong> targetClientIds)
        {
            saveResultsByClientId.Clear();
            receivedSaveResultClientIds.Clear();

            successfulClientIdsBuffer.Clear();
            failedClientIdsBuffer.Clear();
            missingClientIdsBuffer.Clear();

            hasCompletedSaveResultWait = false;
            hasRequestedLobbyReturn = false;

            foreach (ulong clientId in targetClientIds)
                saveResultsByClientId[clientId] = false;
        }

        private bool TryResolveGameSessionManager(out GameSessionManager gameSessionManager)
        {
            if (ServiceLocator.TryGet(out gameSessionManager) && gameSessionManager != null)
                return true;

            gameSessionManager = FindFirstObjectByType<GameSessionManager>();
            return gameSessionManager != null;
        }

        private bool TryResolveCloudSaveSystem(out CloudSaveSystem cloudSaveSystem)
        {
            if (ServiceLocator.TryGet(out cloudSaveSystem) && cloudSaveSystem != null)
                return true;

            cloudSaveSystem = FindFirstObjectByType<CloudSaveSystem>();
            return cloudSaveSystem != null;
        }

        private static string FormatClientIds(IReadOnlyList<ulong> clientIds)
        {
            if (clientIds == null || clientIds.Count == 0) return "[]";

            return $"[{string.Join(", ", clientIds)}]";
        }

        private void LogDebug(string message)
        {
            if (!logDebug) return;

            Debug.Log($"[레이드 클리어] {message}", this);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeadZone.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Network
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RaidClearCoordinator : NetworkBehaviour
    {
        [Header("==== 저장 요청 ====")]
        [Tooltip("클리어 후 각 클라이언트가 저장할 해금 ID입니다.")]
        [SerializeField] private string unlockZoneId = "MapB_All";

        [Header("==== 로그 ====")]
        [Tooltip("클리어 감지, 저장 요청, 저장 결과 수신 로그를 출력합니다.")]
        [SerializeField] private bool logDebug = true;

        private readonly List<ulong> expectedClientIdsBuffer = new();
        private readonly Dictionary<ulong, bool> saveResultsByClientId = new();
        private readonly HashSet<ulong> receivedSaveResultClientIds = new();

        private bool hasHandledClear;

        public bool HasClearHandled => hasHandledClear;

        public bool TryRequestPartyClear(ulong triggeringClientId)
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

            PrepareSaveResultTracking(expectedClientIdsBuffer);

            LogDebug(
                $"Map 1 Clear 감지. TriggerClientId={triggeringClientId}, " +
                $"ExpectedClients={FormatClientIds(expectedClientIdsBuffer)}");

            SendZoneUnlockRequestClientRpc(
                new FixedString64Bytes(unlockZoneId),
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = expectedClientIdsBuffer.ToArray()
                    }
                });

            LogDebug(
                $"Zone 저장 요청 전송. ZoneId={unlockZoneId}, " +
                $"Targets={FormatClientIds(expectedClientIdsBuffer)}");

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

                bool success = await cloudSaveSystem.UnlockZoneAndUploadAsync(zoneId);

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
        }

        private void PrepareSaveResultTracking(IReadOnlyList<ulong> targetClientIds)
        {
            saveResultsByClientId.Clear();
            receivedSaveResultClientIds.Clear();

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
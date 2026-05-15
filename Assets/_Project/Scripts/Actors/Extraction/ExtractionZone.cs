using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Actors
{
    /// <summary>
    /// 탈출 카운트다운을 시작하는 트리거 존. 완료 시 은신처로 복귀.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(NetworkObject))]
    public class ExtractionZone : NetworkBehaviour, IInteractable
    {
        [SerializeField] private string extractionId = "Truck";
        [SerializeField] private bool requireAllExpectedPlayersInZone = true;
        [SerializeField] private RaidClearCoordinator raidClearCoordinator;

        private static ExtractionZone activeLocalPromptZone;

        private readonly HashSet<ulong> clientsInZone = new();
        private readonly HashSet<ulong> pendingExtractionClientIds = new();
        private readonly List<ulong> expectedClientBuffer = new();

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || !netObj.IsPlayerObject) return;

            clientsInZone.Add(netObj.OwnerClientId);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || !netObj.IsPlayerObject) return;

            ulong clientId = netObj.OwnerClientId;
            clientsInZone.Remove(clientId);
            CancelExtraction(clientId);
        }

        public void OnInteract(ulong clientId)
        {
            if (IsServer)
            {
                TryOpenExtractionPrompt(clientId);
                return;
            }

            RequestExtractionRpc(clientId);
        }

        public string GetPromptText()
        {
            return $"[F] Extract - {extractionId}";
        }

        public static void ConfirmCurrentPrompt()
        {
            if (activeLocalPromptZone == null)
                return;

            ulong clientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            activeLocalPromptZone.ConfirmExtraction(clientId);
        }

        public static void CancelCurrentPrompt()
        {
            if (activeLocalPromptZone == null)
                return;

            ulong clientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            activeLocalPromptZone.CancelExtraction(clientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestExtractionRpc(ulong requestedClientId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (requestedClientId != senderClientId)
            {
                Debug.LogWarning(
                    $"[ExtractionZone] Ignoring mismatched extraction request. requested={requestedClientId}, sender={senderClientId}",
                    this);
            }

            TryOpenExtractionPrompt(senderClientId);
        }

        private void ConfirmExtraction(ulong clientId)
        {
            if (IsServer)
            {
                TryCompleteExtraction(clientId);
                return;
            }

            ConfirmExtractionRpc(clientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ConfirmExtractionRpc(ulong requestedClientId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (requestedClientId != senderClientId)
            {
                Debug.LogWarning(
                    $"[ExtractionZone] Ignoring mismatched extraction confirm. requested={requestedClientId}, sender={senderClientId}",
                    this);
            }

            TryCompleteExtraction(senderClientId);
        }

        private void CancelExtraction(ulong clientId)
        {
            if (IsServer)
            {
                CancelExtractionForParty();
                return;
            }

            CancelExtractionRpc(clientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CancelExtractionRpc(ulong requestedClientId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (requestedClientId != senderClientId)
            {
                Debug.LogWarning(
                    $"[ExtractionZone] Ignoring mismatched extraction cancel. requested={requestedClientId}, sender={senderClientId}",
                    this);
            }

            CancelExtractionForParty();
        }

        private bool TryOpenExtractionPrompt(ulong clientId)
        {
            if (!IsServer) return false;

            if (!clientsInZone.Contains(clientId))
            {
                Debug.LogWarning(
                    $"[ExtractionZone] Extraction requested outside zone. clientId={clientId}, extractionId={extractionId}",
                    this);
                return false;
            }

            if (pendingExtractionClientIds.Contains(clientId))
                return true;

            if (!TryGetExtractionParticipants(clientId, expectedClientBuffer))
                return false;

            for (int i = 0; i < expectedClientBuffer.Count; i++)
            {
                ulong participantId = expectedClientBuffer[i];
                pendingExtractionClientIds.Add(participantId);
                EventBus.Publish(new ExtractionStartedEvent
                {
                    clientId = participantId,
                    extractionId = extractionId,
                    countdownSeconds = 0f,
                });
                PublishExtractionStartedClientRpc(participantId, new FixedString64Bytes(extractionId));
            }

            activeLocalPromptZone = this;
            return true;
        }

        private bool TryGetExtractionParticipants(ulong requestingClientId, List<ulong> participants)
        {
            participants.Clear();

            if (!requireAllExpectedPlayersInZone)
            {
                participants.Add(requestingClientId);
                return true;
            }

            GameSessionManager gameSessionManager = Core.ServiceLocator.Get<GameSessionManager>();
            gameSessionManager?.GetExpectedClientIdOrder(participants);

            if (participants.Count == 0)
                participants.Add(requestingClientId);

            for (int i = participants.Count - 1; i >= 0; i--)
            {
                ulong clientId = participants[i];
                NetworkManager networkManager = NetworkManager.Singleton;
                if (networkManager != null && !networkManager.ConnectedClients.ContainsKey(clientId))
                    participants.RemoveAt(i);
            }

            if (participants.Count == 0)
                participants.Add(requestingClientId);

            for (int i = 0; i < participants.Count; i++)
            {
                clientId = clientId,
                extractionId = extractionId,
                countdownSeconds = extractionTime,
            });
            PublishExtractionStartedClientRpc(clientId, new FixedString64Bytes(extractionId), extractionTime);
            return true;
        }

        private bool TryCompleteExtraction(ulong clientId)
        {
            if (!IsServer || !pendingExtractionClientIds.Contains(clientId))
                return false;

            if (!clientsInZone.Contains(clientId) || !TryGetExtractionParticipants(clientId, expectedClientBuffer))
            {
                CancelExtractionForParty();
                return false;
            }

            var completedClientIds = new List<ulong>(pendingExtractionClientIds);
            pendingExtractionClientIds.Clear();

            for (int i = 0; i < completedClientIds.Count; i++)
            {
                ulong completedClientId = completedClientIds[i];
                EventBus.Publish(new ExtractionCompletedEvent
                {
                    clientId = completedClientId,
                    extractionId = extractionId,
                });
                PublishExtractionCompletedClientRpc(completedClientId, new FixedString64Bytes(extractionId));
            }

            activeLocalPromptZone = null;
            if (!TryRequestRaidClear(clientId))
                Core.ServiceLocator.Get<NetworkGameManager>()?.ReturnToHideoutServerRpc();

            return true;
        }

        private void PublishExtractionCanceled(ulong clientId)
        {
            if (!IsServer) return;

            EventBus.Publish(new ExtractionCanceledEvent
            {
                clientId = clientId,
                extractionId = extractionId,
            });
            PublishExtractionCanceledClientRpc(clientId, new FixedString64Bytes(extractionId));
        }

        private void CancelExtractionForParty()
        {
            if (!IsServer || pendingExtractionClientIds.Count == 0)
                return;

            var clientIds = new List<ulong>(pendingExtractionClientIds);
            pendingExtractionClientIds.Clear();
            for (int i = 0; i < clientIds.Count; i++)
                PublishExtractionCanceled(clientIds[i]);

            activeLocalPromptZone = null;
        }

        private bool TryRequestRaidClear(ulong clientId)
        {
            if (raidClearCoordinator == null)
                raidClearCoordinator = FindFirstObjectByType<RaidClearCoordinator>();

            return raidClearCoordinator != null &&
                   raidClearCoordinator.TryRequestPartyClear(clientId);
        }

        [ClientRpc]
        private void PublishExtractionStartedClientRpc(
            ulong clientId,
            FixedString64Bytes id)
        {
            if (IsServer)
                return;

            activeLocalPromptZone = this;
            EventBus.Publish(new ExtractionStartedEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
                countdownSeconds = 0f,
            });
        }

        [ClientRpc]
        private void PublishExtractionCompletedClientRpc(ulong clientId, FixedString64Bytes id)
        {
            if (IsServer)
                return;

            activeLocalPromptZone = null;
            EventBus.Publish(new ExtractionCompletedEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
            });
        }

        [ClientRpc]
        private void PublishExtractionCanceledClientRpc(ulong clientId, FixedString64Bytes id)
        {
            if (IsServer)
                return;

            activeLocalPromptZone = null;
            EventBus.Publish(new ExtractionCanceledEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
            });
        }
    }
}

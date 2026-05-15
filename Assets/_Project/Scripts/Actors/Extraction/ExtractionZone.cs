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
        [SerializeField] private float extractionTime = 7f;
        [SerializeField] private RaidClearCoordinator raidClearCoordinator;

        private readonly HashSet<ulong> clientsInZone = new();
        private readonly Dictionary<ulong, float> extractionTimersByClientId = new();

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
                TryStartExtraction(clientId);
                return;
            }

            RequestExtractionRpc(clientId);
        }

        public string GetPromptText()
        {
            return $"[F] Extract - {extractionId}";
        }

        private void Update()
        {
            if (!IsServer) return;
            if (extractionTimersByClientId.Count == 0) return;

            var keys = new List<ulong>(extractionTimersByClientId.Keys);
            foreach (var cid in keys)
            {
                if (!clientsInZone.Contains(cid))
                {
                    CancelExtraction(cid);
                    continue;
                }

                extractionTimersByClientId[cid] += Time.deltaTime;
                if (extractionTimersByClientId[cid] >= extractionTime)
                {
                    EventBus.Publish(new ExtractionCompletedEvent
                    {
                        clientId = cid,
                        extractionId = extractionId,
                    });
                    PublishExtractionCompletedClientRpc(cid, new FixedString64Bytes(extractionId));
                    extractionTimersByClientId.Remove(cid);
                    if (!TryRequestRaidClear(cid))
                        Core.ServiceLocator.Get<NetworkGameManager>()?.ReturnToHideoutServerRpc();
                }
            }
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

            TryStartExtraction(senderClientId);
        }

        private bool TryStartExtraction(ulong clientId)
        {
            if (!IsServer) return false;

            if (!clientsInZone.Contains(clientId))
            {
                Debug.LogWarning(
                    $"[ExtractionZone] Extraction requested outside zone. clientId={clientId}, extractionId={extractionId}",
                    this);
                return false;
            }

            if (extractionTimersByClientId.ContainsKey(clientId))
                return true;

            extractionTimersByClientId[clientId] = 0f;
            EventBus.Publish(new ExtractionStartedEvent
            {
                clientId = clientId,
                extractionId = extractionId,
                countdownSeconds = extractionTime,
            });
            PublishExtractionStartedClientRpc(clientId, new FixedString64Bytes(extractionId), extractionTime);
            return true;
        }

        private void CancelExtraction(ulong clientId)
        {
            if (!IsServer) return;
            if (!extractionTimersByClientId.Remove(clientId)) return;

            EventBus.Publish(new ExtractionCanceledEvent
            {
                clientId = clientId,
                extractionId = extractionId,
            });
            PublishExtractionCanceledClientRpc(clientId, new FixedString64Bytes(extractionId));
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
            FixedString64Bytes id,
            float countdownSeconds)
        {
            if (IsServer)
                return;

            EventBus.Publish(new ExtractionStartedEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
                countdownSeconds = countdownSeconds,
            });
        }

        [ClientRpc]
        private void PublishExtractionCompletedClientRpc(ulong clientId, FixedString64Bytes id)
        {
            if (IsServer)
                return;

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

            EventBus.Publish(new ExtractionCanceledEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
            });
        }
    }
}

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
    public class ExtractionZone : NetworkBehaviour
    {
        [SerializeField] private string extractionId = "Truck";
        [SerializeField] private float extractionTime = 7f;
        [SerializeField] private RaidClearCoordinator raidClearCoordinator;

        private Dictionary<ulong, float> playersInZone = new();

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

            ulong cid = netObj.OwnerClientId;
            if (!playersInZone.ContainsKey(cid))
            {
                playersInZone[cid] = 0f;
                EventBus.Publish(new ExtractionStartedEvent
                {
                    clientId = cid,
                    extractionId = extractionId,
                    countdownSeconds = extractionTime,
                });
                PublishExtractionStartedClientRpc(cid, new FixedString64Bytes(extractionId), extractionTime);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || !netObj.IsPlayerObject) return;

            playersInZone.Remove(netObj.OwnerClientId);
        }

        private void Update()
        {
            if (!IsServer) return;
            if (playersInZone.Count == 0) return;

            var keys = new List<ulong>(playersInZone.Keys);
            foreach (var cid in keys)
            {
                playersInZone[cid] += Time.deltaTime;
                if (playersInZone[cid] >= extractionTime)
                {
                    EventBus.Publish(new ExtractionCompletedEvent
                    {
                        clientId = cid,
                        extractionId = extractionId,
                    });
                    PublishExtractionCompletedClientRpc(cid, new FixedString64Bytes(extractionId));
                    playersInZone.Remove(cid);
                    if (!TryRequestRaidClear(cid))
                        Core.ServiceLocator.Get<NetworkGameManager>()?.ReturnToHideoutServerRpc();
                }
            }
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
    }
}

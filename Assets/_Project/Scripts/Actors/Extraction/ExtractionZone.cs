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
        private const float FixedExtractionTime = 5f;

        [SerializeField] private string extractionId = "Truck";
        [SerializeField] private float extractionTime = FixedExtractionTime;
        [SerializeField] private bool requireAllExpectedPlayersInZone = true;
        [SerializeField] private RaidClearCoordinator raidClearCoordinator;
        [SerializeField] private ExtractionUI extractionUI;

        private static ExtractionZone activeLocalPromptZone;

        private readonly HashSet<ulong> clientsInZone = new();
        private readonly HashSet<ulong> pendingExtractionClientIds = new();
        private readonly List<ulong> expectedClientBuffer = new();
        private float extractionElapsed;
        private bool localExtractionUiActive;
        private float localExtractionElapsed;
        private float localExtractionDuration;

        private void Awake()
        {
            extractionTime = FixedExtractionTime;
        }

        private void OnValidate()
        {
            extractionTime = FixedExtractionTime;
        }

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

            ulong clientId = netObj.OwnerClientId;
            clientsInZone.Add(clientId);
            TryOpenExtractionPrompt(clientId);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || !netObj.IsPlayerObject) return;

            ulong clientId = netObj.OwnerClientId;
            clientsInZone.Remove(clientId);
            CancelExtractionForParty();
        }

        private void Update()
        {
            UpdateLocalExtractionUI();

            if (!IsServer || pendingExtractionClientIds.Count == 0)
                return;

            if (!ArePendingClientsInZone())
            {
                CancelExtractionForParty();
                return;
            }

            extractionElapsed += Time.deltaTime;
            if (extractionElapsed >= extractionTime)
                TryCompleteFirstPendingExtraction();
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

            if (pendingExtractionClientIds.Count > 0)
                return false;

            if (!TryGetExtractionParticipants(clientId, expectedClientBuffer))
                return false;

            extractionElapsed = 0f;
            Debug.Log($"[ExtractionZone] Extraction countdown started. extractionId={extractionId}, participants={expectedClientBuffer.Count}", this);
            for (int i = 0; i < expectedClientBuffer.Count; i++)
            {
                ulong participantId = expectedClientBuffer[i];
                pendingExtractionClientIds.Add(participantId);
                EventBus.Publish(new ExtractionStartedEvent
                {
                    clientId = participantId,
                    extractionId = extractionId,
                    countdownSeconds = extractionTime,
                });
                PublishExtractionStartedClientRpc(
                    participantId,
                    new FixedString64Bytes(extractionId),
                    extractionTime,
                    BuildTargetClientRpcParams(participantId));

                if (IsLocalClient(participantId))
                {
                    activeLocalPromptZone = this;
                    ShowLocalExtractionUI(extractionId, extractionTime);
                    SetLocalExtractionMoveLocked(true);
                }
            }
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

            if (participants.Count == 0 && IsClientLiving(requestingClientId))
                participants.Add(requestingClientId);

            for (int i = participants.Count - 1; i >= 0; i--)
            {
                ulong clientId = participants[i];
                NetworkManager networkManager = NetworkManager.Singleton;
                if (networkManager != null && !networkManager.ConnectedClients.ContainsKey(clientId))
                {
                    participants.RemoveAt(i);
                    continue;
                }

                if (!IsClientLiving(clientId))
                    participants.RemoveAt(i);
            }

            if (participants.Count == 0 && IsClientLiving(requestingClientId))
                participants.Add(requestingClientId);

            if (participants.Count == 0)
                return false;

            for (int i = 0; i < participants.Count; i++)
            {
                if (!clientsInZone.Contains(participants[i]))
                {
                    Debug.Log(
                        $"[ExtractionZone] Extraction waits for all party members. requester={requestingClientId}, missing={participants[i]}, extractionId={extractionId}",
                        this);
                    return false;
                }
            }

            return true;
        }

        private static bool IsClientLiving(ulong clientId)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null ||
                !networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                client.PlayerObject == null)
                return true;

            PlayerHealthSystem health = client.PlayerObject.GetComponent<PlayerHealthSystem>();
            return health == null || !health.IsDead;
        }

        private bool ArePendingClientsInZone()
        {
            foreach (ulong clientId in pendingExtractionClientIds)
            {
                if (!clientsInZone.Contains(clientId))
                    return false;
            }

            return true;
        }

        private bool TryCompleteFirstPendingExtraction()
        {
            foreach (ulong clientId in pendingExtractionClientIds)
                return TryCompleteExtraction(clientId);

            return false;
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
            extractionElapsed = 0f;

            for (int i = 0; i < completedClientIds.Count; i++)
            {
                ulong completedClientId = completedClientIds[i];
                EventBus.Publish(new ExtractionCompletedEvent
                {
                    clientId = completedClientId,
                    extractionId = extractionId,
                });
                PublishExtractionCompletedClientRpc(
                    completedClientId,
                    new FixedString64Bytes(extractionId),
                    BuildTargetClientRpcParams(completedClientId));

                if (IsLocalClient(completedClientId))
                {
                    HideLocalExtractionUI();
                    SetLocalExtractionMoveLocked(false);
                }
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
            PublishExtractionCanceledClientRpc(
                clientId,
                new FixedString64Bytes(extractionId),
                BuildTargetClientRpcParams(clientId));

            if (IsLocalClient(clientId))
            {
                HideLocalExtractionUI();
                SetLocalExtractionMoveLocked(false);
            }
        }

        private void CancelExtractionForParty()
        {
            if (!IsServer || pendingExtractionClientIds.Count == 0)
                return;

            var clientIds = new List<ulong>(pendingExtractionClientIds);
            pendingExtractionClientIds.Clear();
            extractionElapsed = 0f;
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

        private static bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null &&
                   clientId == NetworkManager.Singleton.LocalClientId;
        }

        private static ClientRpcParams BuildTargetClientRpcParams(ulong clientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };
        }

        private void ShowLocalExtractionUI(string id, float duration)
        {
            localExtractionUiActive = true;
            localExtractionElapsed = 0f;
            localExtractionDuration = Mathf.Max(0f, duration);

            ExtractionUI ui = GetLocalExtractionUI();
            if (ui == null)
            {
                Debug.LogWarning($"[ExtractionZone] ExtractionUI not found. extractionId={id}", this);
                return;
            }

            if (!ui.gameObject.activeSelf)
                ui.gameObject.SetActive(true);

            Debug.Log($"[ExtractionZone] Show ExtractionUI. extractionId={id}, duration={duration}", ui);
            ui.Show(id, localExtractionDuration);
        }

        private void UpdateLocalExtractionUI()
        {
            if (!localExtractionUiActive || localExtractionDuration <= 0f)
                return;

            localExtractionElapsed += Time.deltaTime;

            ExtractionUI ui = GetLocalExtractionUI();
            if (ui != null)
                ui.UpdateProgress(localExtractionElapsed, localExtractionDuration);
        }

        private void HideLocalExtractionUI()
        {
            localExtractionUiActive = false;
            localExtractionElapsed = 0f;
            localExtractionDuration = 0f;

            ExtractionUI ui = GetLocalExtractionUI();
            if (ui != null)
                ui.Hide();
        }

        private ExtractionUI GetLocalExtractionUI()
        {
            if (extractionUI == null)
                extractionUI = FindFirstObjectByType<ExtractionUI>(FindObjectsInactive.Include);

            return extractionUI;
        }

        private static void SetLocalExtractionMoveLocked(bool locked)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            NetworkObject playerObject = networkManager != null &&
                                         networkManager.LocalClient != null
                ? networkManager.LocalClient.PlayerObject
                : null;

            FPSController fps = playerObject != null
                ? playerObject.GetComponent<FPSController>()
                : null;

            if (fps != null)
                fps.SetMoveLocked(locked);
        }

        [ClientRpc]
        private void PublishExtractionStartedClientRpc(
            ulong clientId,
            FixedString64Bytes id,
            float duration,
            ClientRpcParams clientRpcParams = default)
        {
            activeLocalPromptZone = this;

            EventBus.Publish(new ExtractionStartedEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
                countdownSeconds = duration,
            });

            ShowLocalExtractionUI(id.ToString(), duration);
            SetLocalExtractionMoveLocked(true);
        }

        [ClientRpc]
        private void PublishExtractionCompletedClientRpc(
            ulong clientId,
            FixedString64Bytes id,
            ClientRpcParams clientRpcParams = default)
        {
            activeLocalPromptZone = null;

            EventBus.Publish(new ExtractionCompletedEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
            });

            HideLocalExtractionUI();
            SetLocalExtractionMoveLocked(false);
        }

        [ClientRpc]
        private void PublishExtractionCanceledClientRpc(
            ulong clientId,
            FixedString64Bytes id,
            ClientRpcParams clientRpcParams = default)
        {
            activeLocalPromptZone = null;

            EventBus.Publish(new ExtractionCanceledEvent
            {
                clientId = clientId,
                extractionId = id.ToString(),
            });

            HideLocalExtractionUI();
            SetLocalExtractionMoveLocked(false);
        }
    }
}

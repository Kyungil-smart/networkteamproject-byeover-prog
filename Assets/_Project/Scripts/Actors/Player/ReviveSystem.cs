using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// F 키를 홀드하여 다운된 팀원을 부활시킨다. Owner 측에서 시도를 시작하고,
    /// 서버가 매 틱마다 거리 + 상태를 검증하여 타이머가 0에 도달하면 완료한다.
    /// </summary>
    public class ReviveSystem : NetworkBehaviour
    {
        [Header("부활 진행 설정")]
        [Tooltip("부활을 완료하는 데 필요한 시간입니다. 값이 클수록 더 오래 홀드해야 합니다. 단위: 초")]
        [SerializeField] private float reviveDuration = 5f;

        [Tooltip("부활 가능한 최대 거리입니다. " +
                 "구조자와 Knocked 대상 사이의 거리가 이 값보다 멀면 부활을 시작하거나 유지할 수 없습니다. 단위: 미터")]
        [SerializeField] private float reviveRange = 2f;

        [Tooltip("부활 시작 후 구조자가 움직일 수 있는 최대 거리입니다. " +
                 "이 값보다 많이 이동하면 서버에서 부활을 중단합니다. 단위: 미터")]
        [SerializeField] private float movementCancelDistance = 0.75f;

        [Header("부활 대상 탐색")]
        [Tooltip("부활 대상으로 탐색할 레이어입니다. 기본값은 Player 레이어(10)이며, " +
                 "대상 Player의 Collider 또는 CharacterController가 이 레이어에 포함되어야 합니다.")]
        [SerializeField] private LayerMask reviveTargetMask = 1 << 10;

        [Tooltip("부활 대상 탐색 Raycast의 시작 위치와 방향 기준입니다. " +
                 "비워두면 이 오브젝트의 Transform을 사용합니다.")]
        [SerializeField] private Transform raycastOrigin;

        [Header("부활 이벤트 동기화")]
        [Tooltip("부활 진행도 이벤트를 클라이언트에 보내는 최소 간격입니다. " +
                 "값이 낮을수록 UI 반응은 부드럽지만 네트워크 이벤트 빈도가 증가합니다. 단위: 초")]
        [SerializeField, Min(0.02f)] private float progressEventInterval = 0.1f;

        [Header("부활 상호작용 UI")]
        [SerializeField] private string revivePromptText = "[F] 팀원 살리기";

        [SerializeField] private string occupiedRevivePromptText = "다른 팀원이 구조 중입니다";

        public NetworkVariable<float> Progress = new(0f);
        public NetworkVariable<ulong> CurrentTargetClientId = new(ulong.MaxValue);

        private static readonly Dictionary<ulong, ulong> ActiveReviversByTargetClientId = new();

        private PlayerHealthSystem reviverHealth;
        private PlayerStatsUI playerStatsUI;
        private NetworkObject serverTargetObj;
        private Vector3 serverReviveStartPosition;
        private float nextProgressEventTime;
        private bool isShowingRevivePrompt;
        private string currentRevivePromptText;

        public bool IsReviving => CurrentTargetClientId.Value != ulong.MaxValue;

        private void Awake()
        {
            reviverHealth = GetComponent<PlayerHealthSystem>();
        }

        public override void OnNetworkSpawn()
        {
            EventBus.Subscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Subscribe<ReviveEndedEvent>(OnReviveEnded);
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<ReviveStartedEvent>(OnReviveStarted);
            EventBus.Unsubscribe<ReviveEndedEvent>(OnReviveEnded);
            HideRevivePrompt();
        }

        private void OnDisable()
        {
            HideRevivePrompt();
        }

        public void StartHold()
        {
            if (!IsOwner) return;
            if (IsReviving) return;
            if (!CanReviverAttemptRevive()) return;
            if (!TryFindReviveTarget(out NetworkObject targetObj)) return;
            if (IsTargetBeingRevivedByOther(targetObj)) return;

            HideRevivePrompt();
            BeginReviveServerRpc(targetObj.NetworkObjectId);
        }

        public void StopHold()
        {
            if (!IsOwner) return;

            CancelReviveServerRpc();
        }

        public bool HasReviveCandidate()
        {
            if (!IsOwner || IsReviving || !CanReviverAttemptRevive())
                return false;

            return TryFindReviveTarget(out _);
        }

        [ServerRpc]
        private void BeginReviveServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpc = default)
        {
            ulong reviverId = rpc.Receive.SenderClientId;
            if (reviverId != OwnerClientId)
                return;

            if (!CanReviverAttemptRevive())
                return;

            if (!TryResolveServerReviveTarget(targetNetworkObjectId, out NetworkObject targetObj))
                return;

            BeginReviveTargetOnServer(targetObj, reviverId);
        }

        public bool BeginReviveTargetOnServer(NetworkObject targetObj, ulong reviverId)
        {
            if (!IsServer || targetObj == null || targetObj == NetworkObject)
                return false;

            if (reviverId != OwnerClientId)
                return false;

            if (!CanReviverAttemptRevive())
                return false;

            var targetHealth = targetObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived)
                return false;

            if (targetHealth.IsBeingRevived && targetHealth.CurrentReviverClientId != reviverId)
                return false;

            if (!IsWithinReviveRange(targetObj))
                return false;

            if (IsReviving)
                return serverTargetObj == targetObj;

            serverTargetObj = targetObj;
            serverReviveStartPosition = transform.position;
            nextProgressEventTime = 0f;
            CurrentTargetClientId.Value = targetObj.OwnerClientId;
            Progress.Value = 0f;

            targetHealth.OnReviveBegin(reviverId);
            PublishReviveStarted(reviverId, targetObj.OwnerClientId);

            return true;
        }

        [ServerRpc]
        private void CancelReviveServerRpc(ServerRpcParams rpc = default)
        {
            ulong senderClientId = rpc.Receive.SenderClientId;
            if (senderClientId != OwnerClientId)
                return;

            EndOnServer(ReviveResult.Cancelled, senderClientId);
        }

        private void Update()
        {
            UpdateRevivePrompt();

            if (!IsServer) return;
            if (!IsReviving || serverTargetObj == null) return;

            if (!CanReviverAttemptRevive())
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            var targetHealth = serverTargetObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived)
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            if (Vector3.Distance(transform.position, serverReviveStartPosition) > movementCancelDistance)
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            if (!IsWithinReviveRange(serverTargetObj))
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            Progress.Value = Mathf.Min(1f, Progress.Value + Time.deltaTime / reviveDuration);
            if (Time.time >= nextProgressEventTime || Progress.Value >= 1f)
            {
                PublishReviveProgress(OwnerClientId, serverTargetObj.OwnerClientId, Progress.Value);
                nextProgressEventTime = Time.time + progressEventInterval;
            }

            if (Progress.Value >= 1f)
            {
                targetHealth.OnReviveComplete(OwnerClientId);
                EndOnServer(ReviveResult.Completed, OwnerClientId);
            }
        }

        private void EndOnServer(ReviveResult result, ulong reviverId)
        {
            if (!IsServer) return;
            if (!IsReviving) return;

            ulong targetId = CurrentTargetClientId.Value;

            if (result != ReviveResult.Completed && serverTargetObj != null)
            {
                var targetHealth = serverTargetObj.GetComponent<IRevivable>();
                targetHealth?.OnReviveCancel();
            }

            CurrentTargetClientId.Value = ulong.MaxValue;
            Progress.Value = 0f;
            serverTargetObj = null;
            nextProgressEventTime = 0f;

            PublishReviveEnded(reviverId, targetId, result);
        }

        private bool CanReviverAttemptRevive()
        {
            if (reviverHealth == null)
                reviverHealth = GetComponent<PlayerHealthSystem>();

            return reviverHealth != null &&
                   reviverHealth.IsAlive &&
                   !reviverHealth.IsKnocked &&
                   !reviverHealth.IsDead;
        }

        private void UpdateRevivePrompt()
        {
            if (!IsOwner)
                return;

            if (IsReviving || !CanReviverAttemptRevive() || !TryFindReviveTarget(out NetworkObject targetObj))
            {
                HideRevivePrompt();
                return;
            }

            if (IsTargetBeingRevivedByOther(targetObj))
            {
                ShowRevivePrompt(occupiedRevivePromptText);
            }
            else
            {
                ShowRevivePrompt(revivePromptText);
            }
        }

        private void ShowRevivePrompt(string promptText)
        {
            if (isShowingRevivePrompt && currentRevivePromptText == promptText)
                return;

            if (playerStatsUI == null)
                playerStatsUI = ResolvePlayerStatsUI();

            if (playerStatsUI == null)
                return;

            playerStatsUI.ShowRevivePrompt(promptText);
            isShowingRevivePrompt = true;
            currentRevivePromptText = promptText;
        }

        private void HideRevivePrompt()
        {
            if (!isShowingRevivePrompt)
                return;

            if (playerStatsUI == null)
                playerStatsUI = ResolvePlayerStatsUI();

            playerStatsUI?.HideRevivePrompt();
            isShowingRevivePrompt = false;
            currentRevivePromptText = string.Empty;
        }

        private PlayerStatsUI ResolvePlayerStatsUI()
        {
            PlayerStatsUI[] statsUis = FindObjectsByType<PlayerStatsUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            return statsUis.Length > 0 ? statsUis[0] : null;
        }

        private bool TryFindReviveTarget(out NetworkObject targetObj)
        {
            targetObj = null;

            Transform origin = raycastOrigin != null ? raycastOrigin : transform;
            if (Physics.Raycast(
                    origin.position,
                    origin.forward,
                    out RaycastHit hit,
                    reviveRange,
                    reviveTargetMask,
                    QueryTriggerInteraction.Collide) &&
                TryResolveRevivableTarget(hit.collider, out targetObj))
            {
                return true;
            }

            Collider[] candidates = Physics.OverlapSphere(
                transform.position,
                reviveRange,
                reviveTargetMask,
                QueryTriggerInteraction.Collide);

            float bestSqrDistance = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Collider candidate = candidates[i];
                if (!TryResolveRevivableTarget(candidate, out NetworkObject candidateObj))
                    continue;

                float sqrDistance = (candidateObj.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                targetObj = candidateObj;
            }

            return targetObj != null;
        }

        private bool TryResolveServerReviveTarget(ulong targetNetworkObjectId, out NetworkObject targetObj)
        {
            targetObj = null;

            if (!IsServer || NetworkManager == null || NetworkManager.SpawnManager == null)
                return false;

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject candidateObj))
                return false;

            if (candidateObj == null ||
                candidateObj == NetworkObject ||
                candidateObj.OwnerClientId == OwnerClientId)
            {
                return false;
            }

            IRevivable targetHealth = candidateObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived)
                return false;

            if (targetHealth.IsBeingRevived && targetHealth.CurrentReviverClientId != OwnerClientId)
                return false;

            if (!IsWithinReviveRange(candidateObj))
                return false;

            targetObj = candidateObj;
            return true;
        }

        private bool TryResolveRevivableTarget(Collider candidate, out NetworkObject targetObj)
        {
            targetObj = null;

            if (candidate == null)
                return false;

            NetworkObject candidateObj = candidate.GetComponentInParent<NetworkObject>();
            if (candidateObj == null ||
                candidateObj == NetworkObject ||
                candidateObj.OwnerClientId == OwnerClientId)
            {
                return false;
            }

            IRevivable targetHealth = candidateObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived)
                return false;

            if (!IsWithinReviveRange(candidateObj))
                return false;

            targetObj = candidateObj;
            return true;
        }

        private bool IsWithinReviveRange(NetworkObject targetObj)
        {
            if (targetObj == null)
                return false;

            float sqrDistance = (targetObj.transform.position - transform.position).sqrMagnitude;
            return sqrDistance <= reviveRange * reviveRange;
        }

        private bool IsTargetBeingRevivedByOther(NetworkObject targetObj)
        {
            if (targetObj == null)
                return false;

            if (ActiveReviversByTargetClientId.TryGetValue(targetObj.OwnerClientId, out ulong activeReviverClientId) &&
                activeReviverClientId != OwnerClientId)
            {
                return true;
            }

            IRevivable targetHealth = targetObj.GetComponent<IRevivable>();
            return targetHealth != null &&
                   targetHealth.IsBeingRevived &&
                   targetHealth.CurrentReviverClientId != OwnerClientId;
        }

        private void OnReviveStarted(ReviveStartedEvent e)
        {
            ActiveReviversByTargetClientId[e.targetClientId] = e.reviverClientId;
        }

        private void OnReviveEnded(ReviveEndedEvent e)
        {
            if (ActiveReviversByTargetClientId.TryGetValue(e.targetClientId, out ulong activeReviverClientId) &&
                activeReviverClientId == e.reviverClientId)
            {
                ActiveReviversByTargetClientId.Remove(e.targetClientId);
            }
        }

        private void PublishReviveStarted(ulong reviverClientId, ulong targetClientId)
        {
            EventBus.Publish(new ReviveStartedEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                duration = reviveDuration,
            });

            PublishReviveStartedClientRpc(reviverClientId, targetClientId, reviveDuration);
        }

        private void PublishReviveProgress(ulong reviverClientId, ulong targetClientId, float progress01)
        {
            EventBus.Publish(new ReviveProgressEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                progress01 = progress01,
            });

            PublishReviveProgressClientRpc(reviverClientId, targetClientId, progress01);
        }

        private void PublishReviveEnded(ulong reviverClientId, ulong targetClientId, ReviveResult result)
        {
            EventBus.Publish(new ReviveEndedEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                result = result,
            });

            PublishReviveEndedClientRpc(reviverClientId, targetClientId, result);
        }

        [ClientRpc]
        private void PublishReviveStartedClientRpc(ulong reviverClientId, ulong targetClientId, float duration)
        {
            if (IsServer)
                return;

            EventBus.Publish(new ReviveStartedEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                duration = duration,
            });
        }

        [ClientRpc]
        private void PublishReviveProgressClientRpc(ulong reviverClientId, ulong targetClientId, float progress01)
        {
            if (IsServer)
                return;

            EventBus.Publish(new ReviveProgressEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                progress01 = progress01,
            });
        }

        [ClientRpc]
        private void PublishReviveEndedClientRpc(ulong reviverClientId, ulong targetClientId, ReviveResult result)
        {
            if (IsServer)
                return;

            EventBus.Publish(new ReviveEndedEvent
            {
                reviverClientId = reviverClientId,
                targetClientId = targetClientId,
                result = result,
            });
        }
    }
}

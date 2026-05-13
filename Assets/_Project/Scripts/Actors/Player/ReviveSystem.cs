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
        [Header("Revive")]
        [SerializeField] private float reviveDuration = 6f;
        [SerializeField] private float reviveRange = 1.5f;
        [SerializeField] private LayerMask reviveTargetMask = ~0;
        [SerializeField] private Transform raycastOrigin;

        public NetworkVariable<float> Progress = new(0f);
        public NetworkVariable<ulong> CurrentTargetClientId = new(ulong.MaxValue);

        private NetworkObject serverTargetObj;

        public bool IsReviving => CurrentTargetClientId.Value != ulong.MaxValue;

        public void StartHold()
        {
            if (!IsOwner) return;
            BeginReviveServerRpc();
        }

        public void StopHold()
        {
            if (!IsOwner) return;
            CancelReviveServerRpc();
        }

        [ServerRpc]
        private void BeginReviveServerRpc(ServerRpcParams rpc = default)
        {
            ulong reviverId = rpc.Receive.SenderClientId;

            var origin = raycastOrigin != null ? raycastOrigin : transform;
            if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, reviveRange, reviveTargetMask))
                return;

            var targetObj = hit.collider.GetComponentInParent<NetworkObject>();
            if (targetObj == null) return;
            var targetHealth = targetObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived) return;

            serverTargetObj = targetObj;
            CurrentTargetClientId.Value = targetObj.OwnerClientId;
            Progress.Value = 0f;

            targetHealth.OnReviveBegin(reviverId);

            EventBus.Publish(new ReviveStartedEvent
            {
                reviverClientId = reviverId,
                targetClientId = targetObj.OwnerClientId,
                duration = reviveDuration,
            });
        }

        [ServerRpc]
        private void CancelReviveServerRpc(ServerRpcParams rpc = default)
        {
            EndOnServer(ReviveResult.Cancelled, rpc.Receive.SenderClientId);
        }

        private void Update()
        {
            if (!IsServer) return;
            if (!IsReviving || serverTargetObj == null) return;

            var targetHealth = serverTargetObj.GetComponent<IRevivable>();
            if (targetHealth == null || !targetHealth.CanBeRevived)
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            float dist = Vector3.Distance(transform.position, serverTargetObj.transform.position);
            if (dist > reviveRange + 0.5f)
            {
                EndOnServer(ReviveResult.Interrupted, OwnerClientId);
                return;
            }

            Progress.Value = Mathf.Min(1f, Progress.Value + Time.deltaTime / reviveDuration);

            EventBus.Publish(new ReviveProgressEvent
            {
                reviverClientId = OwnerClientId,
                targetClientId = serverTargetObj.OwnerClientId,
                progress01 = Progress.Value,
            });

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

            EventBus.Publish(new ReviveEndedEvent
            {
                reviverClientId = reviverId,
                targetClientId = targetId,
                result = result,
            });
        }
    }
}

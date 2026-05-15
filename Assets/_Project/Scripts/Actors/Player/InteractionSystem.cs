using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// Player 위치와 바라보는 방향을 기준으로 가까운 IInteractable 대상을 찾는다.
    /// 입력 권한은 Player NetworkObject Owner 기준으로 확인하고,
    /// 실제 서버 처리는 감지된 대상의 OnInteract 흐름에 맡긴다.
    /// </summary>
    public class InteractionSystem : MonoBehaviour
    {
        [Header("====상호작용 범위====")]
        [Tooltip("Player 위치를 중심으로 상호작용 후보를 찾는 반경입니다.")]
        [SerializeField, Min(0.1f)] private float interactRadius = 3f;

        [Tooltip("Player 전방 기준으로 상호작용을 허용할 각도입니다. 360이면 주변 전체를 허용합니다.")]
        [SerializeField, Range(1f, 360f)] private float interactAngle = 360f;

        [Tooltip("상호작용 후보로 감지할 레이어입니다. 컨테이너는 ItemBox 레이어를 사용합니다.")]
        [SerializeField] private LayerMask interactMask = ~0;

        [Tooltip("Trigger Collider를 상호작용 후보에 포함할지 여부입니다.")]
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

        [Header("====디버그====")]
        [Tooltip("상호작용 후보 선택 과정을 Console에 출력합니다.")]
        [SerializeField] private bool enableDebugLogs;

        [Tooltip("Scene View에서 상호작용 반경과 전방 범위를 표시합니다.")]
        [SerializeField] private bool drawDebugGizmos;

        private NetworkObject playerNetworkObject;
        private Camera interactionCamera;

        private void Awake()
        {
            playerNetworkObject = GetComponentInParent<NetworkObject>();
        }

        /// <summary>
        /// 기존 PlayerInputController의 카메라 전달 흐름과 호환하기 위한 진입점이다.
        /// 대상 판정은 Player 위치와 방향을 기준으로 수행한다.
        /// </summary>
        public void SetInteractionCamera(Camera camera)
        {
            interactionCamera = camera;
        }

        public void TryInteract()
        {
            if (playerNetworkObject == null)
            {
                LogDebug("Player NetworkObject를 찾지 못해 상호작용을 중단합니다.");
                return;
            }

            if (playerNetworkObject.IsSpawned && !playerNetworkObject.IsOwner)
            {
                LogDebug("Owner가 아닌 Player의 상호작용 입력은 무시합니다.");
                return;
            }

            if (!TryFindBestInteractable(
                    out IInteractable interactable,
                    out Collider selectedCollider,
                    out float selectedDistance,
                    out float selectedAngle))
            {
                return;
            }

            string targetName = GetInteractableName(interactable, selectedCollider);
            LogDebug($"상호작용 대상 감지: {targetName} / 거리 {selectedDistance:F2} / 각도 {selectedAngle:F1}");

            if (enableDebugLogs)
            {
                Vector3 origin = transform.position;
                Vector3 targetPoint = GetCandidatePoint(selectedCollider, origin);
                Debug.DrawLine(origin, targetPoint, Color.green, 0.5f);
            }

            interactable.OnInteract(playerNetworkObject.OwnerClientId);
        }

        private bool TryFindBestInteractable(
            out IInteractable bestInteractable,
            out Collider bestCollider,
            out float bestDistance,
            out float bestAngle)
        {
            bestInteractable = null;
            bestCollider = null;
            bestDistance = float.MaxValue;
            bestAngle = float.MaxValue;

            Vector3 origin = transform.position;
            Vector3 forward = GetPlanarForward();

            Collider[] colliders = Physics.OverlapSphere(
                origin,
                interactRadius,
                interactMask,
                triggerInteraction);

            int colliderCount = 0;
            int missingInteractableCount = 0;
            int angleRejectedCount = 0;
            int bestPriority = int.MaxValue;

            foreach (Collider candidateCollider in colliders)
            {
                if (candidateCollider == null)
                    continue;

                if (candidateCollider.transform.IsChildOf(transform))
                    continue;

                colliderCount++;

                IInteractable candidate = ResolveInteractableForCollider(candidateCollider, out int candidatePriority);
                if (candidate == null)
                {
                    missingInteractableCount++;
                    continue;
                }

                Vector3 candidatePoint = GetCandidatePoint(candidateCollider, origin);
                Vector3 toCandidate = candidatePoint - origin;
                toCandidate.y = 0f;

                float distance = toCandidate.magnitude;
                if (distance > interactRadius)
                    continue;

                bool fullCircle = interactAngle >= 359.9f;
                float angle = 0f;
                if (!fullCircle && toCandidate.sqrMagnitude > 0.0001f)
                {
                    angle = Vector3.Angle(forward, toCandidate.normalized);
                }

                float halfAngle = fullCircle ? 180f : interactAngle * 0.5f;
                if (!fullCircle && angle > halfAngle)
                {
                    angleRejectedCount++;
                    continue;
                }

                if (!IsBetterCandidate(candidatePriority, angle, distance, bestPriority, bestAngle, bestDistance))
                    continue;

                bestInteractable = candidate;
                bestCollider = candidateCollider;
                bestDistance = distance;
                bestAngle = angle;
                bestPriority = candidatePriority;
            }

            if (bestInteractable != null)
                return true;

            LogNoCandidateReason(colliderCount, missingInteractableCount, angleRejectedCount);
            return false;
        }

        /// <summary>
        /// 같은 오브젝트 계층에 여러 상호작용 컴포넌트가 섞여 있어도
        /// 바닥 단일 아이템은 즉시 줍기, 상자는 루팅 UI 흐름으로 분리한다.
        /// 우선순위: LootInteractable → LootContainer → 기타 IInteractable.
        /// </summary>
        private IInteractable ResolveInteractableForCollider(Collider candidateCollider, out int priority)
        {
            priority = int.MaxValue;

            if (candidateCollider == null)
                return null;

            LootInteractable lootItem = candidateCollider.GetComponentInParent<LootInteractable>();
            if (lootItem != null)
            {
                priority = 0;
                return lootItem;
            }

            LootContainer lootContainer = candidateCollider.GetComponentInParent<LootContainer>();
            if (lootContainer != null)
            {
                priority = 1;
                return lootContainer;
            }

            IInteractable interactable = candidateCollider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                priority = 2;
                return interactable;
            }

            return null;
        }

        /// <summary>
        /// 상호작용 타입 우선순위를 먼저 비교하고, 같은 타입끼리는 캐릭터가 바라보는 대상의 각도와 거리를 비교한다.
        /// </summary>
        private bool IsBetterCandidate(
            int candidatePriority,
            float candidateAngle,
            float candidateDistance,
            int currentBestPriority,
            float currentBestAngle,
            float currentBestDistance)
        {
            const float angleTolerance = 3f;

            if (candidatePriority < currentBestPriority)
                return true;

            if (candidatePriority > currentBestPriority)
                return false;

            if (candidateAngle < currentBestAngle - angleTolerance)
                return true;

            if (Mathf.Abs(candidateAngle - currentBestAngle) <= angleTolerance &&
                candidateDistance < currentBestDistance)
            {
                return true;
            }

            return false;
        }

        private Vector3 GetPlanarForward()
        {
            Vector3 forward = interactionCamera != null ? interactionCamera.transform.forward : transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = transform.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.0001f)
                return Vector3.forward;

            return forward.normalized;
        }

        private Vector3 GetCandidatePoint(Collider candidateCollider, Vector3 origin)
        {
            Vector3 point = candidateCollider.ClosestPoint(origin);

            if ((point - origin).sqrMagnitude < 0.0001f)
            {
                point = candidateCollider.bounds.center;
            }

            return point;
        }

        private string GetInteractableName(IInteractable interactable, Collider fallbackCollider)
        {
            if (interactable is Component component)
                return component.gameObject.name;

            return fallbackCollider != null ? fallbackCollider.name : "Unknown";
        }

        private void LogNoCandidateReason(
            int colliderCount,
            int missingInteractableCount,
            int angleRejectedCount)
        {
            if (colliderCount <= 0)
            {
                LogDebug("상호작용 반경 안에 후보 Collider가 없습니다.");
                return;
            }

            if (missingInteractableCount == colliderCount)
            {
                LogDebug("상호작용 반경 안의 후보에 IInteractable이 없습니다.");
                return;
            }

            if (angleRejectedCount > 0)
            {
                LogDebug("상호작용 후보가 전방 각도 범위 밖에 있습니다.");
                return;
            }

            LogDebug("상호작용 가능한 대상을 선택하지 못했습니다.");
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogs)
                return;

            Debug.Log($"[InteractionSystem] {message}", this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos)
                return;

            Vector3 origin = transform.position;
            Vector3 forward = GetPlanarForward();

            Gizmos.color = new Color(0f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(origin, interactRadius);

            float halfAngle = interactAngle >= 359.9f ? 180f : interactAngle * 0.5f;
            Vector3 left = Quaternion.Euler(0f, -halfAngle, 0f) * forward;
            Vector3 right = Quaternion.Euler(0f, halfAngle, 0f) * forward;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + forward * interactRadius);
            Gizmos.DrawLine(origin, origin + left * interactRadius);
            Gizmos.DrawLine(origin, origin + right * interactRadius);
        }
    }

    public interface IInteractable
    {
        void OnInteract(ulong clientId);
        string GetPromptText();
    }
}

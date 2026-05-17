using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 감지 시스템입니다.
    /// 전방 시야 감지와 후방 근접 감지를 분리해 벽 뒤 대상은 제외하고, 뒤에서 접근하는 플레이어는 짧은 거리에서 인식합니다.
    /// </summary>
    public class EnemyVision : NetworkBehaviour
    {
        [Header("감지 기본값")]
        [Tooltip("SO가 없을 때 사용할 전방 시야각입니다.")]
        [SerializeField] private float fov = 110f;

        [Tooltip("SO가 없을 때 사용할 전방 감지 거리입니다.")]
        [SerializeField] private float visionRange = 30f;

        [Tooltip("시야를 막는 벽, 구조물, 장애물 레이어입니다.")]
        [SerializeField] private LayerMask obstacleMask;

        [Tooltip("플레이어를 찾을 때 사용할 레이어입니다.")]
        [SerializeField] private LayerMask playerMask;

        [Tooltip("감지 검사를 반복하는 간격입니다.")]
        [SerializeField] private float checkInterval = 0.1f;

        [Tooltip("시야 판정의 시작 위치입니다. 비워두면 적 위치에서 위쪽으로 보정합니다.")]
        [SerializeField] private Transform eyeTransform;

        [Header("후방 근접 감지")]
        [Tooltip("전방향으로 감지하는 근접 범위입니다. 뒤에서 접근하는 플레이어 감지에 사용합니다.")]
        [SerializeField] private float proximityRange = 5f;

        [Tooltip("근접 감지에도 장애물 차단 검사를 적용할지 여부입니다.")]
        [SerializeField] private bool proximityRequiresLOS = true;

        private float nextCheckTime;
        private Transform cachedTarget;
        private Transform cachedProximityTarget;
        private readonly Collider[] detectionBuffer = new Collider[16];

        /// <summary>
        /// 네트워크 스폰 시 EnemyStatsSO의 감지 값을 적용합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            ApplySOValues();
        }

        private void Start()
        {
            if (!IsSpawned) ApplySOValues();
        }

        /// <summary>
        /// SO에서 시야 값을 적용합니다.
        /// </summary>
        private void ApplySOValues()
        {
            var stats = GetComponent<EnemyStats>();
            if (stats != null && stats.StatsSO != null)
            {
                visionRange = stats.StatsSO.visionRange;
                fov = stats.StatsSO.fov;
                proximityRange = Mathf.Max(proximityRange, stats.StatsSO.preferredRangeMin * 0.5f);
            }
        }

        // ═══════════════════════════════
        //  전방 시야 (FOV)
        // ═══════════════════════════════

        /// <summary>
        /// FOV 기반 시야 감지. 정면 부채꼴 범위 내 플레이어를 탐지한다.
        /// </summary>
        /// <param name="target">감지된 플레이어 Transform입니다.</param>
        /// <returns>전방 시야로 볼 수 있는 대상이 있으면 true입니다.</returns>
        public bool TryGetVisibleTarget(out Transform target)
        {
            target = null;
            if (IsSpawned && !IsServer) return false;

            if (Time.time < nextCheckTime)
            {
                if (IsValidTarget(cachedTarget))
                {
                    target = cachedTarget;
                    return true;
                }

                cachedTarget = null;
                return false;
            }
            nextCheckTime = Time.time + checkInterval;

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                visionRange,
                detectionBuffer,
                playerMask);

            float closestDist = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = detectionBuffer[i];
                if (c == null) continue;

                Transform candidate = ResolveTargetTransform(c);
                if (!IsValidTarget(candidate))
                    continue;

                if (CanSee(candidate))
                {
                    float dist = Vector3.Distance(transform.position, candidate.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        cachedTarget = candidate;
                    }
                }
            }

            if (closestDist < float.MaxValue)
            {
                target = cachedTarget;
                return true;
            }

            cachedTarget = null;
            return false;
        }

        /// <summary>
        /// 특정 대상이 시야 내에 있는지 확인한다.
        /// </summary>
        /// <param name="candidate">검사할 대상 Transform입니다.</param>
        /// <returns>전방 시야로 볼 수 있으면 true입니다.</returns>
        public bool CanSee(Transform candidate)
        {
            if (candidate == null) return false;
            if (!IsValidTarget(candidate)) return false;

            Vector3 origin = GetEyePosition();
            Vector3 dir = candidate.position - origin;
            float dist = dir.magnitude;

            // 거리 체크
            if (dist > visionRange) return false;

            // FOV 각도 체크
            if (Vector3.Angle(transform.forward, dir) > fov * 0.5f) return false;

            return HasLineOfSight(origin, candidate.position, dist);
        }

        // ═══════════════════════════════
        //  후방 근접 감지 (Proximity)
        // ═══════════════════════════════

        /// <summary>
        /// 전방향 근접 감지. 짧은 거리 내 플레이어를 FOV 무시하고 탐지한다.
        /// 뒤에서 접근하는 플레이어를 감지하는 용도.
        /// </summary>
        /// <param name="target">감지된 플레이어 Transform입니다.</param>
        /// <returns>후방 또는 근접 대상이 있으면 true입니다.</returns>
        public bool TryGetProximityTarget(out Transform target)
        {
            target = null;
            if (IsSpawned && !IsServer) return false;

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                proximityRange,
                detectionBuffer,
                playerMask);

            float closestDist = float.MaxValue;
            Transform closest = null;

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = detectionBuffer[i];
                if (c == null) continue;

                Transform candidate = ResolveTargetTransform(c);
                if (!IsValidTarget(candidate))
                    continue;

                float dist = Vector3.Distance(transform.position, candidate.position);

                // 이미 전방 FOV에서 감지 가능한 대상은 스킵 (중복 방지)
                if (CanSee(candidate))
                    continue;

                // 장애물 체크 (옵션)
                if (proximityRequiresLOS)
                {
                    Vector3 origin = GetEyePosition();
                    Vector3 dir = candidate.position - origin;
                    if (!HasLineOfSight(origin, candidate.position, dir.magnitude))
                        continue;
                }

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = candidate;
                }
            }

            if (closest != null)
            {
                cachedProximityTarget = closest;
                target = closest;
                return true;
            }

            cachedProximityTarget = null;
            return false;
        }

        // ───────── 유틸 ─────────

        private Vector3 GetEyePosition()
        {
            return eyeTransform != null
                ? eyeTransform.position
                : transform.position + Vector3.up * 1.6f;
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 targetPosition, float distance)
        {
            if (distance <= 0.01f) return true;

            Vector3 direction = (targetPosition - origin).normalized;
            return !Physics.Raycast(origin, direction, distance, obstacleMask);
        }

        private Transform ResolveTargetTransform(Collider targetCollider)
        {
            NetworkObject networkObject = targetCollider.GetComponentInParent<NetworkObject>();
            if (networkObject != null)
                return networkObject.transform;

            return targetCollider.attachedRigidbody != null
                ? targetCollider.attachedRigidbody.transform
                : targetCollider.transform;
        }

        private static bool IsValidTarget(Transform candidate)
        {
            if (candidate == null)
                return false;

            PlayerHealthSystem health = candidate.GetComponentInParent<PlayerHealthSystem>();
            return health != null && health.IsAlive;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 pos = transform.position + Vector3.up * 1.6f;

            // 시야 범위 (녹색)
            Gizmos.color = new Color(0f, 1f, 0f, 0.1f);
            Gizmos.DrawWireSphere(pos, visionRange > 0 ? visionRange : 30f);

            // 후방 근접 범위 (주황)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(pos, proximityRange);

            // FOV 시야각 (녹색 선)
            float halfFov = (fov > 0 ? fov : 110f) * 0.5f;
            Gizmos.color = Color.green;
            Vector3 leftDir = Quaternion.Euler(0, -halfFov, 0) * transform.forward;
            Vector3 rightDir = Quaternion.Euler(0, halfFov, 0) * transform.forward;
            float drawRange = visionRange > 0 ? visionRange : 30f;
            Gizmos.DrawLine(pos, pos + leftDir * drawRange);
            Gizmos.DrawLine(pos, pos + rightDir * drawRange);
        }
#endif
    }
}

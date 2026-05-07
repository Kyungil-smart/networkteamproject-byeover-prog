using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 감지 시스템. 두 가지 감지 모드를 제공한다:
    /// 1. 전방 시야 (FOV): SO의 visionRange/fov로 정면 감지
    /// 2. 후방 근접 (Proximity): 짧은 거리 내 전방향 감지 (소리/기배)
    /// </summary>
    public class EnemyVision : NetworkBehaviour
    {
        [Header("Inspector 기본값 (SO 있으면 SO 값으로 덮어씀)")]
        [SerializeField] private float fov = 110f;
        [SerializeField] private float visionRange = 30f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private LayerMask playerMask;
        [SerializeField] private float checkInterval = 0.1f;
        [SerializeField] private Transform eyeTransform;

        [Header("후방 근접 감지")]
        [Tooltip("전방향으로 감지하는 근접 범위 (뒤에서 다가오는 플레이어 감지용)")]
        [SerializeField] private float proximityRange = 5f;

        [Tooltip("근접 감지에도 장애물 체크를 할지 여부")]
        [SerializeField] private bool proximityRequiresLOS = true;

        private float nextCheckTime;
        private Transform cachedTarget;
        private Transform cachedProximityTarget;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            ApplySOValues();
        }

        private void Start()
        {
            if (!IsSpawned) ApplySOValues();
        }

        /// <summary>SO에서 시야 값 적용</summary>
        private void ApplySOValues()
        {
            var stats = GetComponent<EnemyStats>();
            if (stats != null && stats.StatsSO != null)
            {
                visionRange = stats.StatsSO.visionRange;
                fov = stats.StatsSO.fov;
            }
        }

        // ═══════════════════════════════
        //  전방 시야 (FOV)
        // ═══════════════════════════════

        /// <summary>
        /// FOV 기반 시야 감지. 정면 부채꼴 범위 내 플레이어를 탐지한다.
        /// </summary>
        public bool TryGetVisibleTarget(out Transform target)
        {
            target = null;
            if (IsSpawned && !IsServer) return false;

            if (Time.time < nextCheckTime)
            {
                target = cachedTarget;
                return cachedTarget != null;
            }
            nextCheckTime = Time.time + checkInterval;

            Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, playerMask);
            float closestDist = float.MaxValue;

            foreach (var c in hits)
            {
                if (CanSee(c.transform))
                {
                    float dist = Vector3.Distance(transform.position, c.transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        cachedTarget = c.transform;
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
        public bool CanSee(Transform candidate)
        {
            if (candidate == null) return false;

            Vector3 origin = GetEyePosition();
            Vector3 dir = candidate.position - origin;
            float dist = dir.magnitude;

            // 거리 체크
            if (dist > visionRange) return false;

            // FOV 각도 체크
            if (Vector3.Angle(transform.forward, dir) > fov * 0.5f) return false;

            // 장애물 체크
            if (Physics.Raycast(origin, dir.normalized, out _, dist, obstacleMask))
                return false;

            return true;
        }

        // ═══════════════════════════════
        //  후방 근접 감지 (Proximity)
        // ═══════════════════════════════

        /// <summary>
        /// 전방향 근접 감지. 짧은 거리 내 플레이어를 FOV 무시하고 탐지한다.
        /// 뒤에서 접근하는 플레이어를 감지하는 용도.
        /// </summary>
        public bool TryGetProximityTarget(out Transform target)
        {
            target = null;
            if (IsSpawned && !IsServer) return false;

            Collider[] hits = Physics.OverlapSphere(transform.position, proximityRange, playerMask);

            float closestDist = float.MaxValue;
            Transform closest = null;

            foreach (var c in hits)
            {
                float dist = Vector3.Distance(transform.position, c.transform.position);

                // 이미 전방 FOV에서 감지 가능한 대상은 스킵 (중복 방지)
                if (Vector3.Angle(transform.forward, c.transform.position - transform.position) <= fov * 0.5f)
                    continue;

                // 장애물 체크 (옵션)
                if (proximityRequiresLOS)
                {
                    Vector3 origin = GetEyePosition();
                    Vector3 dir = c.transform.position - origin;
                    if (Physics.Raycast(origin, dir.normalized, out _, dir.magnitude, obstacleMask))
                        continue;
                }

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = c.transform;
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
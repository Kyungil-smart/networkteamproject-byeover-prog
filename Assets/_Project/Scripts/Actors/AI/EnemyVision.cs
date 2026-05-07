using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    public class EnemyVision : NetworkBehaviour
    {
        [Header("Inspector 기본값 (SO 있으면 SO 값으로 덮어씀)")]
        [SerializeField] private float fov = 110f;
        [SerializeField] private float visionRange = 30f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private LayerMask playerMask;
        [SerializeField] private float checkInterval = 0.1f;
        [SerializeField] private Transform eyeTransform;

        private float nextCheckTime;
        private Transform cachedTarget;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // SO에서 visionRange, fov 읽기
            var stats = GetComponent<EnemyStats>();
            if (stats != null && stats.StatsSO != null)
            {
                visionRange = stats.StatsSO.visionRange;
                fov = stats.StatsSO.fov;
            }
        }

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
            foreach (var c in hits)
            {
                if (CanSee(c.transform))
                {
                    cachedTarget = c.transform;
                    target = cachedTarget;
                    return true;
                }
            }
            cachedTarget = null;
            return false;
        }

        public bool CanSee(Transform candidate)
        {
            if (candidate == null) return false;
            Vector3 origin = eyeTransform != null
                ? eyeTransform.position
                : transform.position + Vector3.up * 1.6f;
            Vector3 dir = candidate.position - origin;
            float dist = dir.magnitude;
            if (dist > visionRange) return false;
            if (Vector3.Angle(transform.forward, dir) > fov * 0.5f) return false;

            if (Physics.Raycast(origin, dir.normalized, out _, dist, obstacleMask))
                return false;
            return true;
        }
    }
}
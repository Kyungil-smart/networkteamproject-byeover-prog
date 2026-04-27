using Unity.Netcode;
using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// v1.1 §7.2 — obstacleMask는 Door + Environment + ViewBlocker 레이어를 포함해야 한다.
    /// Door BoxCollider가 활성화(닫힘)되면 Raycast를 막아서 AI가 플레이어를 볼 수 없다.
    /// </summary>
    public class EnemyVision : NetworkBehaviour
    {
        [SerializeField] private float fov = 110f;
        [SerializeField] private float visionRange = 30f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private LayerMask playerMask;
        [SerializeField] private float checkInterval = 0.1f;
        [SerializeField] private Transform eyeTransform;

        private float nextCheckTime;
        private Transform cachedTarget;

        public bool TryGetVisibleTarget(out Transform target)
        {
            target = null;
            if (!IsServer) return false;
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
            Vector3 origin = eyeTransform != null ? eyeTransform.position : transform.position + Vector3.up * 1.6f;
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

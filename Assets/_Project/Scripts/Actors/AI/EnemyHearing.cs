using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// v2.0 — hearingRange를 SO에서 읽어 티어별 자동 적용.
    /// </summary>
    public class EnemyHearing : NetworkBehaviour
    {
        [Header("Inspector 기본값 (SO 있으면 SO 값으로 덮어씀)")]
        [SerializeField] private float hearingRange = 20f;
        private EnemyAI ai;

        private void Awake() { ai = GetComponent<EnemyAI>(); }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // SO에서 hearingRange 읽기
            var stats = GetComponent<EnemyStats>();
            if (stats != null && stats.StatsSO != null)
                hearingRange = stats.StatsSO.hearingRange;

            EventBus.Subscribe<WeaponFiredEvent>(OnSound);
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnSound);
        }

        private void OnSound(WeaponFiredEvent e)
        {
            if (!IsServer) return;
            float dist = Vector3.Distance(transform.position, e.origin);
            if (dist > hearingRange * e.loudness) return;
            if (ai != null && ai.State.Value == AIState.Patrol)
            {
                transform.LookAt(new Vector3(e.origin.x, transform.position.y, e.origin.z));
            }
        }
    }
}
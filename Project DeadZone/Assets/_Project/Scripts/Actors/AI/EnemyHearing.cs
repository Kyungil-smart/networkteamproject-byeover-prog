using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    public class EnemyHearing : NetworkBehaviour
    {
        [SerializeField] private float hearingRange = 20f;
        private EnemyAI ai;

        private void Awake() { ai = GetComponent<EnemyAI>(); }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
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
                ai.State.Value = AIState.Alert;
            }
        }
    }
}

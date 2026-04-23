using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;


namespace DeadZone.Actors
{
    public enum AIState : byte { Patrol, Alert, Engage }

    /// <summary>
    /// 서버 전용 AI 컨트롤러. FSM을 소유하고 서버에서 매 프레임 틱을 돌린다.
    /// </summary>
    public class EnemyAI : NetworkBehaviour
    {
        [Header("Patrol")]
        [SerializeField] private Transform[] patrolPoints;

        public NetworkVariable<AIState> State = new(AIState.Patrol);

        private NavMeshAgent agent;
        private EnemyStats stats;
        private EnemyVision vision;
        private EnemyShooter shooter;

        private int patrolIndex;
        private Transform currentTarget;
        private float lastSeenTime;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            stats = GetComponent<EnemyStats>();
            vision = GetComponent<EnemyVision>();
            shooter = GetComponent<EnemyShooter>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && stats != null && stats.StatsSO != null)
            {
                if (agent != null) agent.speed = stats.StatsSO.moveSpeed;
            }
        }

        private void Update()
        {
            if (!IsServer || stats == null || stats.IsDead) return;

            switch (State.Value)
            {
                case AIState.Patrol: TickPatrol(); break;
                case AIState.Alert:  TickAlert();  break;
                case AIState.Engage: TickEngage(); break;
            }
        }

        private void TickPatrol()
        {
            if (vision != null && vision.TryGetVisibleTarget(out currentTarget))
            {
                State.Value = AIState.Engage;
                lastSeenTime = Time.time;
                return;
            }

            if (patrolPoints == null || patrolPoints.Length == 0) return;
            if (agent != null && !agent.pathPending && agent.remainingDistance < 0.5f)
            {
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
                agent.SetDestination(patrolPoints[patrolIndex].position);
            }
        }

        private void TickAlert()
        {
            if (vision != null && vision.TryGetVisibleTarget(out currentTarget))
            {
                State.Value = AIState.Engage;
                lastSeenTime = Time.time;
                return;
            }
            if (Time.time - lastSeenTime > 8f) State.Value = AIState.Patrol;
        }

        private void TickEngage()
        {
            if (currentTarget == null)
            {
                State.Value = AIState.Alert;
                return;
            }
            if (vision != null && !vision.CanSee(currentTarget))
            {
                State.Value = AIState.Alert;
                lastSeenTime = Time.time;
                return;
            }

            agent?.SetDestination(currentTarget.position);
            shooter?.TryFireAt(currentTarget);
        }
    }
}

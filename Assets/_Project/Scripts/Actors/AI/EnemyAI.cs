using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 AI 상태 열거형
    /// </summary>
    public enum AIState : byte { Patrol, Engage }
    public class EnemyAI : NetworkBehaviour
    {
        [Header("순찰")]
        [Tooltip("스폰 위치 기준 배회 반경 (보스는 무시)")]
        [SerializeField] private float wanderRadius = 5f;

        [Tooltip("배회 대기 시간 범위")]
        [SerializeField] private Vector2 wanderWait = new(2f, 5f);

        [Header("추적")]
        [Tooltip("스폰 위치에서 이 거리 초과 시 추적 포기 + 복귀")]
        [SerializeField] private float maxChaseDistance = 40f;

        [Tooltip("시야를 잃은 후 마지막 위치에서 탐색하는 시간")]
        [SerializeField] private float searchDuration = 5f;

        /// <summary>현재 AI 상태</summary>
        public NetworkVariable<AIState> State = new(AIState.Patrol);

        // ── 컴포넌트 ──
        private NavMeshAgent agent;
        private EnemyStats stats;
        private EnemyVision vision;
        private EnemyShooter shooter;

        // ── 런타임 ──
        private Vector3 spawnPosition;
        private Transform currentTarget;
        private Vector3 lastKnownPos;
        private bool isBoss;
        private float preferredRangeMin;
        private float preferredRangeMax;

        // 순찰
        private float wanderTimer;
        private float wanderWaitDuration;
        private bool isWanderWaiting;
        private bool hasWanderDest;

        // 추적
        private float lastSeenTime;
        private bool canSeeTarget;
        private float repathTimer; // 경로 재계산 간격

        // ───────── 초기화 ─────────

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            stats = GetComponent<EnemyStats>();
            vision = GetComponent<EnemyVision>();
            shooter = GetComponent<EnemyShooter>();
            spawnPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            spawnPosition = transform.position;
            CacheSOData();
        }

        private void Start()
        {
            if (!IsSpawned) CacheSOData();
            StartWanderWait(Random.Range(0.5f, 1.5f));
        }

        private void CacheSOData()
        {
            if (stats == null || stats.StatsSO == null) return;
            var so = stats.StatsSO;
            isBoss = so.isBoss;
            preferredRangeMin = so.preferredRangeMin;
            preferredRangeMax = so.preferredRangeMax;
            if (agent != null) agent.speed = so.moveSpeed;
        }

        // ───────── 메인 루프 ─────────

        private void Update()
        {
            if (IsSpawned && !IsServer) return;
            if (stats == null || stats.IsDead) return;

            switch (State.Value)
            {
                case AIState.Patrol: TickPatrol(); break;
                case AIState.Engage: TickEngage(); break;
            }
        }

        // ═══════════════════════════════
        //  PATROL — 배회 / 대기
        // ═══════════════════════════════

        private void TickPatrol()
        {
            // 감지 체크 (시야 + 후방 근접)
            if (TryDetect(out Transform target))
            {
                EnterEngage(target);
                return;
            }

            if (isBoss) return; // 보스는 제자리

            // 배회 로직
            if (isWanderWaiting)
            {
                wanderTimer += Time.deltaTime;
                if (wanderTimer >= wanderWaitDuration)
                {
                    isWanderWaiting = false;
                    SetWanderDestination();
                }
            }
            else if (hasWanderDest)
            {
                if (!agent.pathPending && agent.remainingDistance < 0.5f)
                {
                    hasWanderDest = false;
                    StartWanderWait(Random.Range(wanderWait.x, wanderWait.y));
                }
            }
        }

        private void StartWanderWait(float duration)
        {
            isWanderWaiting = true;
            wanderTimer = 0f;
            wanderWaitDuration = duration;
        }

        private void SetWanderDestination()
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 rnd = spawnPosition + Random.insideUnitSphere * wanderRadius;
                rnd.y = spawnPosition.y;

                if (NavMesh.SamplePosition(rnd, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    SetDestinationSafe(hit.position);
                    hasWanderDest = true;
                    return;
                }
            }
            hasWanderDest = false;
            StartWanderWait(1f);
        }

        // ═══════════════════════════════
        //  ENGAGE — 추적 + 사격
        // ═══════════════════════════════

        private void TickEngage()
        {
            // 타겟 소실 (접속 끊김 등)
            if (currentTarget == null)
            {
                ReturnToPatrol();
                return;
            }

            // 스폰에서 너무 멀어졌으면 복귀
            if (Vector3.Distance(transform.position, spawnPosition) > maxChaseDistance)
            {
                ReturnToPatrol();
                return;
            }

            // 시야 확인
            canSeeTarget = vision != null && vision.CanSee(currentTarget);

            if (canSeeTarget)
            {
                // ── 타겟 보임 ──
                lastKnownPos = currentTarget.position;
                lastSeenTime = Time.time;

                float dist = Vector3.Distance(transform.position, currentTarget.position);

                // 거리 유지 이동
                if (dist > preferredRangeMax)
                {
                    // 접근 — 주기적 경로 갱신
                    repathTimer += Time.deltaTime;
                    if (repathTimer > 0.3f)
                    {
                        repathTimer = 0f;
                        SetDestinationSafe(currentTarget.position);
                    }
                    agent.isStopped = false;
                }
                else if (dist < preferredRangeMin)
                {
                    // 후퇴
                    TryRetreat();
                    agent.isStopped = false;
                }
                else
                {
                    // 적정 거리 — 정지
                    agent.isStopped = true;
                }

                // 바라보기 + 사격
                LookAt(currentTarget.position);
                if (dist <= preferredRangeMax)
                    shooter?.TryFireAt(currentTarget);
            }
            else
            {
                // ── 타겟 안 보임 → 마지막 위치로 추적 ──
                agent.isStopped = false;

                repathTimer += Time.deltaTime;
                if (repathTimer > 0.5f)
                {
                    repathTimer = 0f;
                    SetDestinationSafe(lastKnownPos);
                }

                // 마지막 위치 도착 체크
                if (!agent.pathPending && agent.remainingDistance < 1.5f)
                {
                    // 도착했는데 안 보임 → 탐색 시간 초과 시 복귀
                    if (Time.time - lastSeenTime > searchDuration)
                    {
                        ReturnToPatrol();
                    }
                }

                // 추적 중에도 다시 보이면 갱신
                if (TryDetect(out Transform newTarget))
                {
                    currentTarget = newTarget;
                    lastKnownPos = newTarget.position;
                    lastSeenTime = Time.time;
                }
            }
        }

        // ───────── 유틸리티 ─────────

        /// <summary>시야(전방) + 근접(후방) 감지</summary>
        private bool TryDetect(out Transform target)
        {
            target = null;
            if (vision == null) return false;

            // 전방 시야
            if (vision.TryGetVisibleTarget(out target))
                return true;

            // 후방 근접 감지 (소리/기배 느끼는 개념)
            if (vision.TryGetProximityTarget(out target))
                return true;

            return false;
        }

        private void EnterEngage(Transform target)
        {
            currentTarget = target;
            lastKnownPos = target.position;
            lastSeenTime = Time.time;
            repathTimer = 0f;

            if (IsSpawned)
                State.Value = AIState.Engage;
        }

        private void ReturnToPatrol()
        {
            currentTarget = null;

            if (IsSpawned)
                State.Value = AIState.Patrol;

            agent.isStopped = false;
            SetDestinationSafe(spawnPosition);
            hasWanderDest = true; // 스폰 위치 도착 후 배회 재개
        }

        private void TryRetreat()
        {
            Vector3 dir = (transform.position - currentTarget.position).normalized;
            Vector3 retreatPos = transform.position + dir * 3f;

            if (NavMesh.SamplePosition(retreatPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                SetDestinationSafe(hit.position);
        }

        /// <summary>
        /// NavMesh 경로가 유효할 때만 목적지를 설정한다.
        /// 도달 불가능한 목적지로 인한 벽 비빔을 방지한다.
        /// </summary>
        private void SetDestinationSafe(Vector3 dest)
        {
            if (agent == null || !agent.isOnNavMesh) return;

            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(dest, path))
            {
                if (path.status == NavMeshPathStatus.PathComplete ||
                    path.status == NavMeshPathStatus.PathPartial)
                {
                    agent.SetPath(path);
                }
            }
        }

        private void LookAt(Vector3 pos)
        {
            Vector3 dir = pos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 8f);
        }
    }
}
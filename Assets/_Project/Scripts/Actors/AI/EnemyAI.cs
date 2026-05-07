using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 AI 상태 열거형
    /// </summary>
    public enum AIState : byte { Patrol, Alert, Engage }

    /// <summary>
    /// 서버 전용 AI 컨트롤러.
    /// - 순찰: 스폰 위치 기준 소규모 랜덤 배회 (보스는 제자리 대기)
    /// - 교전: preferredRange 거리를 유지하며 사격
    /// - 추적: 시야를 잃어도 마지막 목격 위치까지 추적
    /// - 복귀: 스폰 위치에서 maxChaseDistance 초과 시 추적 포기
    /// </summary>
    public class EnemyAI : NetworkBehaviour
    {
        [Header("순찰 — 웨이포인트 방식 (선택)")]
        [Tooltip("지정된 위치를 순서대로 순찰. 비어있으면 랜덤 배회 모드")]
        [SerializeField] private Transform[] patrolPoints;

        [Header("순찰 — 랜덤 배회 설정")]
        [Tooltip("스폰 위치 기준 배회 반경")]
        [SerializeField] private float wanderRadius = 5f;

        [Tooltip("배회 목적지 도착 후 대기 시간 (최소)")]
        [SerializeField] private float wanderWaitMin = 2f;

        [Tooltip("배회 목적지 도착 후 대기 시간 (최대)")]
        [SerializeField] private float wanderWaitMax = 5f;

        [Header("추적 제한")]
        [Tooltip("스폰 위치에서 이 거리 이상 멀어지면 추적을 포기하고 복귀")]
        [SerializeField] private float maxChaseDistance = 40f;

        [Header("경계 → 복귀")]
        [Tooltip("Alert 상태에서 타겟을 찾지 못하면 Patrol로 돌아가는 시간")]
        [SerializeField] private float alertTimeout = 8f;

        /// <summary>현재 AI 상태 (네트워크 동기화)</summary>
        public NetworkVariable<AIState> State = new(AIState.Patrol);

        // ───────── 컴포넌트 캐시 ─────────

        private NavMeshAgent agent;
        private EnemyStats stats;
        private EnemyVision vision;
        private EnemyShooter shooter;

        // ───────── 런타임 상태 ─────────

        private int patrolIndex;
        private Transform currentTarget;
        private float lastSeenTime;
        private Vector3 lastKnownTargetPos;
        private bool hasReachedLastKnownPos;

        // 랜덤 배회용
        private Vector3 spawnPosition;
        private float wanderWaitTimer;
        private float wanderWaitDuration;
        private bool isWaiting;
        private bool justSetDestination; // 목적지 설정 직후 플래그

        // SO 캐시
        private bool isBoss;
        private float preferredRangeMin;
        private float preferredRangeMax;

        // ───────── 라이프사이클 ─────────

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

        /// <summary>SO 데이터를 로컬 변수에 캐시</summary>
        private void CacheSOData()
        {
            if (stats == null || stats.StatsSO == null) return;

            var so = stats.StatsSO;
            isBoss = so.isBoss;
            preferredRangeMin = so.preferredRangeMin;
            preferredRangeMax = so.preferredRangeMax;

            if (agent != null)
                agent.speed = so.moveSpeed;
        }

        private void Start()
        {
            if (!IsSpawned)
                CacheSOData();

            // 첫 배회 목적지 설정 (보스 아닌 경우)
            if (!isBoss && (patrolPoints == null || patrolPoints.Length == 0))
            {
                isWaiting = true;
                wanderWaitTimer = 0f;
                wanderWaitDuration = Random.Range(0.5f, 1.5f); // 스폰 직후 짧은 대기
            }
        }

        private void Update()
        {
            if (IsSpawned && !IsServer) return;
            if (stats == null || stats.IsDead) return;

            switch (State.Value)
            {
                case AIState.Patrol: TickPatrol(); break;
                case AIState.Alert:  TickAlert();  break;
                case AIState.Engage: TickEngage(); break;
            }
        }

        // ═══════════════════════════════════════
        //  Patrol 상태
        // ═══════════════════════════════════════

        /// <summary>
        /// 순찰 상태.
        /// 보스: 제자리 대기. 일반 적: 배회. 플레이어 발견 시 Engage.
        /// </summary>
        private void TickPatrol()
        {
            // 플레이어 감지 → Engage
            if (vision != null && vision.TryGetVisibleTarget(out currentTarget))
            {
                EnterEngage(currentTarget);
                return;
            }

            // 보스는 제자리 대기
            if (isBoss) return;

            // 웨이포인트 모드
            if (patrolPoints != null && patrolPoints.Length > 0)
                PatrolWaypoints();
            // 랜덤 배회 모드
            else
                PatrolWander();
        }

        /// <summary>웨이포인트 순회</summary>
        private void PatrolWaypoints()
        {
            if (agent == null || agent.pathPending) return;

            if (agent.remainingDistance < 0.5f)
            {
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
                agent.SetDestination(patrolPoints[patrolIndex].position);
            }
        }

        /// <summary>스폰 위치 기준 랜덤 배회 (반복 동작)</summary>
        private void PatrolWander()
        {
            if (agent == null) return;

            // 대기 중 → 타이머 진행
            if (isWaiting)
            {
                wanderWaitTimer += Time.deltaTime;
                if (wanderWaitTimer >= wanderWaitDuration)
                {
                    isWaiting = false;
                    SetRandomWanderDestination();
                    justSetDestination = true;
                }
                return;
            }

            // 목적지 설정 직후 → 경로 계산 대기 (1프레임 스킵)
            if (justSetDestination)
            {
                if (!agent.pathPending)
                    justSetDestination = false;
                return;
            }

            // 이동 중 → 도착 체크
            if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                isWaiting = true;
                wanderWaitTimer = 0f;
                wanderWaitDuration = Random.Range(wanderWaitMin, wanderWaitMax);
            }
        }

        /// <summary>NavMesh 위 유효한 랜덤 지점 설정</summary>
        private void SetRandomWanderDestination()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
                randomDir.y = 0f;
                Vector3 candidate = spawnPosition + randomDir;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.isStopped = false;
                    agent.SetDestination(hit.position);
                    return;
                }
            }

            agent.isStopped = false;
            agent.SetDestination(spawnPosition);
        }

        // ═══════════════════════════════════════
        //  Alert 상태
        // ═══════════════════════════════════════

        /// <summary>
        /// 경계 상태. 마지막 목격 위치를 주시하며 재발견 대기.
        /// 타임아웃 시 Patrol 복귀.
        /// </summary>
        private void TickAlert()
        {
            // 플레이어 재발견 → Engage
            if (vision != null && vision.TryGetVisibleTarget(out currentTarget))
            {
                EnterEngage(currentTarget);
                return;
            }

            // 타임아웃 → 복귀
            if (Time.time - lastSeenTime > alertTimeout)
            {
                ReturnToPatrol();
            }
        }

        // ═══════════════════════════════════════
        //  Engage 상태
        // ═══════════════════════════════════════

        /// <summary>
        /// 교전 상태.
        /// - 시야 내: preferredRange 거리 유지 + 사격
        /// - 시야 잃음: 마지막 목격 위치까지 추적 (길찾기)
        /// - 스폰에서 너무 멀어짐: 추적 포기 + 복귀
        /// </summary>
        private void TickEngage()
        {
            // 타겟 오브젝트 소실 (접속 끊김 등)
            if (currentTarget == null)
            {
                State.Value = AIState.Alert;
                lastSeenTime = Time.time;
                return;
            }

            // ── 스폰 위치에서 너무 멀어졌는지 체크 ──
            float distFromSpawn = Vector3.Distance(transform.position, spawnPosition);
            if (distFromSpawn > maxChaseDistance)
            {
                ReturnToPatrol();
                return;
            }

            // ── 시야 확인 ──
            bool canSee = vision != null && vision.CanSee(currentTarget);

            if (canSee)
            {
                // 타겟 보임 → 마지막 위치 갱신
                lastKnownTargetPos = currentTarget.position;
                lastSeenTime = Time.time;
                hasReachedLastKnownPos = false;

                float distToTarget = Vector3.Distance(transform.position, currentTarget.position);

                // 거리에 따른 행동 분기
                if (distToTarget > preferredRangeMax)
                {
                    // 너무 멀다 → 접근
                    agent.isStopped = false;
                    agent.SetDestination(currentTarget.position);
                }
                else if (distToTarget < preferredRangeMin)
                {
                    // 너무 가깝다 → 후퇴
                    TryRetreat();
                }
                else
                {
                    // 적정 거리 → 정지 + 사격
                    agent.isStopped = true;
                }

                // 타겟 바라보기 + 사격
                LookAtTarget(currentTarget.position);
                if (distToTarget <= preferredRangeMax)
                    shooter?.TryFireAt(currentTarget);
            }
            else
            {
                // 시야 잃음 → 마지막 목격 위치까지 추적 (길찾기)
                agent.isStopped = false;
                agent.SetDestination(lastKnownTargetPos);

                float distToLastKnown = Vector3.Distance(transform.position, lastKnownTargetPos);

                if (distToLastKnown < 1.5f)
                {
                    // 마지막 목격 위치에 도착했는데 안 보임 → Alert
                    if (!hasReachedLastKnownPos)
                    {
                        hasReachedLastKnownPos = true;
                        State.Value = AIState.Alert;
                        lastSeenTime = Time.time;
                    }
                }
            }
        }

        /// <summary>후퇴 시도. NavMesh 위 유효한 후퇴 지점을 찾는다.</summary>
        private void TryRetreat()
        {
            Vector3 retreatDir = (transform.position - currentTarget.position).normalized;
            Vector3 retreatPos = transform.position + retreatDir * 3f;

            if (NavMesh.SamplePosition(retreatPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
            }
            else
            {
                // 후퇴 불가 → 제자리에서 사격
                agent.isStopped = true;
            }
        }

        // ═══════════════════════════════════════
        //  공통 유틸리티
        // ═══════════════════════════════════════

        /// <summary>Engage 상태 진입</summary>
        private void EnterEngage(Transform target)
        {
            currentTarget = target;
            lastKnownTargetPos = target.position;
            lastSeenTime = Time.time;
            hasReachedLastKnownPos = false;
            State.Value = AIState.Engage;
        }

        /// <summary>Patrol 상태로 복귀. 스폰 위치로 돌아간다.</summary>
        private void ReturnToPatrol()
        {
            currentTarget = null;
            State.Value = AIState.Patrol;
            hasReachedLastKnownPos = false;

            if (agent != null)
            {
                agent.isStopped = false;

                if (isBoss)
                {
                    // 보스는 스폰 위치로 복귀
                    agent.SetDestination(spawnPosition);
                }
                else
                {
                    // 일반 적은 스폰 위치 복귀 후 배회 재개
                    agent.SetDestination(spawnPosition);
                    isWaiting = false;
                    justSetDestination = true;
                }
            }
        }

        /// <summary>타겟 방향으로 부드럽게 회전</summary>
        private void LookAtTarget(Vector3 targetPos)
        {
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, Time.deltaTime * 8f);
        }
    }
}
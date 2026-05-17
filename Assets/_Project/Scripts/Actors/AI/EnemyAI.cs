using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

using DeadZone.Core;
using DeadZone.Systems.Audio;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적 AI 상태 열거형입니다.
    /// </summary>
    public enum AIState : byte
    {
        Patrol,
        Investigate,
        Chase,
        Combat,
        SearchCover,
        Return
    }

    /// <summary>
    /// 서버 권위 적 AI 컨트롤러입니다.
    /// 감지, 소리 조사, 추적, 교전, 복귀 상태를 분리해 NavMesh 경로 기반으로 플레이어를 추적합니다.
    /// </summary>
    public class EnemyAI : NetworkBehaviour
    {
        [Header("순찰")]
        [Tooltip("스폰 위치 기준 배회 반경입니다. 보스는 배회하지 않습니다.")]
        [SerializeField] private float wanderRadius = 5f;

        [Tooltip("배회 목적지 도착 후 대기 시간 범위입니다.")]
        [SerializeField] private Vector2 wanderWait = new(2f, 5f);

        [Header("추적 제한")]
        [Tooltip("스폰 위치에서 이 거리 초과 시 추적을 중지하고 복귀합니다.")]
        [SerializeField] private float maxChaseDistance = 40f;

        [Tooltip("시야를 잃은 뒤 마지막 위치까지 계속 추적하는 시간입니다.")]
        [SerializeField] private float lostTargetChaseDuration = 4f;

        [Tooltip("마지막 위치에 도착한 뒤 주변을 조사하는 시간입니다.")]
        [SerializeField] private float searchDuration = 5f;

        [Header("길찾기")]
        [Tooltip("NavMesh 위 목적지를 다시 계산하는 간격입니다.")]
        [SerializeField] private float repathInterval = 0.25f;

        [Tooltip("목적지 도착으로 판단할 거리입니다.")]
        [SerializeField] private float arrivalDistance = 1.2f;

        [Tooltip("목적지를 NavMesh 위로 보정할 때 사용할 검색 반경입니다.")]
        [SerializeField] private float pathSampleRadius = 4f;

        [Tooltip("후퇴할 때 목표 지점까지 벌리는 거리입니다.")]
        [SerializeField] private float retreatDistance = 3f;

        [Tooltip("후퇴 지점 후보를 검사할 횟수입니다.")]
        [SerializeField] private int retreatSampleAttempts = 7;

        [Tooltip("길찾기 중 실제 충돌로 막히는 구조물/벽 레이어입니다. NavMesh가 놓친 장애물 보정에 사용합니다.")]
        [SerializeField] private LayerMask pathObstacleMask = ~0;

        [Tooltip("장애물 막힘을 검사할 때 사용할 AI 몸 반경입니다. NavMeshAgent 반경보다 작으면 Agent 반경을 사용합니다.")]
        [SerializeField] private float pathProbeRadius = 0.45f;

        [Tooltip("장애물에 막혔을 때 좌우 우회 후보를 찾는 기본 거리입니다.")]
        [SerializeField] private float detourDistance = 3f;

        [Tooltip("거의 움직이지 못하는 상태를 검사하는 간격입니다.")]
        [SerializeField] private float stuckCheckInterval = 0.7f;

        [Tooltip("검사 간격 동안 이 거리보다 적게 움직이면 끼임으로 판단합니다.")]
        [SerializeField] private float stuckMoveThreshold = 0.15f;
        [SerializeField, Min(1)] private int hardStuckRecoveryThreshold = 3;
        [SerializeField, Min(0.5f)] private float hardStuckRecoveryRadius = 3f;

        [Header("엄폐 수색")]
        [Tooltip("시야를 잃은 뒤 엄폐물 주변을 수색하는 최대 시간입니다.")]
        [SerializeField] private float coverSearchDuration = 10f;

        [Tooltip("수색 지점에 도착한 뒤 다음 지점으로 넘어가기 전 대기 시간입니다.")]
        [SerializeField] private float searchPointWaitTime = 0.8f;

        [Tooltip("엄폐물 Bounds 바깥쪽으로 수색 후보를 벌리는 거리입니다.")]
        [SerializeField] private float coverCornerPadding = 2f;

        [Tooltip("마지막으로 본 이동 방향을 기준으로 예측 위치를 잡는 시간입니다.")]
        [SerializeField] private float predictedTargetLeadTime = 1.2f;

        [Tooltip("엄폐물 후보가 부족할 때 마지막 위치 주변을 수색하는 반경입니다.")]
        [SerializeField] private float fallbackSearchRadius = 5f;

        [Tooltip("마지막 위치 주변 원형 수색 후보 개수입니다.")]
        [SerializeField] private int fallbackSearchPointCount = 8;

        [Tooltip("한 번의 엄폐 수색에서 사용할 최대 수색 후보 개수입니다.")]
        [SerializeField] private int maxCoverSearchPoints = 10;

        [Header("스폰/NavMesh 안정화")]
        [Tooltip("스폰 지점이 NavMesh에서 살짝 벗어났을 때 주변 NavMesh를 찾는 반경입니다.")]
        [SerializeField] private float spawnNavMeshSampleRadius = 1.5f;

        [Tooltip("스폰 지점을 NavMesh로 보정할 때 허용하는 최대 이동 거리입니다. 이 값보다 멀면 강제 워프하지 않습니다.")]
        [SerializeField] private float maxSpawnWarpDistance = 1.25f;

        [Tooltip("스폰 지점 주변에 유효한 NavMesh가 없을 때 경고 로그를 출력할지 여부입니다.")]
        [SerializeField] private bool warnWhenSpawnOffNavMesh = true;

        [Tooltip("스폰 위치가 큰 NavMeshObstacle 내부에 들어간 경우 경고를 출력할지 여부입니다.")]
        [SerializeField] private bool warnWhenInsideNavMeshObstacle = true;

        [Header("교전/회피 보정")]
        [Tooltip("SO 값이 너무 작아도 플레이어에게 붙지 않도록 보장할 최소 교전 거리입니다.")]
        [SerializeField] private float minimumPersonalSpaceDistance = 6f;

        [Tooltip("SO 유효 사거리에 곱할 AI 교전 거리 배율입니다.")]
        [SerializeField, Range(0.1f, 1f)] private float combatRangeMultiplier = 0.65f;

        [Tooltip("AI가 실제 교전에 진입할 수 있는 최대 거리입니다.")]
        [SerializeField] private float maxCombatDistance = 24f;

        [Tooltip("NavMeshAgent 정지 거리가 너무 작을 때 사용할 최소 정지 거리입니다.")]
        [SerializeField] private float minimumAgentStoppingDistance = 1.2f;

        [Tooltip("AI끼리 비비는 현상을 줄이기 위해 사용할 NavMeshAgent 회피 품질입니다.")]
        [SerializeField] private ObstacleAvoidanceType obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        [Tooltip("AI끼리 같은 우선순위로 밀고 들어가지 않도록 개체별로 뽑을 회피 우선순위 범위입니다.")]
        [SerializeField] private Vector2Int avoidancePriorityRange = new(30, 70);

        [Header("오디오")]
        [Tooltip("적이 플레이어를 발각했을 때 발각음을 다시 재생할 수 있기까지 기다리는 시간입니다.")]
        [SerializeField, Min(0f)] private float alertSoundCooldown = 3f;

        [Tooltip("적 발각음 볼륨 배율입니다. AudioLibrary의 개별 볼륨과 AudioManager의 SFX 볼륨이 함께 적용됩니다.")]
        [SerializeField, Range(0f, 2f)] private float alertSoundVolumeMultiplier = 1f;

        [Tooltip("켜면 적이 NavMeshAgent로 실제 이동 중일 때 AudioManager 이벤트로 발걸음 소리를 재생합니다.")]
        [SerializeField] private bool playFootstepSound = true;

        [Tooltip("적 발걸음 소리를 다시 재생하기까지의 시간입니다.")]
        [SerializeField, Min(0.05f)] private float footstepInterval = 0.55f;

        [Tooltip("이 속도보다 느리면 적 발걸음 소리를 재생하지 않습니다.")]
        [SerializeField, Min(0f)] private float footstepMinSpeed = 0.15f;

        [Tooltip("적 발걸음 볼륨 배율입니다. AudioLibrary의 개별 볼륨과 AudioManager의 SFX 볼륨이 함께 적용됩니다.")]
        [SerializeField, Range(0f, 2f)] private float footstepVolumeMultiplier = 0.9f;

        [Header("발각 표시")]
        [Tooltip("플레이어를 처음 발각했을 때 적 머리 위에 표시할 느낌표 오브젝트입니다. 비워두면 AlertIndicator 또는 Exclamation 이름의 자식을 자동으로 찾습니다.")]
        [SerializeField] private GameObject alertIndicatorRoot;

        [SerializeField, Min(0.1f)] private float alertIndicatorDuration = 0.55f;
        [SerializeField, Min(0.01f)] private float alertIndicatorStartScale = 0.18f;
        [SerializeField, Min(0.01f)] private float alertIndicatorEndScale = 0.45f;
        [SerializeField, Min(0.1f)] private float alertIndicatorHeight = 1.8f;

        [Header("무기 표시")]
        [Tooltip("AI 상태에 따라 표시/숨김을 적용할 무기 비주얼입니다. 비워두면 자식에서 자동 검색합니다.")]
        [SerializeField] private EnemyWeaponVisual enemyWeaponVisual;

        [Header("네트워크 상태")]
        [Tooltip("현재 AI 상태입니다. 서버가 값을 변경하고 클라이언트는 읽습니다.")]
        public NetworkVariable<AIState> State = new(AIState.Patrol);

        private AIState localState = AIState.Patrol;
        private NavMeshAgent agent;
        private EnemyStats stats;
        private EnemyVision vision;
        private EnemyShooter shooter;
        private EnemyAnimHandler animHandler;

        private Vector3 spawnPosition;
        private Transform currentTarget;
        private Vector3 lastKnownPos;
        private Vector3 investigatePosition;
        private Vector3 lastDestination;
        private Vector3 previousSeenPosition;
        private Vector3 lastKnownVelocity;
        private Vector3 activeSearchPoint;
        private readonly Queue<Vector3> coverSearchQueue = new();

        private bool isBoss;
        private bool hasPreviousSeenPosition;
        private bool hasActiveSearchPoint;
        private bool warnedInsideNavMeshObstacle;
        private float preferredRangeMin;
        private float preferredRangeMax;

        private float wanderTimer;
        private float wanderWaitDuration;
        private bool isWanderWaiting;
        private bool hasWanderDestination;

        private float lastSeenTime;
        private float stateEnterTime;
        private float repathTimer;
        private float nextStuckCheckTime;
        private float lastMemoryUpdateTime;
        private float activeSearchPointEnterTime;
        private float footstepTimer;
        private Vector3 lastStuckCheckPosition;
        private int consecutiveStuckChecks;
        private bool canPlayAlertSound = true;
        private Coroutine alertSoundCooldownRoutine;
        private Coroutine alertIndicatorRoutine;
        private bool subscribedToStateChanges;

        private bool CanUseNetworkState => IsSpawned && NetworkObject != null && NetworkObject.IsSpawned;
        private AIState CurrentState => CanUseNetworkState ? State.Value : localState;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            stats = GetComponent<EnemyStats>();
            vision = GetComponent<EnemyVision>();
            shooter = GetComponent<EnemyShooter>();
            animHandler = GetComponent<EnemyAnimHandler>();
            ResolveEnemyWeaponVisual();
            spawnPosition = transform.position;
            lastDestination = transform.position;
        }

        /// <summary>
        /// 네트워크 스폰 시 서버에서 SO 데이터를 캐시합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            ResolveEnemyWeaponVisual();
            SubscribeStateChanges();
            localState = State.Value;
            ApplyWeaponVisibilityForState(State.Value);

            if (!IsServer) return;

            ResolveSpawnPositionOnNavMesh();
            CacheSOData();
            EnterPatrol();
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeStateChanges();

            if (alertSoundCooldownRoutine != null)
            {
                StopCoroutine(alertSoundCooldownRoutine);
                alertSoundCooldownRoutine = null;
            }

            if (alertIndicatorRoutine != null)
            {
                StopCoroutine(alertIndicatorRoutine);
                alertIndicatorRoutine = null;
            }

            base.OnNetworkDespawn();
        }

        private void Start()
        {
            ResolveEnemyWeaponVisual();
            ApplyWeaponVisibilityForState(CurrentState);
            ResolveAlertIndicator();

            if (!IsSpawned)
            {
                ResolveSpawnPositionOnNavMesh();
                CacheSOData();
                EnterPatrol();
            }
        }

        private void Update()
        {
            if (IsSpawned && !IsServer) return;
            if (stats == null || stats.IsDead) return;

            switch (CurrentState)
            {
                case AIState.Patrol:
                    TickPatrol();
                    break;

                case AIState.Investigate:
                    TickInvestigate();
                    break;

                case AIState.Chase:
                    TickChase();
                    break;

                case AIState.Combat:
                    TickCombat();
                    break;

                case AIState.SearchCover:
                    TickSearchCover();
                    break;

                case AIState.Return:
                    TickReturn();
                    break;
            }

            TickFootstepAudio();
        }

        /// <summary>
        /// 소리나 외부 자극으로 확인할 위치를 전달합니다.
        /// </summary>
        /// <param name="position">조사할 월드 위치입니다.</param>
        public void ReportSuspiciousPosition(Vector3 position)
        {
            if (IsSpawned && !IsServer) return;
            if (CurrentState == AIState.Chase || CurrentState == AIState.Combat || CurrentState == AIState.SearchCover) return;
            if (IsOutsideChaseLimit(position)) return;

            if (!TryProjectToNavMesh(position, out Vector3 navPosition))
                return;

            currentTarget = null;
            investigatePosition = navPosition;
            EnterState(AIState.Investigate);
            SetAgentStopped(false);
            TrySetDestination(investigatePosition);
        }

        private void CacheSOData()
        {
            if (stats == null || stats.StatsSO == null) return;

            var so = stats.StatsSO;
            isBoss = so.isBoss;
            preferredRangeMin = Mathf.Max(so.preferredRangeMin, minimumPersonalSpaceDistance);
            preferredRangeMax = Mathf.Min(so.preferredRangeMax, so.maxEffectiveRange * combatRangeMultiplier, maxCombatDistance);
            preferredRangeMax = Mathf.Max(preferredRangeMax, preferredRangeMin + 2f);

            if (agent != null)
            {
                agent.speed = so.moveSpeed;
                pathProbeRadius = Mathf.Max(pathProbeRadius, agent.radius);
                agent.autoRepath = true;
                agent.autoBraking = true;
                agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, minimumAgentStoppingDistance);
                agent.obstacleAvoidanceType = obstacleAvoidanceType;
                int priorityMin = Mathf.Clamp(Mathf.Min(avoidancePriorityRange.x, avoidancePriorityRange.y), 0, 99);
                int priorityMax = Mathf.Clamp(Mathf.Max(avoidancePriorityRange.x, avoidancePriorityRange.y), 0, 99);
                agent.avoidancePriority = Random.Range(priorityMin, priorityMax + 1);
            }
        }

        private void ResolveSpawnPositionOnNavMesh()
        {
            if (agent == null)
            {
                WarnIfInsideNavMeshObstacle();
                spawnPosition = transform.position;
                lastDestination = transform.position;
                return;
            }

            if (TryWarpOutOfNavMeshObstacle())
                return;

            WarnIfInsideNavMeshObstacle();

            if (agent.isOnNavMesh)
            {
                spawnPosition = transform.position;
                lastDestination = transform.position;
                return;
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, spawnNavMeshSampleRadius, NavMesh.AllAreas))
            {
                float warpDistance = Vector3.Distance(transform.position, hit.position);
                if (warpDistance <= maxSpawnWarpDistance && agent.Warp(hit.position))
                {
                    spawnPosition = hit.position;
                    lastDestination = hit.position;
                    return;
                }

                if (warnWhenSpawnOffNavMesh)
                {
                    Debug.LogWarning(
                        $"[EnemyAI] {name} 스폰 위치에서 가장 가까운 NavMesh가 {warpDistance:F2}m 떨어져 있어 자동 워프하지 않았습니다. 배치 위치 또는 NavMesh/Obstacle 설정을 확인하세요.",
                        this);
                }
            }
            else if (warnWhenSpawnOffNavMesh)
            {
                Debug.LogWarning(
                    $"[EnemyAI] {name} 스폰 위치 주변 {spawnNavMeshSampleRadius:F1}m 안에서 NavMesh를 찾지 못했습니다. 적이 이동하지 못하거나 다른 NavMesh로 튕길 수 있습니다.",
                    this);
            }

            spawnPosition = transform.position;
            lastDestination = transform.position;
        }

        private bool TryWarpOutOfNavMeshObstacle()
        {
            if (agent == null)
                return false;

            NavMeshObstacle obstacle = FindContainingNavMeshObstacle();
            if (obstacle == null)
                return false;

            Vector3 obstacleCenter = obstacle.transform.TransformPoint(obstacle.center);
            float searchDistance = GetObstacleHorizontalExtent(obstacle) + Mathf.Max(agent.radius, 0.5f) + 0.75f;
            float sampleRadius = Mathf.Max(spawnNavMeshSampleRadius, searchDistance);
            Vector3 away = transform.position - obstacleCenter;
            away.y = 0f;

            if (away.sqrMagnitude < 0.01f)
                away = transform.forward.sqrMagnitude > 0.01f ? transform.forward : Vector3.forward;

            away.Normalize();

            for (int i = 0; i < 12; i++)
            {
                float angle = i == 0 ? 0f : (360f / 12f) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
                Vector3 probe = obstacleCenter + direction * searchDistance;
                probe.y = transform.position.y;

                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                    continue;

                if (IsPointInsideObstacleBounds(obstacle, hit.position))
                    continue;

                if (!agent.Warp(hit.position))
                    continue;

                warnedInsideNavMeshObstacle = true;
                spawnPosition = hit.position;
                lastDestination = hit.position;
                return true;
            }

            return false;
        }

        private void WarnIfInsideNavMeshObstacle()
        {
            if (!warnWhenInsideNavMeshObstacle || warnedInsideNavMeshObstacle)
            {
                return;
            }

            NavMeshObstacle[] obstacles = Object.FindObjectsByType<NavMeshObstacle>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < obstacles.Length; i++)
            {
                NavMeshObstacle obstacle = obstacles[i];
                if (obstacle == null || !obstacle.enabled || obstacle.transform == transform || obstacle.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!IsPointInsideObstacleBounds(obstacle, transform.position))
                {
                    continue;
                }

                warnedInsideNavMeshObstacle = true;
                Debug.LogWarning(
                    $"[EnemyAI] {name}이(가) '{obstacle.name}' NavMeshObstacle 내부에서 시작했습니다. 빈 건물 전체를 덮는 Obstacle은 실내 NavMeshAgent를 밖으로 밀거나 이동을 막을 수 있습니다. 창고 부모 Obstacle을 제거하고 벽/기둥/소품 단위로 분리하세요.",
                    this);
                return;
            }

            CreateDefaultAlertIndicator();
        }

        private void CreateDefaultAlertIndicator()
        {
            GameObject indicator = new("AlertIndicator");
            indicator.transform.SetParent(transform, false);
            indicator.transform.localPosition = Vector3.up * Mathf.Max(0.1f, alertIndicatorHeight);

            TextMesh textMesh = indicator.AddComponent<TextMesh>();
            textMesh.text = "!";
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.32f;
            textMesh.fontSize = 96;
            textMesh.color = new Color(1f, 0.05f, 0.03f, 1f);

            MeshRenderer renderer = indicator.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sortingOrder = 100;

            alertIndicatorRoot = indicator;
            alertIndicatorRoot.SetActive(false);
        }

        private NavMeshObstacle FindContainingNavMeshObstacle()
        {
            NavMeshObstacle[] obstacles = Object.FindObjectsByType<NavMeshObstacle>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < obstacles.Length; i++)
            {
                NavMeshObstacle obstacle = obstacles[i];
                if (obstacle == null || !obstacle.enabled || obstacle.transform == transform || obstacle.transform.IsChildOf(transform))
                    continue;

                if (IsPointInsideObstacleBounds(obstacle, transform.position))
                    return obstacle;
            }

            return null;
        }

        private float GetObstacleHorizontalExtent(NavMeshObstacle obstacle)
        {
            if (obstacle == null)
                return spawnNavMeshSampleRadius;

            Vector3 scale = obstacle.transform.lossyScale;

            if (obstacle.shape == NavMeshObstacleShape.Box)
            {
                Vector3 size = Vector3.Scale(obstacle.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return Mathf.Max(size.x, size.z) * 0.5f;
            }

            return obstacle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        private bool IsPointInsideObstacleBounds(NavMeshObstacle obstacle, Vector3 worldPoint)
        {
            Vector3 localPoint = obstacle.transform.InverseTransformPoint(worldPoint) - obstacle.center;

            if (obstacle.shape == NavMeshObstacleShape.Box)
            {
                Vector3 halfSize = obstacle.size * 0.5f;
                float padding = agent != null ? agent.radius : 0.5f;
                return Mathf.Abs(localPoint.x) <= halfSize.x + padding &&
                       Mathf.Abs(localPoint.y) <= halfSize.y + padding &&
                       Mathf.Abs(localPoint.z) <= halfSize.z + padding;
            }

            float radius = obstacle.radius + (agent != null ? agent.radius : 0.5f);
            Vector2 horizontal = new(localPoint.x, localPoint.z);
            return horizontal.sqrMagnitude <= radius * radius &&
                   Mathf.Abs(localPoint.y) <= obstacle.height * 0.5f;
        }

        private void TickPatrol()
        {
            if (TryDetect(out Transform target))
            {
                EnterChase(target);
                return;
            }

            if (isBoss)
            {
                SetAgentStopped(true);
                return;
            }

            TickWander();
        }

        private void TickInvestigate()
        {
            if (TryDetect(out Transform target))
            {
                EnterChase(target);
                return;
            }

            if (IsOutsideChaseLimit(transform.position))
            {
                EnterReturn();
                return;
            }

            RefreshDestination(investigatePosition);
            RecoverIfStuck(investigatePosition);
            LookAt(investigatePosition);

            if (HasArrived() || Time.time - stateEnterTime > searchDuration)
            {
                EnterReturn();
            }
        }

        private void TickChase()
        {
            if (!IsValidCombatTarget(currentTarget))
            {
                currentTarget = null;
                EnterInvestigate(lastKnownPos);
                return;
            }

            if (IsOutsideChaseLimit(transform.position))
            {
                EnterReturn();
                return;
            }

            if (TryDetect(out Transform detectedTarget))
            {
                currentTarget = detectedTarget;
                RememberTargetPosition(detectedTarget.position);
            }

            bool canSeeTarget = vision != null && vision.CanSee(currentTarget);
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);

            if (canSeeTarget && distanceToTarget <= preferredRangeMax)
            {
                EnterState(AIState.Combat);
                return;
            }

            if (Time.time - lastSeenTime <= lostTargetChaseDuration)
            {
                RefreshDestination(lastKnownPos);
                RecoverIfStuck(lastKnownPos);
                return;
            }

            if (TryGetVisionBlocker(currentTarget, out RaycastHit hit))
            {
                EnterSearchCover(lastKnownPos, hit.collider);
                return;
            }

            EnterSearchCover(lastKnownPos, null);
        }

        private void TickCombat()
        {
            if (!IsValidCombatTarget(currentTarget))
            {
                currentTarget = null;
                animHandler?.SetAimMode(false);
                EnterInvestigate(lastKnownPos);
                return;
            }

            if (IsOutsideChaseLimit(transform.position))
            {
                animHandler?.SetAimMode(false);
                EnterReturn();
                return;
            }

            bool canSeeTarget = vision != null && vision.CanSee(currentTarget);
            if (!canSeeTarget)
            {
                animHandler?.SetAimMode(false);
                if (TryGetVisionBlocker(currentTarget, out RaycastHit hit))
                {
                    EnterSearchCover(lastKnownPos, hit.collider);
                    return;
                }

                EnterState(AIState.Chase);
                SetAgentStopped(false);
                return;
            }

            animHandler?.SetAimMode(true);
            RememberTargetPosition(currentTarget.position);

            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);

            if (distanceToTarget > preferredRangeMax)
            {
                animHandler?.SetAimMode(false);
                EnterState(AIState.Chase);
                return;
            }

            if (distanceToTarget < preferredRangeMin)
            {
                if (!TryRetreat())
                {
                    SetAgentStopped(true);
                }
            }
            else
            {
                SetAgentStopped(true);
            }

            LookAt(currentTarget.position);
            shooter?.TryFireAt(currentTarget);
        }

        private void TickSearchCover()
        {
            if (TryDetect(out Transform target))
            {
                EnterChase(target);
                return;
            }

            if (IsOutsideChaseLimit(transform.position))
            {
                EnterReturn();
                return;
            }

            if (Time.time - stateEnterTime > coverSearchDuration)
            {
                EnterReturn();
                return;
            }

            if (!hasActiveSearchPoint)
            {
                if (!TryTakeNextSearchPoint())
                {
                    EnterReturn();
                }

                return;
            }

            RefreshDestination(activeSearchPoint);
            RecoverIfStuck(activeSearchPoint);
            LookAt(lastKnownPos);

            if (HasArrived() && Time.time - activeSearchPointEnterTime >= searchPointWaitTime)
            {
                hasActiveSearchPoint = false;
            }
        }

        private void TickReturn()
        {
            if (TryDetect(out Transform target))
            {
                EnterChase(target);
                return;
            }

            RefreshDestination(spawnPosition);
            RecoverIfStuck(spawnPosition);

            if (HasArrived())
            {
                EnterPatrol();
            }
        }

        private void TickWander()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            if (isWanderWaiting)
            {
                wanderTimer += Time.deltaTime;
                if (wanderTimer >= wanderWaitDuration)
                {
                    isWanderWaiting = false;
                    SetWanderDestination();
                }

                return;
            }

            if (hasWanderDestination && HasArrived())
            {
                hasWanderDestination = false;
                StartWanderWait(Random.Range(wanderWait.x, wanderWait.y));
            }
        }

        private void StartWanderWait(float duration)
        {
            isWanderWaiting = true;
            wanderTimer = 0f;
            wanderWaitDuration = Mathf.Max(0f, duration);
        }

        private void SetWanderDestination()
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 randomOffset = Random.insideUnitSphere * wanderRadius;
                randomOffset.y = 0f;
                Vector3 candidate = spawnPosition + randomOffset;

                if (TrySetDestination(candidate))
                {
                    hasWanderDestination = true;
                    return;
                }
            }

            hasWanderDestination = false;
            StartWanderWait(1f);
        }

        private bool TryDetect(out Transform target)
        {
            target = null;
            if (vision == null) return false;

            if (vision.TryGetVisibleTarget(out target))
                return true;

            if (vision.TryGetProximityTarget(out target))
                return true;

            return false;
        }

        private void EnterChase(Transform target)
        {
            if (!IsValidCombatTarget(target))
                return;

            bool wasAlreadyAlerted = CurrentState == AIState.Chase || CurrentState == AIState.Combat;
            currentTarget = target;
            RememberTargetPosition(target.position);
            EnterState(AIState.Chase);
            SetAgentStopped(false);
            RefreshDestination(lastKnownPos, true);

            if (!wasAlreadyAlerted)
                TryPlayAlertSound(target);
        }

        private static bool IsValidCombatTarget(Transform target)
        {
            if (target == null)
                return false;

            PlayerHealthSystem health = target.GetComponentInParent<PlayerHealthSystem>();
            return health != null && health.IsAlive;
        }

        private void EnterInvestigate(Vector3 position)
        {
            if (!TryProjectToNavMesh(position, out Vector3 navPosition))
            {
                EnterReturn();
                return;
            }

            currentTarget = null;
            investigatePosition = navPosition;
            EnterState(AIState.Investigate);
            SetAgentStopped(false);
            RefreshDestination(investigatePosition, true);
        }

        private void EnterReturn()
        {
            currentTarget = null;
            ClearCoverSearch();
            EnterState(AIState.Return);
            SetAgentStopped(false);
            RefreshDestination(spawnPosition, true);
        }

        private void EnterPatrol()
        {
            currentTarget = null;
            ClearCoverSearch();
            hasWanderDestination = false;
            EnterState(AIState.Patrol);

            if (isBoss)
            {
                SetAgentStopped(true);
                return;
            }

            SetAgentStopped(false);
            StartWanderWait(Random.Range(0.5f, 1.5f));
        }

        private void EnterState(AIState nextState)
        {
            if (CurrentState == nextState)
            {
                ApplyWeaponVisibilityForState(nextState);
                return;
            }

            if (CanUseNetworkState)
            {
                State.Value = nextState;
            }
            else
            {
                localState = nextState;
            }

            ApplyWeaponVisibilityForState(nextState);
            stateEnterTime = Time.time;
            repathTimer = repathInterval;
        }

        private void SubscribeStateChanges()
        {
            if (subscribedToStateChanges)
            {
                return;
            }

            State.OnValueChanged += OnStateChanged;
            subscribedToStateChanges = true;
        }

        private void UnsubscribeStateChanges()
        {
            if (!subscribedToStateChanges)
            {
                return;
            }

            State.OnValueChanged -= OnStateChanged;
            subscribedToStateChanges = false;
        }

        private void OnStateChanged(AIState previousState, AIState nextState)
        {
            localState = nextState;
            ApplyWeaponVisibilityForState(nextState);
        }

        private void ApplyWeaponVisibilityForState(AIState state)
        {
            ResolveEnemyWeaponVisual();
            if (enemyWeaponVisual == null)
            {
                return;
            }

            enemyWeaponVisual.SetWeaponVisible(ShouldShowWeaponForState(state));
        }

        private static bool ShouldShowWeaponForState(AIState state)
        {
            return state == AIState.Chase ||
                   state == AIState.Combat ||
                   state == AIState.SearchCover;
        }

        private void ResolveEnemyWeaponVisual()
        {
            if (enemyWeaponVisual != null)
            {
                return;
            }

            enemyWeaponVisual = GetComponentInChildren<EnemyWeaponVisual>(true);
        }

        private void TryPlayAlertSound(Transform target)
        {
            if (!canPlayAlertSound)
                return;

            canPlayAlertSound = false;
            if (alertSoundCooldownRoutine != null)
                StopCoroutine(alertSoundCooldownRoutine);

            alertSoundCooldownRoutine = StartCoroutine(AlertSoundCooldownRoutine());

            ulong enemyId = NetworkObject != null ? NetworkObject.NetworkObjectId : 0;
            ulong targetId = 0;
            NetworkObject targetNetworkObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            if (targetNetworkObject != null)
                targetId = targetNetworkObject.NetworkObjectId;

            EventBus.Publish(new EnemyAlertedEvent
            {
                enemyNetworkObjectId = enemyId,
                targetNetworkObjectId = targetId,
                position = transform.position
            });

            if (IsSpawned)
            {
                PlayAlertIndicatorClientRpc();
                PlayEnemyAlertAudioClientRpc(transform.position, alertSoundVolumeMultiplier);
            }
            else
            {
                PlayAlertIndicatorLocal();
                PublishEnemyAlertAudio(transform.position, alertSoundVolumeMultiplier);
            }
        }

        private System.Collections.IEnumerator AlertSoundCooldownRoutine()
        {
            yield return new WaitForSeconds(alertSoundCooldown);
            canPlayAlertSound = true;
            alertSoundCooldownRoutine = null;
        }

        [ClientRpc]
        private void PlayAlertIndicatorClientRpc()
        {
            PlayAlertIndicatorLocal();
        }

        private void ResolveAlertIndicator()
        {
            if (alertIndicatorRoot != null)
            {
                alertIndicatorRoot.SetActive(false);
                return;
            }

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                string childName = child.name;
                if (!string.Equals(childName, "AlertIndicator", System.StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(childName, "Exclamation", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                alertIndicatorRoot = child.gameObject;
                alertIndicatorRoot.SetActive(false);
                return;
            }
        }

        private void PlayAlertIndicatorLocal()
        {
            ResolveAlertIndicator();
            if (alertIndicatorRoot == null)
                return;

            if (alertIndicatorRoutine != null)
                StopCoroutine(alertIndicatorRoutine);

            alertIndicatorRoutine = StartCoroutine(PlayAlertIndicatorRoutine());
        }

        private System.Collections.IEnumerator PlayAlertIndicatorRoutine()
        {
            Transform indicatorTransform = alertIndicatorRoot.transform;
            float startScale = Mathf.Max(0.01f, alertIndicatorStartScale);
            float endScale = Mathf.Max(startScale, alertIndicatorEndScale);
            float duration = Mathf.Max(0.1f, alertIndicatorDuration);
            float elapsed = 0f;

            alertIndicatorRoot.SetActive(true);
            indicatorTransform.localScale = Vector3.one * startScale;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float ratio = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - ratio, 3f);
                FaceAlertIndicatorToCamera(indicatorTransform);
                indicatorTransform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);
                yield return null;
            }

            FaceAlertIndicatorToCamera(indicatorTransform);
            indicatorTransform.localScale = Vector3.one * endScale;
            alertIndicatorRoot.SetActive(false);
            alertIndicatorRoutine = null;
        }

        private static void FaceAlertIndicatorToCamera(Transform indicatorTransform)
        {
            Camera camera = Camera.main;
            if (indicatorTransform == null || camera == null)
                return;

            Vector3 direction = indicatorTransform.position - camera.transform.position;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            indicatorTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        [ClientRpc]
        private void PlayEnemyAlertAudioClientRpc(Vector3 position, float volumeMultiplier)
        {
            PublishEnemyAlertAudio(position, volumeMultiplier);
        }

        private static void PublishEnemyAlertAudio(Vector3 position, float volumeMultiplier)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = AudioCueId.EnemyAlert,
                position = position,
                use3D = true,
                volumeMultiplier = volumeMultiplier
            });
        }

        private void TickFootstepAudio()
        {
            if (!playFootstepSound || agent == null || !agent.enabled || !agent.isOnNavMesh || agent.isStopped)
            {
                footstepTimer = 0f;
                return;
            }

            Vector3 velocity = agent.velocity;
            velocity.y = 0f;
            if (velocity.magnitude < footstepMinSpeed)
            {
                footstepTimer = 0f;
                return;
            }

            footstepTimer += Time.deltaTime;
            if (footstepTimer < footstepInterval)
                return;

            footstepTimer = 0f;

            if (IsSpawned)
            {
                PlayEnemyFootstepAudioClientRpc(transform.position, footstepVolumeMultiplier);
            }
            else
            {
                PublishEnemyFootstepAudio(transform.position, footstepVolumeMultiplier);
            }
        }

        [ClientRpc]
        private void PlayEnemyFootstepAudioClientRpc(Vector3 position, float volumeMultiplier)
        {
            PublishEnemyFootstepAudio(position, volumeMultiplier);
        }

        private static void PublishEnemyFootstepAudio(Vector3 position, float volumeMultiplier)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = AudioCueId.EnemyFootstep,
                position = position,
                use3D = true,
                volumeMultiplier = volumeMultiplier
            });
        }

        private void EnterSearchCover(Vector3 searchOrigin, Collider coverCollider)
        {
            BuildCoverSearchQueue(searchOrigin, coverCollider);
            EnterState(AIState.SearchCover);
            SetAgentStopped(false);
            hasActiveSearchPoint = false;

            if (!TryTakeNextSearchPoint())
            {
                EnterInvestigate(searchOrigin);
            }
        }

        private void RememberTargetPosition(Vector3 position)
        {
            if (hasPreviousSeenPosition)
            {
                float deltaTime = Mathf.Max(0.01f, Time.time - lastMemoryUpdateTime);
                lastKnownVelocity = (position - previousSeenPosition) / deltaTime;
            }

            previousSeenPosition = position;
            hasPreviousSeenPosition = true;
            lastMemoryUpdateTime = Time.time;
            lastKnownPos = position;
            lastSeenTime = Time.time;
        }

        private void RefreshDestination(Vector3 destination, bool force = false)
        {
            if (agent == null || !agent.isOnNavMesh) return;

            repathTimer += Time.deltaTime;
            if (!force && repathTimer < repathInterval) return;

            repathTimer = 0f;

            if (force || Vector3.Distance(lastDestination, destination) > 0.25f)
                TrySetDestination(destination);
        }

        private bool TrySetDestination(Vector3 rawDestination)
        {
            if (agent == null || !agent.isOnNavMesh) return false;
            if (!TryBuildSafeDestination(rawDestination, out Vector3 destination, out NavMeshPath path))
            {
                if (!TryFindNearbySafeReachablePoint(rawDestination, out destination, out path) &&
                    !TryFindStopPointBeforeObstacle(rawDestination, out destination, out path))
                {
                    return false;
                }
            }

            agent.SetPath(path);
            agent.isStopped = false;
            lastDestination = destination;
            return true;
        }

        private void BuildCoverSearchQueue(Vector3 searchOrigin, Collider coverCollider)
        {
            ClearCoverSearch();
            TryEnqueueSearchPoint(searchOrigin);

            Vector3 predictedPosition = searchOrigin + lastKnownVelocity * predictedTargetLeadTime;
            if (lastKnownVelocity.sqrMagnitude > 0.05f)
            {
                TryEnqueueSearchPoint(predictedPosition);
            }

            if (coverCollider != null)
            {
                EnqueueCoverBoundsPoints(coverCollider.bounds, searchOrigin);
            }

            EnqueueFallbackSearchRing(searchOrigin);
        }

        private void EnqueueCoverBoundsPoints(Bounds bounds, Vector3 searchOrigin)
        {
            Vector3 center = bounds.center;
            center.y = transform.position.y;

            float x = bounds.extents.x + coverCornerPadding;
            float z = bounds.extents.z + coverCornerPadding;

            Vector3[] candidates = BuildBoundsCandidates(center, x, z);
            SortCandidatesByRouteCost(candidates, searchOrigin);

            for (int i = 0; i < candidates.Length; i++)
            {
                TryEnqueueSearchPoint(candidates[i]);
            }
        }

        private void EnqueueFallbackSearchRing(Vector3 center)
        {
            int count = Mathf.Max(3, fallbackSearchPointCount);
            float radius = Mathf.Max(1f, fallbackSearchRadius);

            for (int i = 0; i < count; i++)
            {
                float angle = 360f / count * i;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                TryEnqueueSearchPoint(center + offset);
            }
        }

        private Vector3[] BuildBoundsCandidates(Vector3 center, float x, float z)
        {
            return new[]
            {
                new Vector3(center.x + x, center.y, center.z + z),
                new Vector3(center.x - x, center.y, center.z + z),
                new Vector3(center.x + x, center.y, center.z - z),
                new Vector3(center.x - x, center.y, center.z - z),
                new Vector3(center.x + x, center.y, center.z),
                new Vector3(center.x - x, center.y, center.z),
                new Vector3(center.x, center.y, center.z + z),
                new Vector3(center.x, center.y, center.z - z),
            };
        }

        private void SortCandidatesByRouteCost(Vector3[] candidates, Vector3 finalDestination)
        {
            for (int i = 0; i < candidates.Length - 1; i++)
            {
                for (int j = i + 1; j < candidates.Length; j++)
                {
                    float iScore = CalculateRouteCandidateScore(candidates[i], finalDestination);
                    float jScore = CalculateRouteCandidateScore(candidates[j], finalDestination);
                    if (jScore >= iScore)
                    {
                        continue;
                    }

                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
            }
        }

        private bool TryEnqueueSearchPoint(Vector3 candidate)
        {
            if (coverSearchQueue.Count >= Mathf.Max(1, maxCoverSearchPoints))
                return false;

            if (IsOutsideChaseLimit(candidate))
                return false;

            if (!TryProjectToNavMesh(candidate, out Vector3 projected))
                return false;

            if (IsDuplicateSearchPoint(projected))
                return false;

            if (!TryCalculateCompletePath(projected, out _))
                return false;

            if (PathHasBlockingObstacleToPoint(projected) && !CanBuildDetourToPoint(projected))
                return false;

            coverSearchQueue.Enqueue(projected);
            return true;
        }

        private bool IsDuplicateSearchPoint(Vector3 point)
        {
            foreach (Vector3 queuedPoint in coverSearchQueue)
            {
                if (Vector3.Distance(queuedPoint, point) <= arrivalDistance)
                    return true;
            }

            return false;
        }

        private bool TryTakeNextSearchPoint()
        {
            while (coverSearchQueue.Count > 0)
            {
                Vector3 nextPoint = coverSearchQueue.Dequeue();
                if (!TrySetDestination(nextPoint))
                    continue;

                activeSearchPoint = nextPoint;
                activeSearchPointEnterTime = Time.time;
                hasActiveSearchPoint = true;
                return true;
            }

            return false;
        }

        private void ClearCoverSearch()
        {
            coverSearchQueue.Clear();
            hasActiveSearchPoint = false;
            activeSearchPoint = Vector3.zero;
        }

        private bool TryBuildSafeDestination(Vector3 rawDestination, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            if (!TryProjectToNavMesh(rawDestination, out destination))
                return false;

            if (!TryCalculateCompletePath(destination, out path))
                return false;

            if (!PathHasBlockingObstacle(path, out RaycastHit hit))
                return true;

            return TryFindDetourPoint(rawDestination, hit, out destination, out path);
        }

        private bool TryProjectToNavMesh(Vector3 position, out Vector3 projected)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, pathSampleRadius, NavMesh.AllAreas))
            {
                projected = hit.position;
                return true;
            }

            projected = position;
            return false;
        }

        private bool TryCalculateCompletePath(Vector3 destination, out NavMeshPath path)
        {
            path = new NavMeshPath();
            return agent != null &&
                   agent.CalculatePath(destination, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }

        private bool TryCalculateCompletePath(Vector3 source, Vector3 destination, out NavMeshPath path)
        {
            path = new NavMeshPath();
            return NavMesh.CalculatePath(source, destination, NavMesh.AllAreas, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }

        private bool TryFindNearbySafeReachablePoint(Vector3 center, out Vector3 point, out NavMeshPath path)
        {
            path = null;
            const int directionCount = 12;

            for (int ring = 1; ring <= 3; ring++)
            {
                float radius = ring * pathSampleRadius;

                for (int i = 0; i < directionCount; i++)
                {
                    float angle = 360f / directionCount * i;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                    Vector3 candidate = center + offset;

                    if (TryAcceptDetourCandidate(candidate, out point, out path))
                    {
                        return true;
                    }
                }
            }

            point = center;
            return false;
        }

        private bool TryFindStopPointBeforeObstacle(Vector3 rawDestination, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            if (!HasBlockingObstacleBetween(transform.position, rawDestination, out RaycastHit hit))
                return false;

            Vector3 direction = rawDestination - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return false;

            direction.Normalize();
            Vector3 stopPoint = hit.point - direction * Mathf.Max(pathProbeRadius + 0.75f, agent.radius + 0.75f);

            if (!TryProjectToNavMesh(stopPoint, out destination))
                return false;

            if (!TryCalculateCompletePath(destination, out path))
                return false;

            return !PathHasBlockingObstacle(path, out _);
        }

        private bool PathHasBlockingObstacle(NavMeshPath path, out RaycastHit hit)
        {
            hit = default;
            if (path == null || path.corners == null || path.corners.Length < 2)
                return false;

            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                if (HasBlockingObstacleBetween(path.corners[i], path.corners[i + 1], out hit))
                    return true;
            }

            return false;
        }

        private bool HasBlockingObstacleBetween(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            hit = default;

            Vector3 start = from + Vector3.up * 0.6f;
            Vector3 end = to + Vector3.up * 0.6f;
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance <= 0.05f)
                return false;

            RaycastHit[] hits = Physics.SphereCastAll(
                start,
                pathProbeRadius,
                direction.normalized,
                distance,
                pathObstacleMask,
                QueryTriggerInteraction.Ignore);

            float closestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit candidate = hits[i];
                if (candidate.collider == null)
                    continue;

                if (candidate.collider.transform == transform || candidate.collider.transform.IsChildOf(transform))
                    continue;

                if (currentTarget != null &&
                    (candidate.collider.transform == currentTarget || candidate.collider.transform.IsChildOf(currentTarget)))
                    continue;

                if (candidate.distance < closestDistance)
                {
                    closestDistance = candidate.distance;
                    hit = candidate;
                    found = true;
                }
            }

            return found;
        }

        private bool TryGetVisionBlocker(Transform target, out RaycastHit hit)
        {
            hit = default;
            if (target == null)
                return false;

            Vector3 origin = transform.position + Vector3.up * 1.6f;
            Vector3 targetPosition = target.position + Vector3.up * 1f;
            Vector3 direction = targetPosition - origin;
            float distance = direction.magnitude;
            if (distance <= 0.05f)
                return false;

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                Mathf.Max(0.15f, pathProbeRadius * 0.5f),
                direction.normalized,
                distance,
                pathObstacleMask,
                QueryTriggerInteraction.Ignore);

            float closestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit candidate = hits[i];
                if (candidate.collider == null)
                    continue;

                if (candidate.collider.transform == transform || candidate.collider.transform.IsChildOf(transform))
                    continue;

                if (candidate.collider.transform == target || candidate.collider.transform.IsChildOf(target))
                    continue;

                if (candidate.distance < closestDistance)
                {
                    closestDistance = candidate.distance;
                    hit = candidate;
                    found = true;
                }
            }

            return found;
        }

        private bool TryFindDetourPoint(Vector3 rawDestination, RaycastHit hit, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            if (hit.collider != null && TryFindBestBoundsRoutePoint(rawDestination, hit.collider.bounds, out destination, out path))
                return true;

            if (TryFindSideDetour(rawDestination, hit, out destination, out path))
                return true;

            if (hit.collider != null && TryFindBoundsDetour(rawDestination, hit.collider.bounds, out destination, out path))
                return true;

            return false;
        }

        private bool TryFindBestBoundsRoutePoint(Vector3 rawDestination, Bounds bounds, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            Vector3 center = bounds.center;
            center.y = transform.position.y;
            float x = bounds.extents.x + Mathf.Max(coverCornerPadding, detourDistance);
            float z = bounds.extents.z + Mathf.Max(coverCornerPadding, detourDistance);
            Vector3[] candidates = BuildBoundsCandidates(center, x, z);

            float bestScore = float.MaxValue;
            Vector3 bestDestination = rawDestination;
            NavMeshPath bestPath = null;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!TryAcceptDetourCandidate(candidates[i], out Vector3 candidateDestination, out NavMeshPath candidatePath))
                    continue;

                float score = CalculateRouteCandidateScore(candidateDestination, rawDestination, candidatePath);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestDestination = candidateDestination;
                bestPath = candidatePath;
            }

            if (bestPath == null)
                return false;

            destination = bestDestination;
            path = bestPath;
            return true;
        }

        private bool TryFindSideDetour(Vector3 rawDestination, RaycastHit hit, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            Vector3 normal = hit.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.01f)
                normal = (transform.position - hit.point).normalized;

            Vector3 tangent = Vector3.Cross(Vector3.up, normal.normalized);
            float baseDistance = Mathf.Max(detourDistance, pathProbeRadius * 3f);

            for (int ring = 1; ring <= 3; ring++)
            {
                float distance = baseDistance * ring;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 candidate = hit.point + normal.normalized * (pathProbeRadius + 0.6f) + tangent * side * distance;
                    if (TryAcceptDetourCandidate(candidate, out destination, out path))
                        return true;
                }
            }

            return false;
        }

        private bool TryFindBoundsDetour(Vector3 rawDestination, Bounds bounds, out Vector3 destination, out NavMeshPath path)
        {
            destination = rawDestination;
            path = null;

            Vector3 center = bounds.center;
            center.y = transform.position.y;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + Mathf.Max(detourDistance, pathProbeRadius * 3f);

            for (int ring = 1; ring <= 2; ring++)
            {
                float ringRadius = radius + detourDistance * (ring - 1);
                for (int i = 0; i < 12; i++)
                {
                    float angle = 360f / 12f * i;
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 candidate = center + direction * ringRadius;

                    if (TryAcceptDetourCandidate(candidate, out destination, out path))
                        return true;
                }
            }

            return false;
        }

        private bool TryAcceptDetourCandidate(Vector3 candidate, out Vector3 destination, out NavMeshPath path)
        {
            destination = candidate;
            path = null;

            if (!TryProjectToNavMesh(candidate, out destination))
                return false;

            if (!TryCalculateCompletePath(destination, out path))
                return false;

            return !PathHasBlockingObstacle(path, out _);
        }

        private bool PathHasBlockingObstacleToPoint(Vector3 point)
        {
            if (!TryCalculateCompletePath(point, out NavMeshPath path))
                return true;

            return PathHasBlockingObstacle(path, out _);
        }

        private bool CanBuildDetourToPoint(Vector3 point)
        {
            return HasBlockingObstacleBetween(transform.position, point, out RaycastHit hit) &&
                   TryFindDetourPoint(point, hit, out _, out _);
        }

        private float CalculateRouteCandidateScore(Vector3 candidate, Vector3 finalDestination)
        {
            if (!TryAcceptDetourCandidate(candidate, out Vector3 projected, out NavMeshPath firstLegPath))
                return float.MaxValue;

            return CalculateRouteCandidateScore(projected, finalDestination, firstLegPath);
        }

        private float CalculateRouteCandidateScore(Vector3 projectedCandidate, Vector3 finalDestination, NavMeshPath firstLegPath)
        {
            if (!TryProjectToNavMesh(finalDestination, out Vector3 projectedFinalDestination))
                return float.MaxValue;

            float firstLegLength = GetPathLength(firstLegPath);
            float secondLegLength = Vector3.Distance(projectedCandidate, projectedFinalDestination);
            bool hasSecondLegPath = TryCalculateCompletePath(projectedCandidate, projectedFinalDestination, out NavMeshPath secondLegPath);
            if (hasSecondLegPath)
            {
                secondLegLength = GetPathLength(secondLegPath);
            }

            float score = firstLegLength + secondLegLength;

            if (!hasSecondLegPath)
            {
                score += detourDistance * 8f;
            }

            if (hasSecondLegPath && PathHasBlockingObstacle(secondLegPath, out _))
            {
                score += detourDistance * 6f;
            }
            else
            {
                score -= detourDistance * 1.5f;
            }

            if (!HasBlockingObstacleBetween(projectedCandidate, projectedFinalDestination, out _))
            {
                score -= detourDistance * 2f;
            }

            return score;
        }

        private float GetPathLength(NavMeshPath path)
        {
            if (path == null || path.corners == null || path.corners.Length < 2)
                return 0f;

            float length = 0f;
            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                length += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }

            return length;
        }

        private bool TryRetreat()
        {
            if (currentTarget == null)
            {
                SetAgentStopped(true);
                return false;
            }

            Vector3 baseDirection = transform.position - currentTarget.position;
            baseDirection.y = 0f;

            if (baseDirection.sqrMagnitude < 0.01f)
                baseDirection = -transform.forward;

            baseDirection.Normalize();

            int attempts = Mathf.Max(1, retreatSampleAttempts);
            for (int i = 0; i < attempts; i++)
            {
                float angle = i == 0
                    ? 0f
                    : ((i % 2 == 0) ? 1f : -1f) * Mathf.Ceil(i * 0.5f) * 25f;

                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * baseDirection;
                Vector3 candidate = transform.position + direction * retreatDistance;

                if (TrySetDestination(candidate))
                    return true;
            }

            SetAgentStopped(true);
            return false;
        }

        private void RecoverIfStuck(Vector3 rawDestination)
        {
            if (agent == null || !agent.isOnNavMesh || agent.isStopped)
                return;

            if (Time.time < nextStuckCheckTime)
                return;

            nextStuckCheckTime = Time.time + stuckCheckInterval;

            if (!agent.hasPath || agent.pathPending || agent.remainingDistance <= arrivalDistance)
            {
                lastStuckCheckPosition = transform.position;
                return;
            }

            float movedDistance = Vector3.Distance(transform.position, lastStuckCheckPosition);
            lastStuckCheckPosition = transform.position;

            if (movedDistance > stuckMoveThreshold)
            {
                consecutiveStuckChecks = 0;
                return;
            }

            consecutiveStuckChecks++;

            if (!HasBlockingObstacleBetween(transform.position, rawDestination, out RaycastHit hit))
            {
                TryHardRecoverFromStuck(rawDestination);
                return;
            }

            if (TryFindDetourPoint(rawDestination, hit, out Vector3 detour, out NavMeshPath detourPath))
            {
                agent.SetPath(detourPath);
                agent.isStopped = false;
                lastDestination = detour;
                consecutiveStuckChecks = 0;
                return;
            }

            TryHardRecoverFromStuck(rawDestination);
        }

        private void TryHardRecoverFromStuck(Vector3 rawDestination)
        {
            if (consecutiveStuckChecks < hardStuckRecoveryThreshold)
                return;

            consecutiveStuckChecks = 0;

            Vector3 awayFromCurrent = rawDestination - transform.position;
            awayFromCurrent.y = 0f;

            if (awayFromCurrent.sqrMagnitude < 0.01f)
                awayFromCurrent = transform.forward;

            awayFromCurrent.Normalize();

            int attempts = 12;
            for (int i = 0; i < attempts; i++)
            {
                float angle = (360f / attempts) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * awayFromCurrent;
                Vector3 candidate = transform.position + direction * hardStuckRecoveryRadius;

                if (!TryBuildSafeDestination(candidate, out Vector3 destination, out NavMeshPath path))
                    continue;

                agent.SetPath(path);
                agent.isStopped = false;
                lastDestination = destination;
                return;
            }

            for (int i = 0; i < attempts; i++)
            {
                float angle = (360f / attempts) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * awayFromCurrent;
                Vector3 candidate = transform.position + direction * hardStuckRecoveryRadius;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit recoveryHit, hardStuckRecoveryRadius, NavMesh.AllAreas))
                    continue;

                agent.Warp(recoveryHit.position);
                TrySetDestination(rawDestination);
                return;
            }

            if (NavMesh.SamplePosition(rawDestination, out NavMeshHit destinationHit, hardStuckRecoveryRadius, NavMesh.AllAreas))
            {
                agent.Warp(destinationHit.position);
                TrySetDestination(rawDestination);
                return;
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit currentHit, hardStuckRecoveryRadius, NavMesh.AllAreas))
                agent.Warp(currentHit.position);
        }

        private bool HasArrived()
        {
            if (agent == null || !agent.isOnNavMesh) return true;
            if (agent.pathPending) return false;

            if (agent.remainingDistance <= arrivalDistance)
                return true;

            return !agent.hasPath && agent.velocity.sqrMagnitude < 0.01f;
        }

        private bool IsOutsideChaseLimit(Vector3 position)
        {
            return Vector3.Distance(spawnPosition, position) > maxChaseDistance;
        }

        private void SetAgentStopped(bool stopped)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            agent.isStopped = stopped;
        }

        private void LookAt(Vector3 position)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction),
                Time.deltaTime * 8f);
        }
    }
}

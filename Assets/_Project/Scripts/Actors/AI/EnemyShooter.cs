using DeadZone.Core;
using DeadZone.Systems;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 사격을 처리하고 적 전용 투사체 프리팹을 발사합니다.
    /// </summary>
    public class EnemyShooter : NetworkBehaviour
    {
        [Header("사격 기준")]
        [Tooltip("총알이 생성될 총구 위치입니다. EnemyWeaponVisual이 자동 연결할 수 있습니다.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("명중 판정에 사용할 레이어입니다. 현재 투사체 방식에서는 예비 설정으로 유지됩니다.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("적 전용 투사체")]
        [Tooltip("적이 발사하는 탄환 프리팹입니다. 플레이어 탄환과 색상 및 이펙트를 분리합니다.")]
        [SerializeField] private GameObject enemyBulletPrefab;

        [Header("적 전용 밸런스")]
        [Tooltip("플레이어를 감지한 뒤 첫 발을 쏘기 전 최소 대기 시간입니다.")]
        [SerializeField] private float minimumReactionTime = 1.5f;

        [Tooltip("SO 유효 사거리에 곱할 적 전용 사거리 배율입니다.")]
        [SerializeField, Range(0.1f, 1f)] private float enemyRangeMultiplier = 0.65f;

        [Tooltip("적이 실제로 사격할 수 있는 최대 유효 사거리입니다.")]
        [SerializeField] private float maxEnemyEffectiveRange = 24f;

        [Tooltip("무기 SO 탄속에 곱할 적 전용 탄속 배율입니다.")]
        [SerializeField, Range(0.05f, 1f)] private float enemyMuzzleVelocityMultiplier = 0.35f;

        [Tooltip("적 투사체에 적용할 최대 탄속입니다.")]
        [SerializeField] private float maxEnemyMuzzleVelocity = 300f;

        [Tooltip("무기 SO가 없거나 탄속 값이 잘못됐을 때 사용할 기본 탄속입니다.")]
        [SerializeField] private float fallbackEnemyMuzzleVelocity = 220f;

        [Tooltip("사격 호출이 끊긴 뒤 같은 타겟도 다시 조준한 것으로 판단하는 시간입니다.")]
        [SerializeField] private float targetMemoryResetDelay = 0.5f;

        private EnemyStats stats;
        private EnemyAnimHandler animHandler;
        private Transform trackedTarget;
        private float nextShotAllowed;
        private float targetAcquiredTime;
        private float lastTrackRefreshTime;
        private int burstCount;
        private float muzzleVelocity;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
            animHandler = GetComponent<EnemyAnimHandler>();
        }

        private void Start()
        {
            CacheMuzzleVelocity();
        }

        /// <summary>
        /// 네트워크 스폰 이후 서버 기준 총구 속도를 캐싱합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CacheMuzzleVelocity();
            }
        }

        /// <summary>
        /// 무기 비주얼에서 찾은 총구 위치를 사격 기준점으로 연결합니다.
        /// </summary>
        /// <param name="muzzlePoint">적 무기 프리팹 내부의 총구 위치입니다.</param>
        public void SetMuzzle(Transform muzzlePoint)
        {
            if (muzzlePoint == null)
            {
                return;
            }

            muzzle = muzzlePoint;
        }

        /// <summary>
        /// 타겟을 향해 적 전용 투사체를 발사합니다.
        /// </summary>
        /// <param name="target">사격 대상입니다.</param>
        public void TryFireAt(Transform target)
        {
            if (IsSpawned && !IsServer)
            {
                return;
            }

            if (target == null || stats == null || stats.StatsSO == null)
            {
                return;
            }

            if (Time.time < nextShotAllowed)
            {
                return;
            }

            if (muzzle == null || enemyBulletPrefab == null)
            {
                return;
            }

            EnemyStatsSO so = stats.StatsSO;
            Vector3 aimPoint = GetTargetAimPoint(target);
            Vector3 baseDir = (aimPoint - muzzle.position).normalized;
            float dist = Vector3.Distance(muzzle.position, aimPoint);
            float effectiveRange = GetEffectiveRange(so);

            if (dist > effectiveRange)
            {
                return;
            }

            if (!CanShootTarget(target, aimPoint, dist))
            {
                return;
            }

            if (!HasCompletedReactionDelay(target, so))
            {
                return;
            }

            float distRatio = Mathf.Clamp01(dist / effectiveRange);
            float spreadDeg = Mathf.Lerp(so.spreadAngleMin, so.spreadAngleMax, distRatio);

            Vector3 spreadDir = Quaternion.Euler(
                Random.Range(-1f, 1f) * spreadDeg,
                Random.Range(-1f, 1f) * spreadDeg,
                0f) * baseDir;

            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = DamageSystem.AI_SHOOTER_ID,
                weaponId = so.defaultWeapon != null
                    ? (Unity.Collections.FixedString64Bytes)so.defaultWeapon.itemID
                    : default,
                origin = muzzle.position,
                loudness = 1f,
            });

            animHandler?.TriggerFire();
            FireProjectile(spreadDir, so);
            UpdateBurstTiming(so);
        }

        private void CacheMuzzleVelocity()
        {
            if (stats == null || stats.StatsSO == null)
            {
                return;
            }

            WeaponDataSO weapon = stats.StatsSO.defaultWeapon;
            float sourceVelocity = weapon != null ? weapon.muzzleVelocity : fallbackEnemyMuzzleVelocity;
            if (sourceVelocity <= 0f)
            {
                sourceVelocity = fallbackEnemyMuzzleVelocity;
            }

            muzzleVelocity = Mathf.Min(sourceVelocity * enemyMuzzleVelocityMultiplier, maxEnemyMuzzleVelocity);
            muzzleVelocity = Mathf.Max(1f, muzzleVelocity);
        }

        private void FireProjectile(Vector3 direction, EnemyStatsSO so)
        {
            GameObject bullet = Instantiate(enemyBulletPrefab, muzzle.position, Quaternion.LookRotation(direction));

            ProjectileData projectileData = new ProjectileData
            {
                ShooterId = DamageSystem.AI_SHOOTER_ID,
                BaseDamage = so.defaultWeapon != null
                    ? Mathf.RoundToInt(so.defaultWeapon.damage * so.damageMultiplier)
                    : 10,
                Penetration = so.defaultAmmo != null
                    ? so.defaultAmmo.penetration + so.penetrationModifier
                    : 0,
                TargetNetId = 0,
                WasHeadAim = false,
                Range = GetEffectiveRange(so),
            };

            NetworkObject netObj = bullet.GetComponent<NetworkObject>();
            if (netObj != null && IsSpawned)
            {
                netObj.Spawn();
            }

            ProjectileController controller = bullet.GetComponent<ProjectileController>();
            if (controller != null)
            {
                controller.Initialize(projectileData, direction, muzzleVelocity);
            }
        }

        private void UpdateBurstTiming(EnemyStatsSO so)
        {
            burstCount++;
            if (burstCount >= so.burstSize)
            {
                burstCount = 0;
                nextShotAllowed = Time.time + so.burstRestDelay;
                return;
            }

            nextShotAllowed = Time.time + so.fireInterval;
        }

        private bool HasCompletedReactionDelay(Transform target, EnemyStatsSO so)
        {
            bool targetChanged = trackedTarget != target ||
                                 Time.time - lastTrackRefreshTime > Mathf.Max(0.05f, targetMemoryResetDelay);

            lastTrackRefreshTime = Time.time;

            if (targetChanged)
            {
                trackedTarget = target;
                targetAcquiredTime = Time.time;
                burstCount = 0;
                nextShotAllowed = Mathf.Max(nextShotAllowed, Time.time + GetReactionDelay(so));
                return false;
            }

            return Time.time - targetAcquiredTime >= GetReactionDelay(so);
        }

        private float GetReactionDelay(EnemyStatsSO so)
        {
            return Mathf.Max(minimumReactionTime, so.reactionTime);
        }

        private float GetEffectiveRange(EnemyStatsSO so)
        {
            float scaledRange = so.maxEffectiveRange * enemyRangeMultiplier;
            return Mathf.Max(1f, Mathf.Min(scaledRange, maxEnemyEffectiveRange));
        }

        private Vector3 GetTargetAimPoint(Transform target)
        {
            return target.position + Vector3.up;
        }

        private bool CanShootTarget(Transform target, Vector3 aimPoint, float distance)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                muzzle.position,
                (aimPoint - muzzle.position).normalized,
                distance,
                hitMask,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                return true;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Collider candidate = hits[i].collider;
                if (candidate == null)
                {
                    continue;
                }

                Transform hitTransform = candidate.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                {
                    continue;
                }

                return hitTransform == target || hitTransform.IsChildOf(target);
            }

            return true;
        }
    }
}

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Audio;
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

        [Header("적 탄도 시각 효과")]
        [Tooltip("원격 클라이언트에서 로컬로 재생하는 적 탄도 시각 효과의 최대 유지 시간입니다. 서버 판정 투사체 수명과는 별개입니다.")]
        [SerializeField, Min(0.01f)] private float projectileVisualMaxLifetime = 0.35f;

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

            if (!IsValidCombatTarget(target))
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

            if (Time.time < nextShotAllowed)
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
            PlayAttackAudio(so);
            FireProjectile(spreadDir, so);
            UpdateBurstTiming(so);
        }

        private void PlayAttackAudio(EnemyStatsSO so)
        {
            if (so == null || muzzle == null)
            {
                return;
            }

            AudioCueId cueId = so.isBoss ? AudioCueId.BossAttack : AudioCueId.EnemyAttack;

            if (IsSpawned)
            {
                PlayAttackAudioClientRpc(cueId, muzzle.position);
                return;
            }

            PublishAttackAudio(cueId, muzzle.position);
        }

        [ClientRpc]
        private void PlayAttackAudioClientRpc(AudioCueId cueId, Vector3 position)
        {
            PublishAttackAudio(cueId, position);
        }

        private static void PublishAttackAudio(AudioCueId cueId, Vector3 position)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = cueId,
                position = position,
                use3D = true,
                volumeMultiplier = 1f
            });
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
            if (enemyBulletPrefab == null || muzzle == null || so == null)
            {
                return;
            }

            Vector3 normalizedDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : transform.forward;

            float effectiveRange = GetEffectiveRange(so);
            Vector3 spawnPosition = muzzle.position;

            GameObject bullet = Instantiate(
                enemyBulletPrefab,
                spawnPosition,
                Quaternion.LookRotation(normalizedDirection));

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
                Range = effectiveRange,
            };

            NetworkObject netObj = bullet.GetComponent<NetworkObject>();
            if (netObj != null && IsSpawned)
            {
                netObj.Spawn();
            }

            ProjectileController controller = bullet.GetComponent<ProjectileController>();
            if (controller != null)
            {
                controller.Initialize(projectileData, normalizedDirection, muzzleVelocity);
            }

            if (IsSpawned)
            {
                PlayEnemyProjectileVisualClientRpc(
                    spawnPosition,
                    normalizedDirection,
                    muzzleVelocity,
                    effectiveRange,
                    projectileVisualMaxLifetime);
            }
        }

        /// <summary>
        /// 서버가 확정한 적 탄도 정보를 받아 각 클라이언트에서 로컬 bullet trail을 재생합니다.
        /// Host는 서버 투사체를 직접 보므로 중복 visual 생성을 생략합니다.
        /// </summary>
        [ClientRpc]
        private void PlayEnemyProjectileVisualClientRpc(
            Vector3 spawnPosition,
            Vector3 fireDirection,
            float visualVelocity,
            float visualRange,
            float visualLifetime)
        {
            if (IsServer && !IsClient)
            {
                return;
            }

            if (enemyBulletPrefab == null)
            {
                return;
            }

            Vector3 normalizedDirection = fireDirection.sqrMagnitude > 0.0001f
                ? fireDirection.normalized
                : transform.forward;

            GameObject visual = Instantiate(
                enemyBulletPrefab,
                spawnPosition,
                Quaternion.LookRotation(normalizedDirection));

            PrepareLocalProjectileVisual(visual);

            BulletTrailVisual trailVisual = visual.GetComponent<BulletTrailVisual>();
            if (trailVisual == null)
            {
                trailVisual = visual.AddComponent<BulletTrailVisual>();
            }

            trailVisual.Initialize(
                spawnPosition,
                normalizedDirection,
                visualVelocity,
                visualRange,
                visualLifetime);
        }

        /// <summary>
        /// 네트워크 판정용 projectile prefab을 로컬 visual로 재사용할 때 판정/네트워크 컴포넌트가 동작하지 않도록 비활성화합니다.
        /// </summary>
        private static void PrepareLocalProjectileVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            ProjectileController[] projectileControllers =
                visualRoot.GetComponentsInChildren<ProjectileController>(true);
            for (int i = 0; i < projectileControllers.Length; i++)
            {
                projectileControllers[i].enabled = false;
            }

            NetworkObject[] networkObjects =
                visualRoot.GetComponentsInChildren<NetworkObject>(true);
            for (int i = 0; i < networkObjects.Length; i++)
            {
                networkObjects[i].enabled = false;
            }

            Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
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
            Collider targetCollider = target.GetComponentInChildren<Collider>();
            if (targetCollider != null)
            {
                return targetCollider.bounds.center;
            }

            return target.position + Vector3.up;
        }

        private static bool IsValidCombatTarget(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            PlayerHealthSystem health = target.GetComponentInParent<PlayerHealthSystem>();
            return health != null && health.IsAlive;
        }

        private bool CanShootTarget(Transform target, Vector3 aimPoint, float distance)
        {
            Vector3 direction = (aimPoint - muzzle.position).normalized;
            if (!HasClearShotToTarget(target, direction, distance, hitMask))
                return false;

            return HasClearShotToTarget(target, direction, distance, ~0);
        }

        private bool HasClearShotToTarget(Transform target, Vector3 direction, float distance, LayerMask mask)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                muzzle.position,
                direction,
                distance,
                mask,
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

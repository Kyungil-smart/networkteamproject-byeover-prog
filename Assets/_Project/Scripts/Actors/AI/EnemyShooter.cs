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

        private EnemyStats stats;
        private EnemyAnimHandler animHandler;
        private float nextShotAllowed;
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
            Vector3 baseDir = (target.position - muzzle.position).normalized;
            float dist = Vector3.Distance(muzzle.position, target.position);

            float distRatio = Mathf.Clamp01(dist / so.maxEffectiveRange);
            float spreadDeg = Mathf.Lerp(so.spreadAngleMin, so.spreadAngleMax, distRatio);
            if (dist > so.maxEffectiveRange)
            {
                spreadDeg *= so.rangeSpreadMultiplier;
            }

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
            muzzleVelocity = weapon != null ? weapon.muzzleVelocity : 300f;
            if (muzzleVelocity <= 0f)
            {
                muzzleVelocity = 300f;
            }
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
                Range = so.maxEffectiveRange,
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
    }
}

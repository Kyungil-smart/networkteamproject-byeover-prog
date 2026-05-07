using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 사격을 처리한다.
    /// 적 전용 투사체 프리팹(빨간색)을 사용하여 플레이어 탄환과 구분한다.
    /// WeaponDataSO의 projectilePrefab은 플레이어용이므로 사용하지 않는다.
    /// </summary>
    public class EnemyShooter : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("적 전용 투사체")]
        [Tooltip("적이 발사하는 탄환 프리팹 (Enemy_Bullet_Trail). 플레이어 탄환과 색상 구분됨")]
        [SerializeField] private GameObject enemyBulletPrefab;

        private EnemyStats stats;
        private float nextShotAllowed;
        private int burstCount;
        private float muzzleVelocity;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
        }

        private void Start()
        {
            CacheMuzzleVelocity();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) CacheMuzzleVelocity();
        }

        /// <summary>무기 SO에서 총구 속도를 가져온다.</summary>
        private void CacheMuzzleVelocity()
        {
            if (stats == null || stats.StatsSO == null) return;
            var weapon = stats.StatsSO.defaultWeapon;
            muzzleVelocity = weapon != null ? weapon.muzzleVelocity : 300f;
            if (muzzleVelocity <= 0f) muzzleVelocity = 300f;
        }

        /// <summary>
        /// 타겟을 향해 사격한다. EnemyAI.TickEngage에서 호출.
        /// </summary>
        public void TryFireAt(Transform target)
        {
            if (IsSpawned && !IsServer) return;
            if (target == null || stats == null || stats.StatsSO == null) return;
            if (Time.time < nextShotAllowed) return;
            if (muzzle == null || enemyBulletPrefab == null) return;

            var so = stats.StatsSO;

            // ── 방향 + 탄퍼짐 ──
            Vector3 baseDir = (target.position - muzzle.position).normalized;
            float dist = Vector3.Distance(muzzle.position, target.position);

            float distRatio = Mathf.Clamp01(dist / so.maxEffectiveRange);
            float spreadDeg = Mathf.Lerp(so.spreadAngleMin, so.spreadAngleMax, distRatio);
            if (dist > so.maxEffectiveRange)
                spreadDeg *= so.rangeSpreadMultiplier;

            Vector3 spreadDir = Quaternion.Euler(
                Random.Range(-1f, 1f) * spreadDeg,
                Random.Range(-1f, 1f) * spreadDeg,
                0) * baseDir;

            // ── 청각 이벤트 ──
            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = DamageSystem.AI_SHOOTER_ID,
                weaponId = so.defaultWeapon != null
                    ? (Unity.Collections.FixedString64Bytes)so.defaultWeapon.itemID
                    : default,
                origin = muzzle.position,
                loudness = 1f,
            });

            // ── 투사체 발사 ──
            FireProjectile(spreadDir, so);

            // ── 점사 타이밍 ──
            burstCount++;
            if (burstCount >= so.burstSize)
            {
                burstCount = 0;
                nextShotAllowed = Time.time + so.burstRestDelay;
            }
            else
            {
                nextShotAllowed = Time.time + so.fireInterval;
            }
        }

        /// <summary>적 전용 투사체를 생성한다.</summary>
        private void FireProjectile(Vector3 direction, EnemyStatsSO so)
        {
            GameObject bullet = Instantiate(enemyBulletPrefab,
                muzzle.position, Quaternion.LookRotation(direction));

            var projectileData = new ProjectileData
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

            // 네트워크 스폰 (온라인)
            var netObj = bullet.GetComponent<NetworkObject>();
            if (netObj != null && IsSpawned)
                netObj.Spawn();

            // 초기화
            var controller = bullet.GetComponent<ProjectileController>();
            if (controller != null)
                controller.Initialize(projectileData, direction, muzzleVelocity);
        }
    }
}
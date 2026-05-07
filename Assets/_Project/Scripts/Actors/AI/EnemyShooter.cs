using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 사격을 처리한다.
    /// SO의 projectilePrefab이 있으면 투사체 발사, 없으면 hitscan(레이캐스트) 방식.
    /// </summary>
    public class EnemyShooter : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("투사체 발사 설정")]
        [Tooltip("SO에 projectilePrefab이 없을 때 사용할 기본 투사체 프리팹")]
        [SerializeField] private GameObject fallbackProjectilePrefab;

        private EnemyStats stats;
        private float nextShotAllowed;
        private int burstCount;

        // SO 캐시
        private GameObject projectilePrefab;
        private float muzzleVelocity;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
        }

        /// <summary>네트워크 스폰 시 또는 Start에서 SO 데이터 캐시</summary>
        private void Start()
        {
            CacheWeaponData();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                CacheWeaponData();
        }

        /// <summary>SO에서 투사체 관련 데이터를 캐시한다.</summary>
        private void CacheWeaponData()
        {
            if (stats == null || stats.StatsSO == null) return;

            var so = stats.StatsSO;
            if (so.defaultWeapon == null) return;

            // WeaponDataSO의 projectilePrefab, muzzleVelocity 가져오기
            var weaponField = so.defaultWeapon.GetType().GetField("projectilePrefab",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (weaponField != null)
                projectilePrefab = weaponField.GetValue(so.defaultWeapon) as GameObject;

            var velocityField = so.defaultWeapon.GetType().GetField("muzzleVelocity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (velocityField != null)
            {
                object val = velocityField.GetValue(so.defaultWeapon);
                if (val is float f) muzzleVelocity = f;
                else if (val is int i) muzzleVelocity = i;
            }

            // projectilePrefab 없으면 폴백 사용
            if (projectilePrefab == null)
                projectilePrefab = fallbackProjectilePrefab;

            if (muzzleVelocity <= 0f)
                muzzleVelocity = 300f;
        }

        /// <summary>
        /// 타겟을 향해 사격한다. EnemyAI의 Engage 상태에서 호출.
        /// </summary>
        public void TryFireAt(Transform target)
        {
            // 네트워크 스폰됐으면 서버에서만, 스폰 안 됐으면 로컬 실행
            if (IsSpawned && !IsServer) return;
            if (target == null || stats == null || stats.StatsSO == null) return;
            if (Time.time < nextShotAllowed) return;

            var so = stats.StatsSO;
            if (muzzle == null) return;

            Vector3 baseDir = (target.position - muzzle.position).normalized;
            float dist = Vector3.Distance(transform.position, target.position);

            // ── 탄퍼짐 계산 ──
            float distRatio = Mathf.Clamp01(dist / so.maxEffectiveRange);
            float spreadDeg = Mathf.Lerp(so.spreadAngleMin, so.spreadAngleMax, distRatio);
            if (dist > so.maxEffectiveRange)
                spreadDeg *= so.rangeSpreadMultiplier;

            Vector3 spreadDir = Quaternion.Euler(
                Random.Range(-1f, 1f) * spreadDeg,
                Random.Range(-1f, 1f) * spreadDeg,
                0) * baseDir;

            // ── 발사 이벤트 (청각 감지용) ──
            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = DamageSystem.AI_SHOOTER_ID,
                weaponId = so.defaultWeapon != null
                    ? (Unity.Collections.FixedString64Bytes)so.defaultWeapon.itemID
                    : default,
                origin = muzzle.position,
                loudness = 1f,
            });

            // ── 발사 방식 분기 ──
            if (projectilePrefab != null)
            {
                FireProjectile(spreadDir, so);
            }
            else
            {
                FireHitscan(spreadDir, so);
            }

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

        // ───────── 투사체 발사 ─────────

        /// <summary>
        /// 투사체를 생성하고 네트워크 스폰한다.
        /// ProjectileController가 이동/충돌/데미지를 처리한다.
        /// </summary>
        private void FireProjectile(Vector3 direction, EnemyStatsSO so)
        {
            GameObject bulletObj = Instantiate(projectilePrefab,
                muzzle.position, Quaternion.LookRotation(direction));

            // ProjectileData 구성
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

            // 네트워크 스폰 (온라인일 때만)
            var netObj = bulletObj.GetComponent<NetworkObject>();
            if (netObj != null && IsSpawned)
            {
                netObj.Spawn();
            }

            // ProjectileController 초기화
            var controller = bulletObj.GetComponent<ProjectileController>();
            if (controller != null)
            {
                controller.Initialize(projectileData, direction, muzzleVelocity);
            }
        }

        // ───────── Hitscan 폴백 ─────────

        /// <summary>
        /// 레이캐스트 기반 즉발 판정. projectilePrefab이 없을 때 사용.
        /// </summary>
        private void FireHitscan(Vector3 spreadDir, EnemyStatsSO so)
        {
            if (Physics.Raycast(muzzle.position, spreadDir, out RaycastHit hit,
                    so.maxEffectiveRange * 1.2f, hitMask))
            {
                var zone = hit.collider.GetComponent<HitZone>();
                if (zone != null && so.defaultAmmo != null && so.defaultWeapon != null)
                {
                    var damageSystem = ServiceLocator.Get<DamageSystem>();
                    if (damageSystem != null)
                    {
                        var victim = zone.GetComponentInParent<NetworkObject>()
                            ?.GetComponent<IDamageable>();
                        if (victim != null)
                        {
                            var projectileData = new ProjectileData
                            {
                                ShooterId = DamageSystem.AI_SHOOTER_ID,
                                BaseDamage = Mathf.RoundToInt(
                                    so.defaultWeapon.damage * so.damageMultiplier),
                                Penetration = so.defaultAmmo.penetration
                                    + so.penetrationModifier,
                                TargetNetId = hit.collider
                                    .GetComponentInParent<NetworkObject>()
                                    ?.NetworkObjectId ?? 0,
                                WasHeadAim = false,
                                Range = hit.distance
                            };
                            damageSystem.ApplyDamage(victim, hit.point, projectileData);
                        }
                    }
                }
            }
        }
    }
}
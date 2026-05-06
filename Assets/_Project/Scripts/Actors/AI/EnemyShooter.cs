using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    public class EnemyShooter : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private LayerMask hitMask = ~0;

        private EnemyStats stats;
        private float nextShotAllowed;
        private int burstCount;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
        }

        public void TryFireAt(Transform target)
        {
            if (!IsServer || target == null || stats == null || stats.StatsSO == null) return;
            if (Time.time < nextShotAllowed) return;

            var so = stats.StatsSO;
            Vector3 baseDir = (target.position - muzzle.position).normalized;
            float dist = Vector3.Distance(transform.position, target.position);

            // ── 탄퍼짐 계산 (SO 슬라이더 값 직접 사용) ──
            float distRatio = Mathf.Clamp01(dist / so.maxEffectiveRange);
            float spreadDeg = Mathf.Lerp(so.spreadAngleMin, so.spreadAngleMax, distRatio);
            if (dist > so.maxEffectiveRange)
                spreadDeg *= so.rangeSpreadMultiplier;

            Vector3 spreadDir = Quaternion.Euler(
                Random.Range(-1f, 1f) * spreadDeg,
                Random.Range(-1f, 1f) * spreadDeg,
                0) * baseDir;

            // ── 발사 이벤트 ──
            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = DamageSystem.AI_SHOOTER_ID,
                weaponId = so.defaultWeapon != null
                    ? (Unity.Collections.FixedString64Bytes)so.defaultWeapon.itemID
                    : default,
                origin = muzzle.position,
                loudness = 1f,
            });

            // ── 레이캐스트 판정 ──
            if (Physics.Raycast(muzzle.position, spreadDir, out RaycastHit hit,
                    so.maxEffectiveRange * 1.2f, hitMask))
            {
                var zone = hit.collider.GetComponent<HitZone>();
                if (zone != null && so.defaultAmmo != null && so.defaultWeapon != null)
                {
                    var hitInfo = new HitInfo
                    {
                        victim    = zone.GetComponentInParent<NetworkObject>()?.gameObject,
                        zone      = zone.ZoneType,
                        hitPoint  = hit.point,
                        hitNormal = hit.normal,
                        distance  = hit.distance,
                    };

                    var damageSystem = ServiceLocator.Get<DamageSystem>();
                    if (damageSystem != null)
                    {
                        var victim = hitInfo.victim?.GetComponent<IDamageable>();
                        if (victim != null)
                        {
                            var netObj = hitInfo.victim.GetComponent<NetworkObject>();
                            var projectileData = new ProjectileData
                            {
                                ShooterId   = DamageSystem.AI_SHOOTER_ID,
                                BaseDamage  = Mathf.RoundToInt(so.defaultWeapon.damage * so.damageMultiplier),
                                Penetration = so.defaultAmmo.penetration + so.penetrationModifier,
                                TargetNetId = netObj != null ? netObj.NetworkObjectId : 0,
                                WasHeadAim  = hitInfo.zone == BodyPart.Head,
                                Range       = hitInfo.distance
                            };
                            damageSystem.ApplyDamage(victim, hitInfo.hitPoint, projectileData);
                        }
                    }
                }
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
    }
}
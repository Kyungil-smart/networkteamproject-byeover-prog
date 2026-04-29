using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// v1.1 §8 — 명중률 기반 탄퍼짐 + 점사 페이싱.
    /// 서버 전용. DamageSystem을 직접 호출 (ServerRpc 없음).
    /// </summary>
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

            float effective = so.accuracy;
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist > so.maxEffectiveRange) effective *= 0.7f;

            float spreadDeg = (1f - effective) * 4.6f;
            Vector3 spreadDir = Quaternion.Euler(
                Random.Range(-1f, 1f) * spreadDeg,
                Random.Range(-1f, 1f) * spreadDeg,
                0) * baseDir;

            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = DamageSystem.AI_SHOOTER_ID,
                weaponId = so.defaultWeapon != null ? (Unity.Collections.FixedString64Bytes)so.defaultWeapon.itemID : default,
                origin = muzzle.position,
                loudness = 1f,
            });

            if (Physics.Raycast(muzzle.position, spreadDir, out RaycastHit hit, so.maxEffectiveRange, hitMask))
            {
                var zone = hit.collider.GetComponent<DeadZone.Actors.HitZone>();
                if (zone != null)
                {
                    var hitInfo = new HitInfo
                    {
                        victim = zone.GetComponentInParent<NetworkObject>()?.gameObject,
                        zone = zone.ZoneType,
                        hitPoint = hit.point,
                        hitNormal = hit.normal,
                        distance = hit.distance,
                    };
                    var damageSystem = ServiceLocator.Get<DamageSystem>();
                    if (damageSystem != null && so.defaultWeapon != null)
                    {
                        var defaultAmmo = ScriptableObject.CreateInstance<AmmoDataSO>();
                        defaultAmmo.penetration = 2;
                        defaultAmmo.damageMultiplier = 1f;
                        // 탄환 방식으로 수정 필요 damageSystem.ApplyDamage(hitInfo, defaultAmmo, so.defaultWeapon, DamageSystem.AI_SHOOTER_ID);
                    }
                }
            }

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

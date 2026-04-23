using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// Owner가 로컬에서 먼저 발사하여 즉각 피드백을 주고, ServerRpc로 서버가 검증 후 데미지를 적용한다.
    /// Hitscan vs Projectile 결정은 무기팀의 몫 — 이 스텁은 Hitscan을 사용한다.
    /// </summary>
    public class ShootingSystem : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform muzzleTransform;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private float maxRange = 600f;

        private EquipmentSlots equipment;
        private float nextFireAllowed;

        private void Awake()
        {
            equipment = GetComponent<EquipmentSlots>();
        }

        public void TryFire()
        {
            if (!IsOwner) return;
            if (Time.time < nextFireAllowed) return;

            var weapon = equipment != null ? equipment.GetCurrentWeapon() : null;
            if (weapon == null) return;
            nextFireAllowed = Time.time + (1f / Mathf.Max(0.1f, weapon.fireRate));

            Vector3 origin = muzzleTransform != null ? muzzleTransform.position : transform.position;
            Vector3 dir = muzzleTransform != null ? muzzleTransform.forward : transform.forward;

            FireServerRpc(origin, dir, weapon.itemID);
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 origin, Vector3 dir, FixedString64Bytes weaponId, ServerRpcParams rpc = default)
        {
            ulong shooterId = rpc.Receive.SenderClientId;

            var weapon = equipment != null ? equipment.Lookup(weaponId.ToString()) as WeaponDataSO : null;
            if (weapon == null) return;

            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = shooterId,
                weaponId = weaponId,
                origin = origin,
                loudness = 1f,
            });

            if (Physics.Raycast(origin, dir, out RaycastHit hitInfo, maxRange, hitMask))
            {
                var hitZone = hitInfo.collider.GetComponent<HitZone>();
                if (hitZone == null) return;

                var hit = new HitInfo
                {
                    victim = hitZone.GetComponentInParent<NetworkObject>()?.gameObject,
                    zone = hitZone.ZoneType,
                    hitPoint = hitInfo.point,
                    hitNormal = hitInfo.normal,
                    distance = hitInfo.distance,
                };

                var defaultAmmo = ScriptableObject.CreateInstance<AmmoDataSO>();
                defaultAmmo.penetration = 3;
                defaultAmmo.damageMultiplier = 1f;

                ServiceLocator.Get<DamageSystem>()?.ApplyDamage(hit, defaultAmmo, weapon, shooterId);
            }
        }
    }
}

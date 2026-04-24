using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 서버 권위 데미지 허브. Map_X > Systems > DamageSystem에 부착된다.
    /// ServiceLocator를 통한 싱글톤.
    /// </summary>
    /// <remarks>
    /// IDamageable은 데미지 적용만 담당. IArmored는 별도로 질의된다
    /// victim GameObject에서 조회하므로 방어구 없는 엔티티가 헬멧 메서드를 가짜로 구현할 필요 없다.
    /// v1.1 §6.3.3: AI가 플레이어 머리를 맞혀도 헤드샷 배율이 아닌 토르소 배율로 다운그레이드된다.
    /// </remarks>
    public class DamageSystem : NetworkBehaviour
    {
        public const ulong AI_SHOOTER_ID = ulong.MaxValue;

        public override void OnNetworkSpawn()
        {
            ServiceLocator.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            ServiceLocator.Unregister<DamageSystem>();
        }

        public void ApplyDamage(HitInfo hit, AmmoDataSO ammo, WeaponDataSO weapon, ulong shooterClientId)
        {
            if (!IsServer) return;
            if (hit.victim == null || ammo == null || weapon == null) return;

            var victim = hit.victim.GetComponent<IDamageable>();
            if (victim == null || victim.IsDead) return;

            BodyPart effectiveZone = hit.zone;
            bool effectiveCritical = hit.zone == BodyPart.Head;

            if (IsAttackerAI(shooterClientId) && victim.IsPlayer && hit.zone == BodyPart.Head)
            {
                effectiveZone = BodyPart.Torso;
                effectiveCritical = false;
            }

            var armored = hit.victim.GetComponent<IArmored>();
            DamageResult result = CalculateDamage(armored, ammo, weapon, effectiveZone);

            if (armored != null && result.armorDamage > 0)
            {
                if (effectiveZone == BodyPart.Head) armored.DamageHelmetDurability(result.armorDamage);
                else if (effectiveZone == BodyPart.Torso) armored.DamageArmorDurability(result.armorDamage);
            }

            victim.ApplyDamage(result.finalDamage, shooterClientId, hit);

            if (effectiveCritical && result.finalDamage > 0)
            {
                EventBus.Publish(new CriticalHitEvent
                {
                    attackerClientId = shooterClientId,
                    zone = hit.zone,
                    damage = result.finalDamage,
                });
            }
        }

        private bool IsAttackerAI(ulong shooterClientId)
        {
            if (shooterClientId == AI_SHOOTER_ID) return true;
            if (NetworkManager.Singleton == null) return false;
            return !NetworkManager.Singleton.ConnectedClients.ContainsKey(shooterClientId);
        }

        private DamageResult CalculateDamage(IArmored armored, AmmoDataSO ammo, WeaponDataSO weapon, BodyPart zone)
        {
            if (armored == null)
            {
                return PenetrationCalculator.CalculateUnarmored(ammo, zone, weapon.damage);
            }

            switch (zone)
            {
                case BodyPart.Head:
                    var helmet = armored.GetEquippedHelmet();
                    if (helmet != null && armored.GetHelmetDurability() > 0)
                    {
                        return PenetrationCalculator.CalculateHelmet(
                            ammo, helmet, armored.GetHelmetDurability(), zone, weapon.damage);
                    }
                    break;
                case BodyPart.Torso:
                    var armorPiece = armored.GetEquippedArmor();
                    if (armorPiece != null && armored.GetArmorDurability() > 0)
                    {
                        return PenetrationCalculator.CalculateArmor(
                            ammo, armorPiece, armored.GetArmorDurability(), zone, weapon.damage);
                    }
                    break;
            }
            return PenetrationCalculator.CalculateUnarmored(ammo, zone, weapon.damage);
        }
    }
}

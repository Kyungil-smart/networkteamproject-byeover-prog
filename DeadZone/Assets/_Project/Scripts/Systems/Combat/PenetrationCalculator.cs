using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 플레이어/적/NPC의 데미지 경로가 공유하는 정적 유틸리티.
    /// GDD §4.3 관통 확률 테이블을 구현한다.
    /// </summary>
    public static class PenetrationCalculator
    {
        public static DamageResult CalculateHelmet(
            AmmoDataSO ammo, HelmetDataSO helmet, float helmetDurability,
            BodyPart zone, float baseDamage)
        {
            int armorClassValue = (int)helmet.helmetClass;
            return CalculateInternal(ammo, armorClassValue, helmet.blockChance, zone, baseDamage);
        }

        public static DamageResult CalculateArmor(
            AmmoDataSO ammo, ArmorDataSO armor, float armorDurability,
            BodyPart zone, float baseDamage)
        {
            int armorClassValue = (int)armor.armorClass;
            return CalculateInternal(ammo, armorClassValue, armor.blockChance, zone, baseDamage);
        }

        public static DamageResult CalculateUnarmored(
            AmmoDataSO ammo, BodyPart zone, float baseDamage)
        {
            float damage = baseDamage * HitInfo.GetZoneMultiplier(zone) * ammo.damageMultiplier;
            return new DamageResult
            {
                finalDamage = Mathf.RoundToInt(damage),
                penetrated = true,
                armorDamage = 0f,
            };
        }

        private static DamageResult CalculateInternal(
            AmmoDataSO ammo, int armorClass, float blockChance,
            BodyPart zone, float baseDamage)
        {
            int diff = ammo.penetration - armorClass;
            var (penChance, dmgReduction) = GetPenTableEntry(diff);

            bool penetrated = Random.value < penChance;
            float zoneMul = HitInfo.GetZoneMultiplier(zone);
            float ammoMul = ammo.damageMultiplier;

            float damage;
            if (penetrated)
            {
                damage = baseDamage * zoneMul * ammoMul * (1f - dmgReduction);
            }
            else
            {
                damage = baseDamage * zoneMul * ammoMul * (1f - blockChance);
            }

            return new DamageResult
            {
                finalDamage = Mathf.RoundToInt(damage),
                penetrated = penetrated,
                armorDamage = penetrated ? 8f : 12f,
            };
        }

        private static (float, float) GetPenTableEntry(int diff)
        {
            if (diff >= 2)  return (1.00f, 0.00f);
            if (diff == 1)  return (1.00f, 0.00f);
            if (diff == 0)  return (0.90f, 0.10f);
            if (diff == -1) return (0.55f, 0.30f);
            if (diff == -2) return (0.25f, 0.55f);
            if (diff == -3) return (0.10f, 0.75f);
            if (diff == -4) return (0.03f, 0.90f);
            return (0.01f, 0.95f);
        }
    }
}

using UnityEngine;
using Unity.Netcode;

using DeadZone.Core;
using DeadZone.Actors;

namespace DeadZone.Systems
{
    /// <summary>
    /// 플레이어/적/NPC의 데미지 경로가 공유하는 정적 유틸리티.
    /// GDD §4.3 관통 확률 테이블을 구현한다.
    /// </summary>
    public static class PenetrationCalculator
    {
        private const float BaseHeadshotMultiplier = 3.0f;
        private const float HeadshotReductionPerExtraPlayer = 0.15f;

        public static DamageResult Calculate(
            IArmored armored, BodyPart effectiveZone, ProjectileData data)
        {
            // 오버라이드 없이 방어구 체크로 분기
            // 1. 방어구 체크 (Head면 헬멧, Torso면 갑옷 질의)
            int armorClass = 0;
            float blockChance = 0;
            bool hasValidArmor = false;

            if (armored != null)
            {
                if (effectiveZone == BodyPart.Head)
                {
                    var helmet = armored.GetEquippedHelmet();
                    if (helmet != null && armored.GetHelmetDurability() > 0)
                    {
                        armorClass = (int)helmet.helmetClass;
                        blockChance = helmet.blockChance;
                        hasValidArmor = true;
                    }
                }
                else // 모든 비치명타는 Torso 판정
                {
                    var armor = armored.GetEquippedArmor();
                    if (armor != null && armored.GetArmorDurability() > 0)
                    {
                        armorClass = (int)armor.armorClass;
                        blockChance = armor.blockChance;
                        hasValidArmor = true;
                    }
                }
            }

            // 2. 관통 및 데미지 연산
            // 치명타에 따른 피해 배율
            float multiplier = (effectiveZone == BodyPart.Head) ? GetScaledHeadshotMultiplier() : 1.0f;
        
            // 방어구가 없을 경우 관통 연산 없이 반환
            if (!hasValidArmor)
            {
                return new DamageResult {
                    finalDamage = Mathf.RoundToInt(data.BaseDamage * multiplier),
                    penetrated = true,
                    armorDamage = 0f
                };
            }

            // 3. 관통 테이블 연산
            // 관통력 - 아머 등급 (관통 테이블은 GetPenTableEntry에서 정의)
            int diff = data.Penetration - armorClass;
            var (penChance, penetrationReductionScale) = GetPenTableEntry(diff);
            // 확률 기반 관통 여부 판단
            bool penetrated = Random.value < penChance;

            float baseReduction = GetArmorClassDamageReduction(armorClass);
            float blockBonus = Mathf.Clamp01(blockChance) * 0.5f;
            float fullReduction = Mathf.Clamp01(baseReduction + blockBonus);
            float appliedReduction = penetrated
                ? Mathf.Clamp01(fullReduction * penetrationReductionScale)
                : fullReduction;

            float finalDmg = data.BaseDamage * multiplier * (1f - appliedReduction);
            float armorDamage = penetrated
                ? Mathf.Max(4f, data.BaseDamage * 0.12f)
                : Mathf.Max(8f, data.BaseDamage * 0.22f);

            return new DamageResult {
                finalDamage = Mathf.Max(1, Mathf.RoundToInt(finalDmg)),
                penetrated = penetrated,
                armorDamage = armorDamage
            };
        }

        private static (float, float) GetPenTableEntry(int diff)
        {
            if (diff >= 2)  return (1.00f, 0.15f);
            if (diff == 1)  return (1.00f, 0.25f);
            if (diff == 0)  return (0.90f, 0.45f);
            if (diff == -1) return (0.55f, 0.65f);
            if (diff == -2) return (0.25f, 0.80f);
            if (diff == -3) return (0.10f, 0.90f);
            if (diff == -4) return (0.03f, 0.95f);
            return (0.01f, 1.00f);
        }

        private static float GetArmorClassDamageReduction(int armorClass)
        {
            return armorClass switch
            {
                <= 1 => 0.20f,
                2 => 0.30f,
                3 => 0.40f,
                4 => 0.50f,
                5 => 0.60f,
                _ => 0.70f,
            };
        }

        private static float GetScaledHeadshotMultiplier()
        {
            int playerCount = 1;
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening)
                playerCount = Mathf.Max(1, networkManager.ConnectedClientsIds.Count);

            float reductionScale = 1f - HeadshotReductionPerExtraPlayer * Mathf.Max(0, playerCount - 1);
            return Mathf.Max(1f, BaseHeadshotMultiplier * reductionScale);
        }
    }
}

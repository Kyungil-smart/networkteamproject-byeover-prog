using UnityEngine;

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
            float multiplier = (effectiveZone == BodyPart.Head) ? 3.0f : 1.0f;
        
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
            var (penChance, dmgReduction) = GetPenTableEntry(diff);
            // 확률 기반 관통 여부 판단
            bool penetrated = Random.value < penChance;

            // 관통 여부에 따른 최종 피해 계산
            float finalDmg = penetrated ? 
                data.BaseDamage * multiplier * (1f - dmgReduction) :
                data.BaseDamage * multiplier * (1f - blockChance);

            return new DamageResult {
                finalDamage = Mathf.RoundToInt(finalDmg),
                penetrated = penetrated,
                armorDamage = penetrated ? 8f : 12f
            };
        }

        // 관통 테이블
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

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Actors;

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

        public void ApplyDamage(
            IDamageable victim, 
            Vector3 hitPoint, 
            ProjectileData data)
        {
            if (!IsServer || victim == null || victim.IsDead) return;

            var victimNetObj = (victim as MonoBehaviour)?
                .GetComponent<NetworkObject>();
    
            // 1. 의도 기반 판정 (물리적 hitZone은 아예 인자로 받지 않음)
            bool isCritical = false;
            if (victimNetObj != null && 
                victimNetObj.NetworkObjectId == data.TargetNetId)
            {
                isCritical = data.WasHeadAim;
            }

            // 2. 판정 부위 강제 고정
            // 치명타면 Head(3.0x), 아니면 무조건 Torso(1.0x)
            BodyPart effectiveZone = isCritical ? 
                BodyPart.Head : BodyPart.Torso;

            // 3. 최종 계산 및 적용
            var armored = (victim as MonoBehaviour)?.GetComponent<IArmored>();
            DamageResult result = PenetrationCalculator.Calculate(
                armored, effectiveZone, data);

            victim.ApplyDamage(result.finalDamage, data.ShooterId, hitPoint);

            // 4. 아머 내구도 소모 처리
            if (armored != null && result.armorDamage > 0)
            {
                if (effectiveZone == BodyPart.Head) 
                    armored.DamageHelmetDurability(result.armorDamage);
                else 
                    armored.DamageArmorDurability(result.armorDamage);
            }
        }

        private DamageResult CalculateFinalDamage(
            IArmored armored, BodyPart zone, ProjectileData data)
        {
            // PenetrationCalculator가 HitInfo대신 ProjectileData를 받도록 수정
            // isCritical이 true라면 내부적으로 헤드셋 배율을 강제 적용
            return PenetrationCalculator.Calculate(armored, zone, data);
        }
    }
}

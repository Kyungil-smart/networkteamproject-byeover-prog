using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// ShootingSystem -> DamageSystem으로 전달되는 피격 정보.
    /// 순수 데이터, 동작 없음.
    /// </summary>
    public struct HitInfo
    {
        public GameObject victim;
        public BodyPart zone;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
        public float distance;

        /// <summary>피격 부위별 데미지 배율 (Head=3.0, Torso=1.0, Limb=0.65).</summary>
        public static float GetZoneMultiplier(BodyPart zone) => zone switch
        {
            BodyPart.Head  => 3.0f,
            BodyPart.Torso => 1.0f,
            BodyPart.Limb  => 0.65f,
            _ => 1.0f,
        };
    }

    /// <summary>
    /// 관통 계산의 결과 출력.
    /// </summary>
    public struct DamageResult
    {
        public int finalDamage;
        public bool penetrated;
        public float armorDamage;
    }
}

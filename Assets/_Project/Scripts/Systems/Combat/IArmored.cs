using DeadZone.Core;


namespace DeadZone.Systems
{
    /// <summary>
    /// 방어구를 착용하는 엔티티가 구현한다. 플레이어는 헬멧+아머 풀세트.
    /// 적 AI는 아머만 (헬멧은 null, 내구도 0). DamageSystem이 모든 IDamageable에
    /// 가짜 방어구를 강제하지 않고 capability 기반으로 분기할 수 있게 한다
    /// helmet methods.
    /// </summary>
    public interface IArmored
    {
        HelmetDataSO GetEquippedHelmet();
        ArmorDataSO GetEquippedArmor();
        float GetHelmetDurability();
        float GetArmorDurability();
        void DamageHelmetDurability(float amount);
        void DamageArmorDurability(float amount);
    }
}

namespace DeadZone.Systems
{
    /// <summary>
    /// DamageSystem이 데미지를 줄 수 있는 모든 것. 의도적으로 슬림하게 설계.
    /// 방어구, 부활, 루팅 드롭 관심사는 각각 IArmored, IRevivable, IRecoverable로 분리.
    /// </summary>
    public interface IDamageable
    {
        bool IsPlayer { get; }
        bool IsDead { get; }
        void ApplyDamage(int damage, ulong attackerClientId, HitInfo hit);
    }
}

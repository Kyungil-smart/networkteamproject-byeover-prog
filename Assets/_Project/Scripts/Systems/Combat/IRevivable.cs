namespace DeadZone.Systems
{
    /// <summary>
    /// 다운된 후 부활될 수 있는 엔티티가 구현한다.
    /// 플레이어 전용 — 적은 그냥 죽는다.
    /// </summary>
    public interface IRevivable
    {
        bool IsKnocked { get; }
        bool CanBeRevived { get; }
        float ReviveHpAmount { get; }
        void OnReviveBegin(ulong reviverClientId);
        void OnReviveCancel();
        void OnReviveComplete(ulong reviverClientId);
    }
}

namespace DeadZone.Core
{
    /// <summary>
    /// 플레이어의 현재 소지 무게 또는 최대 소지 무게가 변경될 때 발행됩니다.
    /// Inventory UI, HUD, 이동 패널티 시스템이 이 이벤트를 구독할 수 있습니다.
    /// </summary>
    public struct PlayerCarryWeightChangedEvent : IGameEvent
    {
        public ulong clientId;

        public float oldCurrentWeightKg;
        public float newCurrentWeightKg;

        public float oldMaxCarryWeightKg;
        public float newMaxCarryWeightKg;

        public float housingCarryWeightBonusKg;

        public bool isOverWeight;
        public float weightRatio;
    }
}
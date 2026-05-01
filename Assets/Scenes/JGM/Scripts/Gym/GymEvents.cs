using DeadZone.Core;

namespace DeadZone.Core
{
    /// <summary>
    /// 헬스장 레벨에 따른 소지 무게 보너스가 변경되었을 때 발행되는 이벤트입니다.
    /// 추후 PlayerStats, Inventory UI, HUD가 이 이벤트를 구독해서 실제 수치와 화면을 갱신합니다.
    /// </summary>
    public struct GymCarryWeightBonusChangedEvent : IGameEvent
    {
        public int level;
        public float carryWeightBonusKg;
    }
}

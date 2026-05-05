using DeadZone.Core;

namespace DeadZone.Core
{
    /// <summary>
    /// 침대 시설의 최대 스태미너 보너스가 변경되었을 때 발행되는 이벤트입니다.
    /// PlayerStats, UI가 완성되면 이 이벤트를 구독해서 실제 능력치에 반영합니다.
    /// </summary>
    public struct BedStaminaBonusChangedEvent : IGameEvent
    {
        public int level;
        public int maxStaminaBonus;
    }
}

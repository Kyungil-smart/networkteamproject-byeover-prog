namespace DeadZone.Core
{
    /// <summary>
    /// 주방 레벨에 따른 최대 스태미너 보너스가 변경되었을 때 발행되는 이벤트입니다.
    /// 추후 PlayerStats, HUD, Stamina UI가 이 이벤트를 구독해서 실제 수치와 화면을 갱신합니다.
    /// </summary>
    public struct KitchenStaminaBonusChangedEvent : IGameEvent
    {
        public int level;
        public int maxStaminaBonus;
    }
}

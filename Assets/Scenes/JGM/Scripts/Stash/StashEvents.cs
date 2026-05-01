namespace DeadZone.Core
{
    /// <summary>
    /// 보관함 레벨에 따른 스태쉬 크기가 변경되었을 때 발행되는 이벤트입니다.
    /// 추후 Stash UI, 실제 보관함 인벤토리, 저장 시스템이 이 이벤트를 구독해서 갱신합니다.
    /// </summary>
    public struct StashSizeChangedEvent : IGameEvent
    {
        public int level;
        public int columns;
        public int rows;
        public int totalSlots;
    }
}

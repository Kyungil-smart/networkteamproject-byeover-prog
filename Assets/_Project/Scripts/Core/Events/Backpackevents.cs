using Unity.Collections;


namespace DeadZone.Core
{
    /// <summary>
    /// 가방 장착/탈착/교체 시 EquipmentSlots가 발행. GridInventory가 구독하여 그리드 리사이즈.
    /// </summary>
    /// <remarks>
    /// newBackpackId가 빈 문자열이면 가방 탈착을 의미.
    /// </remarks>
    public struct BackpackChangedEvent : IGameEvent
    {
        public ulong clientId;
        public FixedString64Bytes oldBackpackId;
        public FixedString64Bytes newBackpackId;
    }

    /// <summary>
    /// 가방 탈착 시 가방 영역에 있는 아이템들이 베이스 영역으로 못 옮겨질 때 발행.
    /// UI가 구독하여 "가방 안의 아이템을 비워야 벗을 수 있습니다" 토스트 표시.
    /// </summary>
    public struct BackpackUnequipFailedEvent : IGameEvent
    {
        public ulong clientId;
    }
}
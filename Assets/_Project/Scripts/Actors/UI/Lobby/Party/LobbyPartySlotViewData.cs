namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 파티 슬롯 UI에 전달되는 표시 전용 데이터입니다.
    /// </summary>
    public struct LobbyPartySlotViewData
    {
        public bool HasPlayer;
        public ulong ClientId;
        public string DisplayName;
        public bool IsHost;
        public bool IsReady;
        public bool IsLocalPlayer;

        public static LobbyPartySlotViewData Empty => new LobbyPartySlotViewData
        {
            HasPlayer = false,

            // 빈 슬롯은 실제 ClientId와 혼동되지 않도록 MaxValue를 사용
            // Empty 여부는 ClientId가 아니라 HasPlayer로 판단한다.
            ClientId = ulong.MaxValue,

            DisplayName = string.Empty,
            IsHost = false,
            IsReady = false,
            IsLocalPlayer = false
        };
    }
}
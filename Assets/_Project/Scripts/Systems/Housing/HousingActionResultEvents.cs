using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    // 시설 업그레이드 결과를 UI나 로그 시스템에 전달하는 이벤트
    public struct HousingUpgradeResultEvent : IGameEvent
    {
        public string facilityName;
        public int previousLevel;
        public int currentLevel;
        public bool success;
        public string reason;
    }

    // 시설 제작 결과를 UI나 로그 시스템에 전달하는 이벤트
    public struct HousingCraftResultEvent : IGameEvent
    {
        public string facilityName;
        public string recipeId;
        public string resultItemId;
        public int resultCount;
        public bool success;
        public string reason;
    }
}
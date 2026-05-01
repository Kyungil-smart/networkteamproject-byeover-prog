
namespace DeadZone.Core
{
    /// <summary>
    /// 작업대 제작 해금 상태가 바뀌었을 때 발행되는 이벤트입니다.
    /// 추후 UI, 플레이어 성장 표시, 제작 목록 갱신 시스템이 이 이벤트를 구독해서 사용할 수 있습니다.
    /// </summary>
    public struct WorkbenchCraftingUnlockChangedEvent : IGameEvent
    {
        public int level;
        public string unlockedGradeLabel;
        public int maxRequiredFacilityLevel;
    }

    /// <summary>
    /// 작업대 제작이 성공했을 때 발행되는 이벤트입니다.
    /// 추후 UI 알림, 사운드, 퀘스트 진행 체크와 연결할 수 있습니다.
    /// </summary>
    public struct WorkbenchCraftSucceededEvent : IGameEvent
    {
        public string recipeId;
        public string resultItemId;
        public int resultCount;
    }

    /// <summary>
    /// 작업대 제작이 실패했을 때 발행되는 이벤트입니다.
    /// 현재는 테스트 로그 확인용이며, 추후 UI 실패 메시지에 연결할 수 있습니다.
    /// </summary>
    public struct WorkbenchCraftFailedEvent : IGameEvent
    {
        public string recipeId;
        public string reason;
    }
}

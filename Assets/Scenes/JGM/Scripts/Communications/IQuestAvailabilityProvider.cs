namespace DeadZone.Systems
{
    /// <summary>
    /// 현재 조건에서 특정 퀘스트를 수락할 수 있는지 검사하는 인터페이스입니다.
    /// 추후 QuestManager가 퀘스트 수락 전에 이 인터페이스를 호출하면 됩니다.
    /// </summary>
    public interface IQuestAvailabilityProvider
    {
        bool CanAcceptQuest(string questId, out string failReason);
    }
}

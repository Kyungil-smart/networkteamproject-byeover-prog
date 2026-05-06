namespace DeadZone.Systems
{
    /// <summary>
    /// 퀘스트 완료 여부만 읽기 위한 인터페이스입니다.
    /// 통신장비가 QuestManager의 내부 구현을 직접 알지 않도록 분리합니다.
    /// </summary>
    public interface IQuestCompletionReader
    {
        bool IsQuestCompleted(string questId);
    }
}

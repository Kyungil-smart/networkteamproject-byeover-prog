using DeadZone.Core;

namespace DeadZone.Systems.Quests
{
    public interface IQuestQuery
    {
        bool TryGetQuestData(string questId, out QuestDataSO questData);
        int GetObjectiveProgress(ulong clientId, string questId, string targetId);
        void ReportProgress(ulong clientId, ObjectiveType type, string targetId, int amount);
        bool IsQuestCompleted(ulong clientId, string questId);
        bool IsQuestActive(ulong clientId, string questId);
    }
}

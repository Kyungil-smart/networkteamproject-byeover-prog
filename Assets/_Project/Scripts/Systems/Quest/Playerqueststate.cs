using System.Collections.Generic;
using System.Linq;
using DeadZone.Network;

namespace DeadZone.Systems.Quests
{
    public class PlayerQuestState
    {
        public HashSet<string> ActiveQuestIds { get; } = new();
        public HashSet<string> CompletedQuestIds { get; } = new();
        public HashSet<string> RewardClaimedQuestIds { get; } = new();
        public HashSet<string> UnlockedZones { get; } = new();
        public HashSet<string> PendingCompletionIds { get; } = new();

        public Dictionary<string, (int current, int required)> RaidQuestProgress { get; } = new();

        public Dictionary<string, (int current, int required)> ObjectiveProgress => RaidQuestProgress;

        public static string MakeKey(string questId, string targetId)
            => $"{questId}:{targetId}";

        public int GetProgress(string questId, string targetId)
        {
            string key = MakeKey(questId, targetId);
            return RaidQuestProgress.TryGetValue(key, out var value) ? value.current : 0;
        }

        public int AddProgress(string questId, string targetId, int amount, int requiredCount)
        {
            string key = MakeKey(questId, targetId);

            if (!RaidQuestProgress.TryGetValue(key, out var value))
                value = (0, requiredCount);

            int newCount = System.Math.Min(value.current + amount, requiredCount);
            RaidQuestProgress[key] = (newCount, requiredCount);
            return newCount;
        }

        public bool AreAllObjectivesComplete(string questId)
        {
            bool foundObjective = false;

            foreach (var kvp in RaidQuestProgress)
            {
                if (!kvp.Key.StartsWith(questId + ":"))
                    continue;

                foundObjective = true;

                if (kvp.Value.current < kvp.Value.required)
                    return false;
            }

            return foundObjective;
        }

        public void ResetRaidQuestProgress()
        {
            var keys = RaidQuestProgress.Keys.ToList();
            foreach (string key in keys)
            {
                var value = RaidQuestProgress[key];
                RaidQuestProgress[key] = (0, value.required);
            }

            PendingCompletionIds.Clear();
        }

        public void WriteToCloudProgress(ProgressData progress)
        {
            progress.personalActiveQuestIds = ActiveQuestIds.ToList();
            progress.personalCompletedQuestIds = CompletedQuestIds.ToList();
            progress.rewardClaimedQuestIds = RewardClaimedQuestIds.ToList();
            progress.unlockedZones = UnlockedZones.ToList();

            progress.pendingCompletionIds = new List<string>();
            progress.questObjectives = new List<QuestObjectiveProgress>();
        }

        public void ReadFromCloudProgress(ProgressData progress)
        {
            ActiveQuestIds.Clear();
            CompletedQuestIds.Clear();
            RewardClaimedQuestIds.Clear();
            UnlockedZones.Clear();
            PendingCompletionIds.Clear();
            RaidQuestProgress.Clear();

            if (progress.personalActiveQuestIds != null)
            {
                foreach (string id in progress.personalActiveQuestIds)
                    ActiveQuestIds.Add(id);
            }

            if (progress.personalCompletedQuestIds != null)
            {
                foreach (string id in progress.personalCompletedQuestIds)
                    CompletedQuestIds.Add(id);
            }

            if (progress.rewardClaimedQuestIds != null)
            {
                foreach (string id in progress.rewardClaimedQuestIds)
                    RewardClaimedQuestIds.Add(id);
            }

            if (progress.unlockedZones != null)
            {
                foreach (string id in progress.unlockedZones)
                    UnlockedZones.Add(id);
            }
        }
    }
}

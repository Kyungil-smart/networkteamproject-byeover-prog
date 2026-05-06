using System.Collections.Generic;
using System.Linq;
using DeadZone.Network;

namespace DeadZone.Systems.Quests
{
    public class PlayerQuestState
    {
        /// <summary>현재 수주 중인 퀘스트 ID 목록.</summary>
        public HashSet<string> ActiveQuestIds { get; } = new();

        /// <summary>완료한 퀘스트 ID 목록.</summary>
        public HashSet<string> CompletedQuestIds { get; } = new();

        /// <summary>해금된 구역 ID 목록.</summary>
        public HashSet<string> UnlockedZones { get; } = new();

        /// <summary>
        /// objective별 진행 카운트.
        /// Key = "questId:targetId" (예: "Q1:Enemy_Zone1_Any")
        /// Value = (currentCount, requiredCount)
        /// </summary>
        public Dictionary<string, (int current, int required)> ObjectiveProgress { get; } = new();

        // ─────────────────────────────────────────────
        //  키 생성 유틸
        // ─────────────────────────────────────────────

        public static string MakeKey(string questId, string targetId)
            => $"{questId}:{targetId}";

        // ─────────────────────────────────────────────
        //  진행 조회 / 보고
        // ─────────────────────────────────────────────

        public int GetProgress(string questId, string targetId)
        {
            string key = MakeKey(questId, targetId);
            return ObjectiveProgress.TryGetValue(key, out var val) ? val.current : 0;
        }

        /// <summary>
        /// objective 카운트를 amount만큼 증가. requiredCount 초과는 하지 않음.
        /// </summary>
        /// <returns>증가 후 현재 카운트.</returns>
        public int AddProgress(string questId, string targetId, int amount, int requiredCount)
        {
            string key = MakeKey(questId, targetId);

            if (!ObjectiveProgress.TryGetValue(key, out var val))
                val = (0, requiredCount);

            int newCount = System.Math.Min(val.current + amount, requiredCount);
            ObjectiveProgress[key] = (newCount, requiredCount);
            return newCount;
        }

        /// <summary>특정 퀘스트의 모든 objective가 완료되었는지 확인.</summary>
        public bool AreAllObjectivesComplete(string questId)
        {
            foreach (var kvp in ObjectiveProgress)
            {
                if (!kvp.Key.StartsWith(questId + ":")) continue;
                if (kvp.Value.current < kvp.Value.required) return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────
        //  Firestore 변환: 저장
        // ─────────────────────────────────────────────

        /// <summary>런타임 상태 → ProgressData 필드에 복사 (CloudSaveSystem용).</summary>
        public void WriteToCloudProgress(ProgressData progress)
        {
            progress.personalActiveQuestIds = ActiveQuestIds.ToList();
            progress.personalCompletedQuestIds = CompletedQuestIds.ToList();
            progress.unlockedZones = UnlockedZones.ToList();

            progress.questObjectives = new List<QuestObjectiveProgress>();
            foreach (var kvp in ObjectiveProgress)
            {
                // key = "Q1:Enemy_Zone1_Any"
                string[] parts = kvp.Key.Split(':');
                if (parts.Length < 2) continue;

                progress.questObjectives.Add(new QuestObjectiveProgress
                {
                    questId = parts[0],
                    targetId = string.Join(":", parts.Skip(1)), // targetId에 ':'가 포함될 경우 대비
                    current = kvp.Value.current,
                    required = kvp.Value.required
                });
            }
        }

        // ─────────────────────────────────────────────
        //  Firestore 변환: 로드
        // ─────────────────────────────────────────────

        /// <summary>ProgressData → 런타임 상태 복원 (CloudSaveLoadedEvent 수신 시).</summary>
        public void ReadFromCloudProgress(ProgressData progress)
        {
            ActiveQuestIds.Clear();
            CompletedQuestIds.Clear();
            UnlockedZones.Clear();
            ObjectiveProgress.Clear();

            if (progress.personalActiveQuestIds != null)
                foreach (var id in progress.personalActiveQuestIds)
                    ActiveQuestIds.Add(id);

            if (progress.personalCompletedQuestIds != null)
                foreach (var id in progress.personalCompletedQuestIds)
                    CompletedQuestIds.Add(id);

            if (progress.unlockedZones != null)
                foreach (var id in progress.unlockedZones)
                    UnlockedZones.Add(id);

            if (progress.questObjectives != null)
            {
                foreach (var obj in progress.questObjectives)
                {
                    string key = MakeKey(obj.questId, obj.targetId);
                    ObjectiveProgress[key] = (obj.current, obj.required);
                }
            }
        }
    }
}
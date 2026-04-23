using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 서버 권위 퀘스트 추적. PersistentSystems (DontDestroyOnLoad)에 부착된다.
    /// 모든 플레이어가 동일한 퀘스트 상태를 공유한다.
    /// </summary>
    public class QuestManager : NetworkBehaviour
    {
        [Header("Available Quests")]
        [SerializeField] private QuestDataSO[] allQuests;

        public NetworkList<FixedString64Bytes> ActiveQuestIds;
        public NetworkList<FixedString64Bytes> CompletedQuestIds;

        private Dictionary<string, int> killCounters = new();
        private Dictionary<string, int> collectCounters = new();

        private void Awake()
        {
            ActiveQuestIds = new NetworkList<FixedString64Bytes>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);

            CompletedQuestIds = new NetworkList<FixedString64Bytes>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            ServiceLocator.Register(this);

            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<ItemLootedEvent>(OnItemLooted);
            EventBus.Subscribe<ZoneEnteredEvent>(OnZoneEntered);
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<ItemLootedEvent>(OnItemLooted);
            EventBus.Unsubscribe<ZoneEnteredEvent>(OnZoneEntered);

            ServiceLocator.Unregister<QuestManager>();
        }

        [ServerRpc(RequireOwnership = false)]
        public void AcceptQuestServerRpc(FixedString64Bytes questId)
        {
            if (Contains(ActiveQuestIds, questId)) return;
            if (Contains(CompletedQuestIds, questId)) return;

            ActiveQuestIds.Add(questId);
            EventBus.Publish(new QuestAcceptedEvent { questId = questId });
        }

        public bool IsQuestActive(string questId) => Contains(ActiveQuestIds, questId);
        public bool IsQuestCompleted(string questId) => Contains(CompletedQuestIds, questId);

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            if (!IsServer) return;
            ReportProgress(ObjectiveType.Kill, $"enemy_{e.tier}");
        }

        private void OnItemLooted(ItemLootedEvent e)
        {
            if (!IsServer) return;
            ReportProgress(ObjectiveType.Collect, e.itemId.ToString());
        }

        private void OnZoneEntered(ZoneEnteredEvent e)
        {
            if (!IsServer) return;
            ReportProgress(ObjectiveType.Reach, e.zoneId.ToString());
        }

        private void ReportProgress(ObjectiveType type, string targetId)
        {
            if (allQuests == null) return;

            for (int i = ActiveQuestIds.Count - 1; i >= 0; i--)
            {
                var qId = ActiveQuestIds[i];
                var quest = FindQuest(qId.ToString());
                if (quest == null) continue;

                bool allComplete = true;
                foreach (var obj in quest.objectives)
                {
                    if (obj.type == type && obj.targetID == targetId)
                    {
                        string key = $"{qId}_{obj.targetID}";
                        var counter = (type == ObjectiveType.Kill) ? killCounters : collectCounters;
                        counter.TryGetValue(key, out int current);
                        current = Mathf.Min(current + 1, obj.requiredCount);
                        counter[key] = current;

                        EventBus.Publish(new QuestProgressEvent
                        {
                            questId = qId,
                            objectiveType = obj.type,
                            currentCount = current,
                            requiredCount = obj.requiredCount,
                        });
                    }

                    string keyCheck = $"{qId}_{obj.targetID}";
                    var counterCheck = (obj.type == ObjectiveType.Kill) ? killCounters : collectCounters;
                    counterCheck.TryGetValue(keyCheck, out int currentCheck);
                    if (currentCheck < obj.requiredCount) allComplete = false;
                }

                if (allComplete) CompleteQuest(qId);
            }
        }

        private void CompleteQuest(FixedString64Bytes questId)
        {
            if (!IsServer) return;
            for (int i = ActiveQuestIds.Count - 1; i >= 0; i--)
            {
                if (ActiveQuestIds[i].Equals(questId))
                {
                    ActiveQuestIds.RemoveAt(i);
                    break;
                }
            }
            CompletedQuestIds.Add(questId);
            EventBus.Publish(new QuestCompletedEvent { questId = questId });
        }

        private QuestDataSO FindQuest(string id)
        {
            foreach (var q in allQuests)
            {
                if (q != null && q.questID == id) return q;
            }
            return null;
        }

        private static bool Contains(NetworkList<FixedString64Bytes> list, FixedString64Bytes value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(value)) return true;
            }
            return false;
        }

        private static bool Contains(NetworkList<FixedString64Bytes> list, string value)
        {
            FixedString64Bytes fs = value;
            return Contains(list, fs);
        }
    }
}

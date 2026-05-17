using System;
using System.Collections.Generic;
using DeadZone.Actors;
using DeadZone.Core;
using UnityEngine;

namespace DeadZone.Systems.Raid
{
    public enum RaidResultType
    {
        Survived,
        Dead
    }

    [Serializable]
    public class RaidLootResult
    {
        public string itemId;
        public string itemName;
        public int count;

        public RaidLootResult(string itemName, int count)
        {
            itemId = string.Empty;
            this.itemName = itemName;
            this.count = count;
        }

        public RaidLootResult(string itemId, string itemName, int count)
        {
            this.itemId = itemId;
            this.itemName = itemName;
            this.count = count;
        }
    }

    [Serializable]
    public class RaidPlayerResult
    {
        public ulong clientId;
        public RaidResultType resultType;
        public string mapName;
        public int killCount;
        public float survivalTime;
        public List<RaidLootResult> lootItems = new();
    }

    [Serializable]
    internal class RaidPlayerResultJson
    {
        public ulong clientId;
        public int resultType;
        public string mapName;
        public int killCount;
        public float survivalTime;
        public List<RaidLootResult> lootItems = new();
    }

    internal sealed class RaidRuntimePlayerRecord
    {
        public ulong clientId;
        public int killCount;
        public float deathTime = -1f;
        public float extractionTime = -1f;
        public bool isDead;
        public bool isExtracted;
        public readonly Dictionary<string, int> startInventoryCounts = new();
    }

    public static class RaidResultData
    {
        private static readonly Dictionary<ulong, RaidRuntimePlayerRecord> runtimeRecords = new();

        public static RaidResultType ResultType { get; private set; }

        public static string MapName { get; private set; }
        public static int KillCount { get; private set; }
        public static int Dollar { get; private set; }
        public static float SurvivalTime { get; private set; }

        public static readonly List<RaidLootResult> LootItems = new();

        public static bool HasLocalResult { get; private set; }

        public static void BeginRaid(string mapName, IReadOnlyList<ulong> clientIds)
        {
            runtimeRecords.Clear();
            ClearLocalResult();

            MapName = mapName;

            if (clientIds == null)
                return;

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];
                runtimeRecords[clientId] = new RaidRuntimePlayerRecord
                {
                    clientId = clientId
                };
            }
        }

        public static void CaptureStartInventorySnapshot(ulong clientId, GameObject playerObject)
        {
            RaidRuntimePlayerRecord record = GetOrCreateRuntimeRecord(clientId);
            record.startInventoryCounts.Clear();
            AddInventoryCounts(playerObject, record.startInventoryCounts);
        }

        public static void AddKillForPlayer(ulong clientId)
        {
            GetOrCreateRuntimeRecord(clientId).killCount++;
        }

        public static void MarkPlayerDead(ulong clientId, float elapsedSeconds)
        {
            RaidRuntimePlayerRecord record = GetOrCreateRuntimeRecord(clientId);
            if (record.isDead)
                return;

            record.isDead = true;
            record.deathTime = Mathf.Max(0f, elapsedSeconds);
        }

        public static void MarkPlayersExtracted(IReadOnlyList<ulong> clientIds, float elapsedSeconds)
        {
            if (clientIds == null)
                return;

            for (int i = 0; i < clientIds.Count; i++)
            {
                RaidRuntimePlayerRecord record = GetOrCreateRuntimeRecord(clientIds[i]);
                if (record.isDead)
                    continue;

                record.isExtracted = true;
                record.extractionTime = Mathf.Max(0f, elapsedSeconds);
            }
        }

        public static RaidPlayerResult BuildResultForPlayer(ulong clientId, string fallbackMapName, GameObject playerObject)
        {
            RaidRuntimePlayerRecord record = GetOrCreateRuntimeRecord(clientId);
            bool survived = record.isExtracted && !record.isDead;
            float survivalTime = survived ? record.extractionTime : record.deathTime;
            if (survivalTime < 0f)
                survivalTime = 0f;

            RaidPlayerResult result = new()
            {
                clientId = clientId,
                resultType = survived ? RaidResultType.Survived : RaidResultType.Dead,
                mapName = string.IsNullOrWhiteSpace(MapName) ? fallbackMapName : MapName,
                killCount = record.killCount,
                survivalTime = survivalTime
            };

            if (survived)
                result.lootItems = BuildLootDiff(record.startInventoryCounts, playerObject);

            return result;
        }

        public static string ToJson(RaidPlayerResult result)
        {
            if (result == null)
                return string.Empty;

            RaidPlayerResultJson dto = new()
            {
                clientId = result.clientId,
                resultType = (int)result.resultType,
                mapName = result.mapName,
                killCount = result.killCount,
                survivalTime = result.survivalTime,
                lootItems = result.lootItems ?? new List<RaidLootResult>()
            };

            return JsonUtility.ToJson(dto);
        }

        public static bool SetLocalResultFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            RaidPlayerResultJson dto;
            try
            {
                dto = JsonUtility.FromJson<RaidPlayerResultJson>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RaidResultData] Result JSON parse failed. {ex.Message}");
                return false;
            }

            SetLocalResult(new RaidPlayerResult
            {
                clientId = dto.clientId,
                resultType = (RaidResultType)dto.resultType,
                mapName = dto.mapName,
                killCount = dto.killCount,
                survivalTime = dto.survivalTime,
                lootItems = dto.lootItems ?? new List<RaidLootResult>()
            });

            return true;
        }

        public static void SetLocalResult(RaidPlayerResult result)
        {
            if (result == null)
                return;

            ResultType = result.resultType;
            MapName = result.mapName;
            KillCount = result.killCount;
            Dollar = 0;
            SurvivalTime = result.survivalTime;
            HasLocalResult = true;

            LootItems.Clear();
            if (result.resultType == RaidResultType.Survived && result.lootItems != null)
                LootItems.AddRange(result.lootItems);
        }

        public static void SetSurvived(
            string mapName,
            int killCount,
            int dollar,
            float survivalTime,
            List<RaidLootResult> lootItems)
        {
            ResultType = RaidResultType.Survived;
            MapName = mapName;
            KillCount = killCount;
            Dollar = dollar;
            SurvivalTime = survivalTime;
            HasLocalResult = true;

            LootItems.Clear();

            if (lootItems != null)
            {
                LootItems.AddRange(lootItems);
            }
        }

        public static void SetDead(
            string mapName,
            int killCount,
            float survivalTime)
        {
            ResultType = RaidResultType.Dead;
            MapName = mapName;
            KillCount = killCount;
            Dollar = 0;
            SurvivalTime = survivalTime;
            HasLocalResult = true;

            LootItems.Clear();
        }

        public static void ClearLocalResult()
        {
            ResultType = RaidResultType.Dead;
            MapName = string.Empty;
            KillCount = 0;
            Dollar = 0;
            SurvivalTime = 0f;
            HasLocalResult = false;
            LootItems.Clear();
        }

        public static string FormatTime(float seconds)
        {
            int totalSeconds = UnityEngine.Mathf.FloorToInt(seconds);
            int minutes = totalSeconds / 60;
            int remainSeconds = totalSeconds % 60;

            return $"{minutes:00}:{remainSeconds:00}";
        }

        private static RaidRuntimePlayerRecord GetOrCreateRuntimeRecord(ulong clientId)
        {
            if (!runtimeRecords.TryGetValue(clientId, out RaidRuntimePlayerRecord record))
            {
                record = new RaidRuntimePlayerRecord
                {
                    clientId = clientId
                };
                runtimeRecords.Add(clientId, record);
            }

            return record;
        }

        private static List<RaidLootResult> BuildLootDiff(
            IReadOnlyDictionary<string, int> startInventoryCounts,
            GameObject playerObject)
        {
            Dictionary<string, int> endCounts = new();
            AddInventoryCounts(playerObject, endCounts);

            List<RaidLootResult> results = new();
            foreach (KeyValuePair<string, int> pair in endCounts)
            {
                int startCount = 0;
                startInventoryCounts?.TryGetValue(pair.Key, out startCount);
                int gainedCount = pair.Value - startCount;
                if (gainedCount <= 0)
                    continue;

                results.Add(new RaidLootResult(pair.Key, ResolveDisplayName(pair.Key), gainedCount));
            }

            results.Sort((left, right) => string.Compare(left.itemName, right.itemName, StringComparison.Ordinal));
            return results;
        }

        private static void AddInventoryCounts(GameObject playerObject, Dictionary<string, int> counts)
        {
            if (playerObject == null || counts == null)
                return;

            GridInventory inventory = playerObject.GetComponent<GridInventory>();
            if (inventory == null)
                return;

            AddSnapshotCounts(inventory.ExportSnapshot(), counts);
            AddQuickSlotSnapshotCounts(inventory.ExportQuickSlotSnapshot(), counts);
        }

        private static void AddSnapshotCounts(IReadOnlyList<InventoryItemSaveData> snapshot, Dictionary<string, int> counts)
        {
            if (snapshot == null)
                return;

            for (int i = 0; i < snapshot.Count; i++)
            {
                InventoryItemSaveData item = snapshot[i];
                AddCount(counts, item?.itemId, item?.stackCount ?? 0);
            }
        }

        private static void AddQuickSlotSnapshotCounts(IReadOnlyList<QuickSlotSaveData> snapshot, Dictionary<string, int> counts)
        {
            if (snapshot == null)
                return;

            for (int i = 0; i < snapshot.Count; i++)
            {
                QuickSlotSaveData item = snapshot[i];
                AddCount(counts, item?.itemId, item?.stackCount ?? 0);
            }
        }

        private static void AddCount(Dictionary<string, int> counts, string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return;

            if (counts.ContainsKey(itemId))
                counts[itemId] += count;
            else
                counts.Add(itemId, count);
        }

        private static string ResolveDisplayName(string itemId)
        {
            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            ItemDataSO itemData = itemDatabase?.GetById(itemId);

            if (itemData == null)
                return itemId;

            return string.IsNullOrWhiteSpace(itemData.displayName)
                ? itemData.name
                : itemData.displayName;
        }
    }
}

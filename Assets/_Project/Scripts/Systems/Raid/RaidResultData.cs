using System;
using System.Collections.Generic;

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
        public string itemName;
        public int count;

        public RaidLootResult(string itemName, int count)
        {
            this.itemName = itemName;
            this.count = count;
        }
    }

    public static class RaidResultData
    {
        public static RaidResultType ResultType { get; private set; }

        public static string MapName { get; private set; }
        public static int KillCount { get; private set; }
        public static int Dollar { get; private set; }
        public static float SurvivalTime { get; private set; }

        public static readonly List<RaidLootResult> LootItems = new();

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

            LootItems.Clear();
        }

        public static string FormatTime(float seconds)
        {
            int totalSeconds = UnityEngine.Mathf.FloorToInt(seconds);
            int minutes = totalSeconds / 60;
            int remainSeconds = totalSeconds % 60;

            return $"{minutes:00}:{remainSeconds:00}";
        }
    }
}
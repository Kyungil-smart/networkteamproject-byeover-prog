using System.Collections.Generic;
using UnityEngine;
using DeadZone.Systems.Raid;

namespace DeadZone.Systems.Raid
{
    public class RaidSessionTracker : MonoBehaviour
    {
        public static RaidSessionTracker Instance { get; private set; }

        [Header("레이드 정보")]
        [SerializeField] private string mapName = "전선도시";

        [Header("런타임 기록")]
        [SerializeField] private int killCount;
        [SerializeField] private int acquiredDollar;
        [SerializeField] private float survivalTime;

        private readonly Dictionary<string, int> lootedItems = new();

        public string MapName => mapName;
        public int KillCount => killCount;
        public int AcquiredDollar => acquiredDollar;
        public float SurvivalTime => survivalTime;

        private bool isTracking = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            if (!isTracking)
                return;

            survivalTime += Time.deltaTime;
        }

        public void AddKillCount(int amount = 1)
        {
            killCount += amount;
        }

        public void AddDollar(int amount)
        {
            acquiredDollar += amount;
        }

        public void AddLootItem(string itemName, int count = 1)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            if (lootedItems.ContainsKey(itemName))
            {
                lootedItems[itemName] += count;
            }
            else
            {
                lootedItems.Add(itemName, count);
            }
        }

        public List<RaidLootResult> GetLootResults()
        {
            List<RaidLootResult> results = new();

            foreach (var pair in lootedItems)
            {
                results.Add(new RaidLootResult(pair.Key, pair.Value));
            }

            return results;
        }

        public void StopTracking()
        {
            isTracking = false;
        }

        public void ClearAllLootRecord()
        {
            lootedItems.Clear();
            acquiredDollar = 0;
        }
    }
}
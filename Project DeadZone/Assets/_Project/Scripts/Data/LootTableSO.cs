using System;
using UnityEngine;


namespace DeadZone.Core
{
    [Serializable]
    public struct LootEntry
    {
        public ItemDataSO item;
        public int weight;
        public Vector2Int countRange;
    }

    [CreateAssetMenu(menuName = "DeadZone/Loot/Loot Table", fileName = "LootTable_New")]
    public class LootTableSO : ScriptableObject
    {
        public LootEntry[] entries;
        [Tooltip("Locked-zone rarity multiplier")]
        public float lockedZoneMultiplier = 1.5f;

        public ItemDataSO RollOne()
        {
            if (entries == null || entries.Length == 0) return null;
            int totalWeight = 0;
            foreach (var e in entries) totalWeight += Mathf.Max(0, e.weight);
            if (totalWeight <= 0) return null;

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int acc = 0;
            foreach (var e in entries)
            {
                acc += Mathf.Max(0, e.weight);
                if (roll < acc) return e.item;
            }
            return entries[entries.Length - 1].item;
        }
    }
}

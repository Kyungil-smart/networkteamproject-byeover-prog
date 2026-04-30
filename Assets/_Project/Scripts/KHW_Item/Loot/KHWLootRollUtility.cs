using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// LootTableSO에서 아이템과 수량을 함께 뽑기 위한 보조 코드입니다.
    /// 기존 LootTableSO.RollOne()은 아이템만 반환하므로, countRange까지 사용하기 위해 새로 추가했습니다.
    /// </summary>
    public static class KHWLootRollUtility
    {
        public static bool TryRoll(LootTableSO table, out ItemDataSO item, out int amount)
        {
            item = null;
            amount = 1;

            if (table == null || table.entries == null || table.entries.Length == 0)
            {
                return false;
            }

            int totalWeight = 0;
            for (int i = 0; i < table.entries.Length; i++)
            {
                totalWeight += Mathf.Max(0, table.entries[i].weight);
            }

            if (totalWeight <= 0)
            {
                return false;
            }

            int roll = Random.Range(0, totalWeight);
            int acc = 0;

            for (int i = 0; i < table.entries.Length; i++)
            {
                LootEntry entry = table.entries[i];
                acc += Mathf.Max(0, entry.weight);

                if (roll < acc)
                {
                    item = entry.item;
                    amount = RollAmount(entry);
                    return item != null;
                }
            }

            LootEntry last = table.entries[table.entries.Length - 1];
            item = last.item;
            amount = RollAmount(last);
            return item != null;
        }

        private static int RollAmount(LootEntry entry)
        {
            int min = Mathf.Max(1, entry.countRange.x);
            int max = Mathf.Max(min, entry.countRange.y);
            return Random.Range(min, max + 1);
        }
    }
}

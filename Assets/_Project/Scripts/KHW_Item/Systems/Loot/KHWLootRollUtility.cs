using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 역할: 기존 LootTableSO를 수정하지 않고, LootEntry.countRange까지 같이 사용하는 보조 롤링 함수.
    ///
    /// 코드 해석:
    /// - 기존 LootTableSO.RollOne()은 ItemDataSO만 반환한다.
    /// - 이 유틸은 entries 배열을 직접 읽어서 item + amount를 함께 반환한다.
    /// - 기존 LootTableSO.cs는 수정하지 않는다.
    /// </summary>
    public static class KHWLootRollUtility
    {
        public static bool TryRollItemWithAmount(LootTableSO lootTable, out ItemDataSO item, out int amount)
        {
            item = null;
            amount = 1;

            if (lootTable == null || lootTable.entries == null || lootTable.entries.Length == 0)
                return false;

            int totalWeight = 0;
            foreach (LootEntry entry in lootTable.entries)
            {
                if (entry.item == null) continue;
                totalWeight += Mathf.Max(0, entry.weight);
            }

            if (totalWeight <= 0) return false;

            int roll = Random.Range(0, totalWeight);
            int acc = 0;

            foreach (LootEntry entry in lootTable.entries)
            {
                if (entry.item == null) continue;

                acc += Mathf.Max(0, entry.weight);
                if (roll < acc)
                {
                    item = entry.item;

                    int min = Mathf.Max(1, entry.countRange.x);
                    int max = Mathf.Max(min, entry.countRange.y);
                    amount = Random.Range(min, max + 1);
                    return true;
                }
            }

            return false;
        }
    }
}

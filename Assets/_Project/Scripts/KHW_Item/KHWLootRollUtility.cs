// ============================================================================
// KHWLootRollUtility.cs
// 목적: 기존 LootTableSO의 entries, weight, countRange를 이용해서 아이템 1개를 랜덤 추첨합니다.
// 패턴: static Utility + Weighted Random.
// 적용: 오브젝트에 붙이지 않습니다. KHWLootContainer에서 코드로 호출합니다.
// ============================================================================
using UnityEngine;

using DeadZone.Core;

/// <summary>
/// [KHW 추가 스크립트]
/// 기존 LootTableSO를 수정하지 않고, countRange까지 같이 뽑기 위한 유틸리티입니다.
/// </summary>
public static class KHWLootRollUtility
{
    public static bool TryRoll(LootTableSO lootTable, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        if (lootTable == null) return false;
        if (lootTable.entries == null || lootTable.entries.Length == 0) return false;

        int totalWeight = 0;
        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            totalWeight += Mathf.Max(0, lootTable.entries[i].weight);
        }

        if (totalWeight <= 0) return false;

        int roll = Random.Range(0, totalWeight);
        int acc = 0;

        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            LootEntry entry = lootTable.entries[i];
            acc += Mathf.Max(0, entry.weight);

            if (roll < acc)
            {
                item = entry.item;
                amount = RollAmount(entry);
                return item != null;
            }
        }

        LootEntry last = lootTable.entries[lootTable.entries.Length - 1];
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

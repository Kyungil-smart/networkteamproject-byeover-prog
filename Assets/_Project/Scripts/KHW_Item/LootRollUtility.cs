using DeadZone.Core;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// LootTableSO의 entries, weight, countRange를 이용해 컨테이너 슬롯 데이터를 생성한다.
/// weight 총합은 고정값으로 맞추지 않고, 현재 유효한 항목들의 총합을 기준으로 정규화된다.
/// </summary>
public static class LootRollUtility
{
    public static int GetTotalWeight(LootTableSO lootTable)
    {
        if (lootTable == null || lootTable.entries == null)
            return 0;

        int totalWeight = 0;
        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            LootEntry entry = lootTable.entries[i];

            if (entry.item == null)
                continue;

            totalWeight += Mathf.Max(0, entry.weight);
        }

        return totalWeight;
    }

    public static bool IsTotalWeight100(LootTableSO lootTable)
    {
        return GetTotalWeight(lootTable) == 100;
    }

    public static bool TryRollOne(LootTableSO lootTable, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        if (lootTable == null || lootTable.entries == null || lootTable.entries.Length == 0)
            return false;

        return TryRollFromEntries(lootTable.entries, out item, out amount);
    }

    public static List<ContainerSlotNetData> RollSlots(
        LootTableSO lootTable,
        int slotCount,
        int rollCount,
        bool requireTotalWeight100,
        bool isWeaponBox,
        int minWeaponCount,
        int maxWeaponCount)
    {
        List<ContainerSlotNetData> results = new List<ContainerSlotNetData>();

        if (lootTable == null || lootTable.entries == null || lootTable.entries.Length == 0)
            return results;

        int totalWeight = GetTotalWeight(lootTable);
        if (totalWeight <= 0)
        {
            Debug.LogWarning("[LootRollUtility] LootTable에 유효한 item과 weight가 없습니다.");
            return results;
        }

        int safeSlotCount = Mathf.Max(1, slotCount);
        int safeRollCount = Mathf.Clamp(rollCount, 1, safeSlotCount);

        if (isWeaponBox)
        {
            RollWeaponBoxSlots(
                lootTable,
                safeSlotCount,
                safeRollCount,
                minWeaponCount,
                maxWeaponCount,
                results);
        }
        else
        {
            RollNormalSlots(lootTable, safeRollCount, results);
        }

        while (results.Count < safeSlotCount)
        {
            results.Add(new ContainerSlotNetData());
        }

        return results;
    }

    private static void RollNormalSlots(
        LootTableSO lootTable,
        int rollCount,
        List<ContainerSlotNetData> results)
    {
        for (int i = 0; i < rollCount; i++)
        {
            if (TryRollOne(lootTable, out ItemDataSO item, out int amount))
            {
                AddSlot(results, item, amount);
            }
        }
    }

    private static void RollWeaponBoxSlots(
        LootTableSO lootTable,
        int slotCount,
        int rollCount,
        int minWeaponCount,
        int maxWeaponCount,
        List<ContainerSlotNetData> results)
    {
        List<LootEntry> weaponEntries = CollectEntriesByWeaponState(lootTable, true);
        List<LootEntry> nonWeaponEntries = CollectEntriesByWeaponState(lootTable, false);

        int safeMin = Mathf.Clamp(minWeaponCount, 0, rollCount);
        int safeMax = Mathf.Clamp(maxWeaponCount, safeMin, rollCount);
        int weaponCount = Random.Range(safeMin, safeMax + 1);

        if (weaponEntries.Count == 0)
        {
            Debug.LogWarning("[LootRollUtility] 총기 상자 모드지만 WeaponDataSO 항목이 LootTable에 없습니다.");
            weaponCount = 0;
        }

        for (int i = 0; i < weaponCount && results.Count < slotCount; i++)
        {
            if (TryRollFromEntryList(weaponEntries, out ItemDataSO item, out int amount))
            {
                AddSlot(results, item, amount);
            }
        }

        int remainCount = rollCount - results.Count;

        for (int i = 0; i < remainCount && results.Count < slotCount; i++)
        {
            if (nonWeaponEntries.Count > 0)
            {
                if (TryRollFromEntryList(nonWeaponEntries, out ItemDataSO item, out int amount))
                {
                    AddSlot(results, item, amount);
                }

                continue;
            }

            if (TryRollOne(lootTable, out ItemDataSO fallbackItem, out int fallbackAmount))
            {
                AddSlot(results, fallbackItem, fallbackAmount);
            }
        }
    }

    private static List<LootEntry> CollectEntriesByWeaponState(
        LootTableSO lootTable,
        bool wantWeapon)
    {
        List<LootEntry> list = new List<LootEntry>();

        if (lootTable == null || lootTable.entries == null)
            return list;

        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            LootEntry entry = lootTable.entries[i];

            if (entry.item == null || entry.weight <= 0)
                continue;

            bool isWeapon = entry.item is WeaponDataSO;
            if (isWeapon == wantWeapon)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    private static bool TryRollFromEntryList(
        List<LootEntry> entries,
        out ItemDataSO item,
        out int amount)
    {
        item = null;
        amount = 0;

        if (entries == null || entries.Count == 0)
            return false;

        return TryRollFromEntries(entries.ToArray(), out item, out amount);
    }

    private static bool TryRollFromEntries(
        LootEntry[] entries,
        out ItemDataSO item,
        out int amount)
    {
        item = null;
        amount = 0;

        if (entries == null || entries.Length == 0)
            return false;

        int totalWeight = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            LootEntry entry = entries[i];

            if (entry.item == null)
                continue;

            totalWeight += Mathf.Max(0, entry.weight);
        }

        if (totalWeight <= 0)
            return false;

        int roll = Random.Range(0, totalWeight);
        int acc = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            LootEntry entry = entries[i];

            if (entry.item == null)
                continue;

            acc += Mathf.Max(0, entry.weight);

            if (roll < acc)
            {
                item = entry.item;
                amount = RollAmount(entry);
                return true;
            }
        }

        return false;
    }

    private static int RollAmount(LootEntry entry)
    {
        int min = Mathf.Max(1, entry.countRange.x);
        int max = Mathf.Max(min, entry.countRange.y);

        return Random.Range(min, max + 1);
    }

    private static void AddSlot(
        List<ContainerSlotNetData> results,
        ItemDataSO item,
        int amount)
    {
        if (item == null)
            return;

        if (string.IsNullOrEmpty(item.itemID))
        {
            Debug.LogWarning("[LootRollUtility] itemID가 비어 있는 아이템은 슬롯에 넣을 수 없습니다: " + item.name);
            return;
        }

        ContainerSlotNetData data = new ContainerSlotNetData
        {
            itemId = new FixedString64Bytes(item.itemID),
            amount = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue)
        };

        results.Add(data);
    }
}
using System.Collections.Generic;
using DeadZone.Core;
using Unity.Collections;
using UnityEngine;

public static class LootRollUtility
{
    public static int GetTotalWeight(LootTableSO lootTable)
    {
        return lootTable != null ? GetTotalWeight(lootTable.entries) : 0;
    }

    public static int GetTotalWeight(IReadOnlyList<LootEntry> entries)
    {
        if (entries == null)
            return 0;

        int totalWeight = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            LootEntry entry = entries[i];
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
        if (lootTable == null)
        {
            item = null;
            amount = 0;
            return false;
        }

        return TryRollOne(lootTable.entries, out item, out amount);
    }

    public static bool TryRollOne(IReadOnlyList<LootEntry> entries, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        if (entries == null || entries.Count == 0)
            return false;

        int totalWeight = GetTotalWeight(entries);
        if (totalWeight <= 0)
            return false;

        int roll = Random.Range(0, totalWeight);
        int acc = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            LootEntry entry = entries[i];
            if (entry.item == null)
                continue;

            acc += Mathf.Max(0, entry.weight);
            if (roll >= acc)
                continue;

            item = entry.item;
            amount = RollAmount(entry);
            return true;
        }

        return false;
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
        return RollSlots(
            lootTable != null ? lootTable.entries : null,
            slotCount,
            rollCount,
            requireTotalWeight100,
            isWeaponBox,
            minWeaponCount,
            maxWeaponCount);
    }

    public static List<ContainerSlotNetData> RollSlots(
        IReadOnlyList<LootEntry> entries,
        int slotCount,
        int rollCount,
        bool requireTotalWeight100,
        bool isWeaponBox,
        int minWeaponCount,
        int maxWeaponCount)
    {
        List<ContainerSlotNetData> results = new List<ContainerSlotNetData>();

        if (entries == null || entries.Count == 0)
            return results;

        int totalWeight = GetTotalWeight(entries);
        if (totalWeight <= 0)
        {
            Debug.LogWarning("[LootRollUtility] Loot entries have no valid weighted items.");
            return results;
        }

        int safeSlotCount = Mathf.Max(1, slotCount);
        int safeRollCount = Mathf.Clamp(rollCount, 0, safeSlotCount);

        if (isWeaponBox)
        {
            RollWeaponBoxSlots(entries, safeSlotCount, safeRollCount, minWeaponCount, maxWeaponCount, results);
        }
        else
        {
            RollNormalSlots(entries, safeRollCount, results);
        }

        while (results.Count < safeSlotCount)
            results.Add(new ContainerSlotNetData());

        return results;
    }

    private static void RollNormalSlots(
        IReadOnlyList<LootEntry> entries,
        int rollCount,
        List<ContainerSlotNetData> results)
    {
        for (int i = 0; i < rollCount; i++)
        {
            if (TryRollOne(entries, out ItemDataSO item, out int amount))
                AddSlot(results, item, amount);
        }
    }

    private static void RollWeaponBoxSlots(
        IReadOnlyList<LootEntry> entries,
        int slotCount,
        int rollCount,
        int minWeaponCount,
        int maxWeaponCount,
        List<ContainerSlotNetData> results)
    {
        List<LootEntry> weaponEntries = CollectEntriesByWeaponState(entries, true);
        List<LootEntry> nonWeaponEntries = CollectEntriesByWeaponState(entries, false);

        int safeMin = Mathf.Clamp(minWeaponCount, 0, rollCount);
        int safeMax = Mathf.Clamp(maxWeaponCount, safeMin, rollCount);
        int weaponCount = Random.Range(safeMin, safeMax + 1);

        if (weaponEntries.Count == 0)
        {
            Debug.LogWarning("[LootRollUtility] Weapon box mode has no WeaponDataSO entries.");
            weaponCount = 0;
        }

        for (int i = 0; i < weaponCount && results.Count < slotCount; i++)
        {
            if (TryRollFromEntryList(weaponEntries, out ItemDataSO item, out int amount))
                AddSlot(results, item, amount);
        }

        int remainCount = rollCount - results.Count;
        for (int i = 0; i < remainCount && results.Count < slotCount; i++)
        {
            if (nonWeaponEntries.Count > 0)
            {
                if (TryRollFromEntryList(nonWeaponEntries, out ItemDataSO item, out int amount))
                    AddSlot(results, item, amount);

                continue;
            }

            if (TryRollOne(entries, out ItemDataSO fallbackItem, out int fallbackAmount))
                AddSlot(results, fallbackItem, fallbackAmount);
        }
    }

    private static List<LootEntry> CollectEntriesByWeaponState(
        IReadOnlyList<LootEntry> entries,
        bool wantWeapon)
    {
        List<LootEntry> list = new List<LootEntry>();
        if (entries == null)
            return list;

        for (int i = 0; i < entries.Count; i++)
        {
            LootEntry entry = entries[i];
            if (entry.item == null || entry.weight <= 0)
                continue;

            bool isWeapon = entry.item is WeaponDataSO;
            if (isWeapon == wantWeapon)
                list.Add(entry);
        }

        return list;
    }

    private static bool TryRollFromEntryList(
        List<LootEntry> entries,
        out ItemDataSO item,
        out int amount)
    {
        return TryRollOne(entries, out item, out amount);
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
            Debug.LogWarning("[LootRollUtility] Cannot add loot item with empty itemID: " + item.name);
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

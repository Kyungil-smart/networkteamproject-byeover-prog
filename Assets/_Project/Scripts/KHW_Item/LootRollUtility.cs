// ============================================================================
// 목적: 기존 LootTableSO의 entries, weight, countRange를 이용해서 아이템 1개를 랜덤 추첨.
// 패턴: static Utility + Weighted Random.
// 적용: LootContainer에서 코드로 호출합니다.
// ============================================================================
using DeadZone.Core;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// [루팅 확률 계산 유틸리티]
/// 패턴: static Utility + Weighted Random.
/// 역할: 기존 LootTableSO를 수정하지 않고 weight 합계 100 검증, 확률 추첨, 수량 추첨을 수행한다.
/// 설명: 오브젝트에 붙이지 않고 LootContainer에서 코드로만 호출한다.
/// </summary>
public static class LootRollUtility
{
    /// <summary>
    /// LootTableSO의 weight 총합을 계산한다.
    /// </summary>
    public static int GetTotalWeight(LootTableSO lootTable)
    {
        if (lootTable == null || lootTable.entries == null)
        {
            return 0;
        }

        int totalWeight = 0;
        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            totalWeight += Mathf.Max(0, lootTable.entries[i].weight);
        }

        return totalWeight;
    }

    /// <summary>
    /// weight 총합이 100인지 검사한다.
    /// </summary>
    public static bool IsTotalWeight100(LootTableSO lootTable)
    {
        return GetTotalWeight(lootTable) == 100;
    }

    /// <summary>
    /// 전체 LootTable에서 아이템 1개를 weight 기반으로 뽑는다.
    /// </summary>
    public static bool TryRollOne(LootTableSO lootTable, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        if (lootTable == null || lootTable.entries == null || lootTable.entries.Length == 0)
        {
            return false;
        }

        return TryRollFromEntries(lootTable.entries, out item, out amount);
    }

    /// <summary>
    /// 총기 상자 규칙을 포함해서 슬롯 목록을 생성한다.
    /// </summary>
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
        {
            return results;
        }

        if (requireTotalWeight100 && !IsTotalWeight100(lootTable))
        {
            Debug.LogWarning("[LootRollUtility] LootTable weight 총합이 100이 아닙니다. 현재 총합: " + GetTotalWeight(lootTable));
            return results;
        }

        int safeSlotCount = Mathf.Max(1, slotCount);
        int safeRollCount = Mathf.Clamp(rollCount, 1, safeSlotCount);

        if (isWeaponBox)
        {
            RollWeaponBoxSlots(lootTable, safeSlotCount, safeRollCount, minWeaponCount, maxWeaponCount, results);
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

    /// <summary>
    /// 일반 상자: LootTable 전체 후보에서 rollCount만큼 추첨한다.
    /// </summary>
    private static void RollNormalSlots(LootTableSO lootTable, int rollCount, List<ContainerSlotNetData> results)
    {
        for (int i = 0; i < rollCount; i++)
        {
            ItemDataSO item;
            int amount;

            if (!TryRollOne(lootTable, out item, out amount))
            {
                continue;
            }

            AddSlot(results, item, amount);
        }
    }

    /// <summary>
    /// 총기 상자: 무기는 최소/최대 개수를 따로 보장하고, 나머지는 비무기 후보에서 뽑는다.
    /// </summary>
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
            ItemDataSO item;
            int amount;

            if (TryRollFromEntryList(weaponEntries, out item, out amount))
            {
                AddSlot(results, item, amount);
            }
        }

        int remainCount = rollCount - results.Count;

        for (int i = 0; i < remainCount && results.Count < slotCount; i++)
        {
            ItemDataSO item;
            int amount;

            if (nonWeaponEntries.Count > 0)
            {
                if (TryRollFromEntryList(nonWeaponEntries, out item, out amount))
                {
                    AddSlot(results, item, amount);
                }
            }
            else
            {
                if (TryRollOne(lootTable, out item, out amount))
                {
                    AddSlot(results, item, amount);
                }
            }
        }
    }

    /// <summary>
    /// WeaponDataSO 여부에 따라 후보를 분리한다.
    /// </summary>
    private static List<LootEntry> CollectEntriesByWeaponState(LootTableSO lootTable, bool wantWeapon)
    {
        List<LootEntry> list = new List<LootEntry>();

        if (lootTable == null || lootTable.entries == null)
        {
            return list;
        }

        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            LootEntry entry = lootTable.entries[i];

            bool isWeapon = entry.item is WeaponDataSO;
            if (isWeapon == wantWeapon)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    /// <summary>
    /// List 후보에서 weight 기반으로 1개를 뽑는다.
    /// </summary>
    private static bool TryRollFromEntryList(List<LootEntry> entries, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        if (entries == null || entries.Count == 0)
        {
            return false;
        }

        return TryRollFromEntries(entries.ToArray(), out item, out amount);
    }

    /// <summary>
    /// 배열 후보에서 weight 기반으로 1개를 뽑는다.
    /// </summary>
    private static bool TryRollFromEntries(LootEntry[] entries, out ItemDataSO item, out int amount)
    {
        item = null;
        amount = 0;

        int totalWeight = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].item == null)
            {
                continue;
            }

            totalWeight += Mathf.Max(0, entries[i].weight);
        }

        if (totalWeight <= 0)
        {
            return false;
        }

        int roll = Random.Range(0, totalWeight);
        int acc = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            LootEntry entry = entries[i];

            if (entry.item == null)
            {
                continue;
            }

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

    /// <summary>
    /// LootEntry.countRange로 수량을 뽑는다.
    /// </summary>
    private static int RollAmount(LootEntry entry)
    {
        int min = Mathf.Max(1, entry.countRange.x);
        int max = Mathf.Max(min, entry.countRange.y);
        return Random.Range(min, max + 1);
    }

    /// <summary>
    /// ItemDataSO를 ContainerSlotNetData로 변환한다.
    /// </summary>
    private static void AddSlot(List<ContainerSlotNetData> results, ItemDataSO item, int amount)
    {
        if (item == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.itemID))
        {
            Debug.LogWarning("[LootRollUtility] itemID가 비어 있는 아이템은 슬롯에 넣을 수 없습니다: " + item.name);
            return;
        }

        ContainerSlotNetData data = new ContainerSlotNetData();
        data.itemId = new FixedString64Bytes(item.itemID);
        data.amount = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue);

        results.Add(data);
    }
}

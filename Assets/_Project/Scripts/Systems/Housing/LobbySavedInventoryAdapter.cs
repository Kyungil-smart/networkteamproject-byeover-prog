using System.Collections.Generic;

using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 로비 저장 데이터의 플레이어 인벤토리와 보관함을 시설 제작/업그레이드용 IInventory로 노출합니다.
    /// </summary>
    public sealed class LobbySavedInventoryAdapter : IInventory
    {
        private const string InventoryContainerId = "Inventory";
        private const string StashContainerId = "stash";
        private const int PlayerInventoryGridWidth = 4;
        private const int StashSlotCapacity = 110;

        private readonly LobbyInventoryState inventoryState;

        public LobbySavedInventoryAdapter(LobbyInventoryState inventoryState)
        {
            this.inventoryState = inventoryState;
        }

        public bool IsValid => inventoryState != null;

        public bool HasAnyItems =>
            HasAny(inventoryState?.InventoryItems) ||
            HasAny(inventoryState?.StashItems);

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (inventoryState == null || item == null || string.IsNullOrWhiteSpace(item.itemID) || amount <= 0)
                return false;

            List<ItemSaveDTO> inventoryItems = CloneItems(inventoryState.InventoryItems, InventoryContainerId);
            List<ItemSaveDTO> stashItems = CloneItems(inventoryState.StashItems, StashContainerId);

            int remaining = AddToExistingStacks(inventoryItems, item, amount);
            remaining = AddToExistingStacks(stashItems, item, remaining);

            while (remaining > 0)
            {
                int slotIndex = FindFirstFreeInventorySlotIndex(inventoryItems);
                if (slotIndex < 0)
                    break;

                int stackCount = Mathf.Min(Mathf.Max(1, item.maxStackSize), remaining);
                inventoryItems.Add(CreateSaveItem(item, InventoryContainerId, slotIndex, PlayerInventoryGridWidth, stackCount));
                remaining -= stackCount;
            }

            while (remaining > 0)
            {
                int slotIndex = FindFirstFreeLinearSlotIndex(stashItems, StashSlotCapacity);
                if (slotIndex < 0)
                    return false;

                int stackCount = Mathf.Min(Mathf.Max(1, item.maxStackSize), remaining);
                stashItems.Add(CreateSaveItem(item, StashContainerId, slotIndex, StashSlotCapacity, stackCount));
                remaining -= stackCount;
            }

            inventoryState.SetInventoryItems(inventoryItems);
            inventoryState.SetStashItems(stashItems);
            return true;
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            return GetItemCount(itemId) >= count;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (inventoryState == null || string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            List<ItemSaveDTO> inventoryItems = CloneItems(inventoryState.InventoryItems, InventoryContainerId);
            List<ItemSaveDTO> stashItems = CloneItems(inventoryState.StashItems, StashContainerId);

            if (GetItemCount(inventoryItems, itemId) + GetItemCount(stashItems, itemId) < count)
                return false;

            int remaining = RemoveItemCount(inventoryItems, itemId, count);
            remaining = RemoveItemCount(stashItems, itemId, remaining);

            if (remaining > 0)
                return false;

            inventoryState.SetInventoryItems(inventoryItems);
            inventoryState.SetStashItems(stashItems);
            return true;
        }

        public int GetItemCount(string itemId)
        {
            if (inventoryState == null || string.IsNullOrWhiteSpace(itemId))
                return 0;

            return GetItemCount(inventoryState.InventoryItems, itemId) +
                   GetItemCount(inventoryState.StashItems, itemId);
        }

        private static int AddToExistingStacks(List<ItemSaveDTO> items, ItemDataSO item, int amount)
        {
            int remaining = Mathf.Max(0, amount);
            int maxStack = Mathf.Max(1, item.maxStackSize);

            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                ItemSaveDTO savedItem = items[i];
                if (savedItem == null || !IsSameItem(savedItem, item.itemID))
                    continue;

                int available = maxStack - Mathf.Max(0, savedItem.stackCount);
                if (available <= 0)
                    continue;

                int addCount = Mathf.Min(available, remaining);
                savedItem.stackCount += addCount;
                remaining -= addCount;
            }

            return remaining;
        }

        private static int RemoveItemCount(List<ItemSaveDTO> items, string itemId, int amount)
        {
            int remaining = Mathf.Max(0, amount);

            for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemSaveDTO item = items[i];
                if (item == null || !IsSameItem(item, itemId))
                    continue;

                int removeCount = Mathf.Min(Mathf.Max(0, item.stackCount), remaining);
                item.stackCount -= removeCount;
                remaining -= removeCount;

                if (item.stackCount <= 0)
                    items.RemoveAt(i);
            }

            return remaining;
        }

        private static int GetItemCount(IReadOnlyList<ItemSaveDTO> items, string itemId)
        {
            if (items == null || string.IsNullOrWhiteSpace(itemId))
                return 0;

            int count = 0;

            for (int i = 0; i < items.Count; i++)
            {
                ItemSaveDTO item = items[i];
                if (item != null && IsSameItem(item, itemId))
                    count += Mathf.Max(0, item.stackCount);
            }

            return count;
        }

        private static List<ItemSaveDTO> CloneItems(IReadOnlyList<ItemSaveDTO> source, string defaultContainerId)
        {
            List<ItemSaveDTO> result = new();

            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                ItemSaveDTO item = source[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.stackCount <= 0)
                    continue;

                result.Add(new ItemSaveDTO
                {
                    itemId = item.itemId,
                    instanceId = item.instanceId,
                    containerId = string.IsNullOrWhiteSpace(item.containerId) ? defaultContainerId : item.containerId,
                    x = Mathf.Max(0, item.x),
                    y = Mathf.Max(0, item.y),
                    rotated = item.rotated,
                    stackCount = Mathf.Max(1, item.stackCount),
                    currentDurability = Mathf.Max(0f, item.currentDurability),
                    currentAmmo = Mathf.Max(0, item.currentAmmo)
                });
            }

            return result;
        }

        private static ItemSaveDTO CreateSaveItem(
            ItemDataSO item,
            string containerId,
            int slotIndex,
            int gridWidth,
            int stackCount)
        {
            int safeGridWidth = Mathf.Max(1, gridWidth);

            return new ItemSaveDTO
            {
                itemId = item.itemID,
                instanceId = $"{containerId}_{slotIndex}_{item.itemID}",
                containerId = containerId,
                x = containerId == StashContainerId ? slotIndex : slotIndex % safeGridWidth,
                y = containerId == StashContainerId ? 0 : slotIndex / safeGridWidth,
                rotated = false,
                stackCount = Mathf.Max(1, stackCount),
                currentDurability = GetDefaultDurability(item),
                currentAmmo = item is WeaponDataSO weapon ? Mathf.Max(0, weapon.magSize) : 0
            };
        }

        private static int FindFirstFreeInventorySlotIndex(List<ItemSaveDTO> items)
        {
            int maxIndex = Mathf.Max(PlayerInventoryGridWidth * 5, GetMaxLinearSlotIndex(items, PlayerInventoryGridWidth) + 1);
            return FindFirstFreeLinearSlotIndex(items, maxIndex, PlayerInventoryGridWidth);
        }

        private static int FindFirstFreeLinearSlotIndex(
            List<ItemSaveDTO> items,
            int slotCapacity,
            int gridWidth = StashSlotCapacity)
        {
            int safeCapacity = Mathf.Max(1, slotCapacity);
            int safeGridWidth = Mathf.Max(1, gridWidth);

            for (int slotIndex = 0; slotIndex < safeCapacity; slotIndex++)
            {
                bool occupied = false;

                for (int i = 0; i < items.Count; i++)
                {
                    ItemSaveDTO item = items[i];
                    if (item == null)
                        continue;

                    int itemSlotIndex = ToLinearSlotIndex(item, safeGridWidth);
                    if (itemSlotIndex != slotIndex)
                        continue;

                    occupied = true;
                    break;
                }

                if (!occupied)
                    return slotIndex;
            }

            return -1;
        }

        private static int GetMaxLinearSlotIndex(List<ItemSaveDTO> items, int gridWidth)
        {
            int maxIndex = -1;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                    maxIndex = Mathf.Max(maxIndex, ToLinearSlotIndex(items[i], gridWidth));
            }

            return maxIndex;
        }

        private static int ToLinearSlotIndex(ItemSaveDTO item, int gridWidth)
        {
            if (item == null)
                return -1;

            if (item.y <= 0 && item.x >= gridWidth)
                return Mathf.Max(0, item.x);

            return Mathf.Max(0, item.y) * Mathf.Max(1, gridWidth) + Mathf.Max(0, item.x);
        }

        private static bool HasAny(IReadOnlyList<ItemSaveDTO> items)
        {
            if (items == null)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                ItemSaveDTO item = items[i];
                if (item != null && !string.IsNullOrWhiteSpace(item.itemId) && item.stackCount > 0)
                    return true;
            }

            return false;
        }

        private static bool IsSameItem(ItemSaveDTO item, string itemId)
        {
            return item != null &&
                   !string.IsNullOrWhiteSpace(item.itemId) &&
                   string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase);
        }

        private static float GetDefaultDurability(ItemDataSO item)
        {
            return item switch
            {
                WeaponDataSO weapon => Mathf.Max(0f, weapon.maxDurability),
                ArmorDataSO armor => Mathf.Max(0f, armor.maxDurability),
                HelmetDataSO helmet => Mathf.Max(0f, helmet.maxDurability),
                _ => 0f
            };
        }
    }
}

using System;
using System.Collections.Generic;
using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Systems;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class LobbyInventoryStateUiBridge : MonoBehaviour
    {
        private const string InventoryContainerId = "inventory";
        private const string StashContainerId = "stash";
        private const string QuickSlotContainerId = "quickslot";
        private const int PlayerInventoryGridWidth = 4;
        private const int StashGridWidth = 10;
        private const int QuickSlotGridWidth = 6;

        [Header("????곹깭")] [SerializeField] private LobbyInventoryState inventoryState;

        [Header("?꾩씠???곗씠?곕쿋?댁뒪")] [SerializeField]
        private ItemDatabase itemDatabase;

        [Header("?ы솕")] [SerializeField] private WalletSystem walletSystem;

        [Header("UI 猷⑦듃")] [SerializeField] private Transform inventorySlotsRoot;
        [SerializeField] private Transform stashSlotsRoot;
        [SerializeField] private Transform quickSlotSlotsRoot;
        [SerializeField] private Transform equipmentSlotsRoot;
        [SerializeField] private Transform quickSlotsRoot;
        [SerializeField] private ItemTooltipUI tooltipUI;

        [Header("Sync")] [SerializeField] private bool captureOnStart = true;

        public bool CanAuthorInventorySection
        {
            get
            {
                ResolveMissingReferences();
                return inventorySlotsRoot != null;
            }
        }

        public bool CanAuthorStashSection
        {
            get
            {
                ResolveMissingReferences();
                return stashSlotsRoot != null;
            }
        }

        public bool CanAuthorEquipmentSection
        {
            get
            {
                ResolveMissingReferences();
                return equipmentSlotsRoot != null;
            }
        }

        public bool CanAuthorQuickSlotSection
        {
            get
            {
                ResolveMissingReferences();
                return quickSlotsRoot != null;
            }
        }

        private void Start()
        {
            ResolveMissingReferences();

            if (!captureOnStart)
                return;

            if (HasExistingInventoryState())
            {
                Debug.Log(
                    "[LobbyInventoryStateUiBridge] Existing lobby inventory state found. Skipping Start capture to avoid overwriting saved inventory with empty UI.",
                    this);
                return;
            }

            if (captureOnStart)
            {
                Debug.Log(
                    "[LobbyInventoryStateUiBridge] captureOnStart is ignored. UI state is restored from CloudSaveSystem first and captured only when an explicit save is requested.",
                    this);
            }
        }

        private bool HasExistingInventoryState()
        {
            return inventoryState != null &&
                   ((inventoryState.InventoryItems != null && inventoryState.InventoryItems.Count > 0) ||
                    (inventoryState.StashItems != null && inventoryState.StashItems.Count > 0) ||
                    (inventoryState.QuickSlotItems != null && inventoryState.QuickSlotItems.Count > 0) ||
                    (inventoryState.EquipmentItems != null && inventoryState.EquipmentItems.Count > 0));
        }

        [Button("UI ?곹깭瑜?????곹깭濡?諛섏쁺")]
        public void CaptureUiToState()
        {
            ResolveMissingReferences();

            if (inventoryState == null)
            {
                Debug.LogWarning("[LobbyInventoryStateUiBridge] LobbyInventoryState媛 ?곌껐?섏? ?딆븯?듬땲??", this);
                return;
            }

            if (walletSystem != null)
                inventoryState.SetCredits(walletSystem.CurrentCredits);

            if (inventorySlotsRoot != null)
            {
                List<ItemSaveDTO> capturedInventoryItems = CollectItemSlots(inventorySlotsRoot, InventoryContainerId);
                if (capturedInventoryItems.Count > 0 || inventoryState.InventoryItems == null ||
                    inventoryState.InventoryItems.Count == 0)
                    inventoryState.SetInventoryItems(capturedInventoryItems);
                else
                    Debug.Log(
                        "[LobbyInventoryStateUiBridge] Inventory UI scan returned 0 items. Keeping existing inventory state to avoid wiping saved inventory.",
                        this);
            }
            else
                Debug.LogWarning(
                    "[LobbyInventoryStateUiBridge] Inventory slots root missing. Keeping existing inventory state.",
                    this);

            if (stashSlotsRoot != null)
            {
                List<ItemSaveDTO> capturedStashItems = CollectStashSlots(stashSlotsRoot);
                if (capturedStashItems.Count > 0 || inventoryState.StashItems == null ||
                    inventoryState.StashItems.Count == 0)
                    inventoryState.SetStashItems(capturedStashItems);
                else
                    Debug.Log(
                        "[LobbyInventoryStateUiBridge] Stash UI scan returned 0 items. Keeping existing stash state to avoid wiping saved stash.",
                        this);
            }
            else
                Debug.LogWarning(
                    "[LobbyInventoryStateUiBridge] Stash slots root missing. Keeping existing stash state.", this);

            List<ItemSaveDTO> capturedQuickSlotItems = CollectQuickSlotSlots(quickSlotSlotsRoot);
            PruneQuickSlotItemsAgainstInventory(capturedQuickSlotItems, inventoryState.InventoryItems);
            if (capturedQuickSlotItems.Count > 0 || inventoryState.QuickSlotItems == null ||
                inventoryState.QuickSlotItems.Count == 0)
                inventoryState.SetQuickSlotItems(capturedQuickSlotItems);
            else
                Debug.Log(
                    "[LobbyInventoryStateUiBridge] QuickSlot UI scan returned 0 items. Keeping existing quick slot state to avoid wiping lobby quick slots.",
                    this);

            List<EquipmentSaveDTO> capturedEquipmentItems = CollectEquipmentSlots(equipmentSlotsRoot);
            if (capturedEquipmentItems.Count > 0 || inventoryState.EquipmentItems == null ||
                inventoryState.EquipmentItems.Count == 0)
                inventoryState.SetEquipmentItems(capturedEquipmentItems);
            else
                Debug.Log(
                    "[LobbyInventoryStateUiBridge] Equipment UI scan returned 0 items. Keeping existing equipment state to avoid wiping equipped lobby items.",
                    this);

            if (quickSlotsRoot != null)
            {
                List<ItemSaveDTO> capturedQuickSlotsRootItems = CollectItemSlots(quickSlotsRoot, QuickSlotContainerId);
                PruneQuickSlotItemsAgainstInventory(capturedQuickSlotsRootItems, inventoryState.InventoryItems);
                if (capturedQuickSlotsRootItems.Count > 0 || inventoryState.QuickSlotItems == null || inventoryState.QuickSlotItems.Count == 0)
                    inventoryState.SetQuickSlotItems(capturedQuickSlotsRootItems);
                else
                    Debug.Log("[LobbyInventoryStateUiBridge] QuickSlot UI scan returned 0 items. Keeping existing quickslot state to avoid wiping saved quickslots.", this);
            }
        }

        public void CaptureChangedEquipmentSlots(params InventorySlotUI[] changedSlots)
        {
            ResolveMissingReferences();

            if (inventoryState == null || changedSlots == null || changedSlots.Length == 0)
                return;

            List<EquipmentSaveDTO> equipmentItems = new();
            if (inventoryState.EquipmentItems != null)
            {
                for (int i = 0; i < inventoryState.EquipmentItems.Count; i++)
                {
                    EquipmentSaveDTO item = inventoryState.EquipmentItems[i];
                    if (item != null && !string.IsNullOrWhiteSpace(item.slotId))
                        equipmentItems.Add(CloneEquipment(item));
                }
            }

            bool changed = false;
            for (int i = 0; i < changedSlots.Length; i++)
            {
                InventorySlotUI slot = changedSlots[i];
                if (slot == null)
                    continue;

                slot.PrepareForSaveSnapshot();

                string slotId = slot.GetEquipmentSaveSlotId();
                if (string.IsNullOrWhiteSpace(slotId))
                    continue;

                RemoveEquipment(equipmentItems, slotId);
                changed = true;

                if (!slot.HasItem || slot.CurrentItemData == null)
                    continue;

                equipmentItems.Add(new EquipmentSaveDTO
                {
                    slotId = slotId,
                    itemId = slot.CurrentItemData.itemID,
                    instanceId = $"{slotId}_{slot.CurrentItemData.itemID}",
                    loadedAmmoId = string.Empty,
                    currentAmmo = GetDefaultAmmo(slot.CurrentItemData),
                    durability = GetDefaultDurability(slot.CurrentItemData)
                });
            }

            if (changed)
                inventoryState.SetEquipmentItems(equipmentItems);
        }

        public void CaptureChangedItemSlots(params InventorySlotUI[] changedSlots)
        {
            ResolveMissingReferences();

            if (inventoryState == null || changedSlots == null || changedSlots.Length == 0)
                return;

            List<ItemSaveDTO> inventoryItems = CloneItems(inventoryState.InventoryItems);
            List<ItemSaveDTO> stashItems = CloneItems(inventoryState.StashItems);
            List<ItemSaveDTO> quickSlotItems = CloneItems(inventoryState.QuickSlotItems);
            bool inventoryChanged = false;
            bool stashChanged = false;
            bool quickSlotChanged = false;

            for (int i = 0; i < changedSlots.Length; i++)
            {
                InventorySlotUI slot = changedSlots[i];
                if (slot == null)
                    continue;

                slot.PrepareForSaveSnapshot();

                if (slot.SlotKind != InventorySlotKind.Bag && slot.SlotKind != InventorySlotKind.QuickSlot)
                    continue;

                string containerId = ResolveChangedItemContainerId(slot);
                List<ItemSaveDTO> targetItems =
                    ResolveItemList(containerId, inventoryItems, stashItems, quickSlotItems);

                int gridWidth = GetGridWidth(containerId);
                int slotIndex = Mathf.Max(0, slot.SlotIndex);

                RemoveItemAtSlot(targetItems, slotIndex, gridWidth);

                if (slot.HasItem && slot.CurrentItemData != null)
                {
                    targetItems.Add(new ItemSaveDTO
                    {
                        itemId = slot.CurrentItemData.itemID,
                        instanceId = $"{containerId}_{slotIndex}_{slot.CurrentItemData.itemID}",
                        containerId = containerId,
                        x = ToGridX(slotIndex, gridWidth),
                        y = ToGridY(slotIndex, gridWidth),
                        rotated = false,
                        stackCount = Mathf.Max(1, slot.CurrentStackCount),
                        currentDurability = GetDefaultDurability(slot.CurrentItemData),
                        currentAmmo = GetDefaultAmmo(slot.CurrentItemData)
                    });
                }

                if (string.Equals(containerId, StashContainerId, StringComparison.OrdinalIgnoreCase))
                    stashChanged = true;
                else if (string.Equals(containerId, QuickSlotContainerId, StringComparison.OrdinalIgnoreCase))
                    quickSlotChanged = true;
                else
                    inventoryChanged = true;
            }

            if (PruneQuickSlotItemsAgainstInventory(quickSlotItems, inventoryItems))
                quickSlotChanged = true;

            if (inventoryChanged)
                inventoryState.SetInventoryItems(inventoryItems);

            if (stashChanged)
                inventoryState.SetStashItems(stashItems);

            if (quickSlotChanged)
                inventoryState.SetQuickSlotItems(quickSlotItems);

            if (inventoryChanged || stashChanged || quickSlotChanged)
            {
                Debug.Log(
                    $"[LobbyInventoryStateUiBridge] Changed item slots captured. InventoryItems={inventoryItems.Count}, StashItems={stashItems.Count}, QuickSlotItems={quickSlotItems.Count}",
                    this);
            }
        }

        [Button("????곹깭瑜?UI??諛섏쁺")]
        public void ApplyStateToUi()
        {
            ResolveMissingReferences();

            if (inventoryState == null)
            {
                Debug.LogWarning("[LobbyInventoryStateUiBridge] LobbyInventoryState媛 ?곌껐?섏? ?딆븯?듬땲??", this);
                return;
            }

            IItemDatabase database = ResolveItemDatabase();
            if (database == null)
            {
                Debug.LogWarning(
                    "[LobbyInventoryStateUiBridge] IItemDatabase瑜?李얠? 紐삵뻽?듬땲?? itemId瑜?ItemDataSO濡?蹂듭썝?????놁뒿?덈떎.", this);
                return;
            }

            RefreshSlotViews();

            if (walletSystem != null && inventoryState.HasCredits)
                walletSystem.SetCreditsLocalTest(inventoryState.Credits);

            ApplyLobbyBagLevelFromEquipment(inventoryState.EquipmentItems, database);
            ApplyItemSlots(inventorySlotsRoot, inventoryState.InventoryItems, database, InventoryContainerId);
            ApplyItemSlots(stashSlotsRoot, inventoryState.StashItems, database, StashContainerId);
            ApplyQuickSlotItems(quickSlotSlotsRoot, inventoryState.QuickSlotItems, database);
            if (!ApplyStashGridSlots(stashSlotsRoot, inventoryState.StashItems, database))
                ApplyItemSlots(stashSlotsRoot, inventoryState.StashItems, database, StashContainerId);
            ApplyEquipmentSlots(equipmentSlotsRoot, inventoryState.EquipmentItems, database);
            ApplyItemSlots(quickSlotsRoot, inventoryState.QuickSlotItems, database, QuickSlotContainerId);
        }

        private void ApplyLobbyBagLevelFromEquipment(
            IReadOnlyList<EquipmentSaveDTO> equipmentItems,
            IItemDatabase database)
        {
            LobbyPlayerInventoryUI lobbyInventory = inventorySlotsRoot != null
                ? inventorySlotsRoot.GetComponentInParent<LobbyPlayerInventoryUI>(true)
                : null;

            if (lobbyInventory == null && inventorySlotsRoot != null)
                lobbyInventory = inventorySlotsRoot.GetComponentInChildren<LobbyPlayerInventoryUI>(true);

            if (lobbyInventory == null)
                lobbyInventory = FindFirstObjectByType<LobbyPlayerInventoryUI>(FindObjectsInactive.Include);

            if (lobbyInventory == null)
                return;

            int bagLevel = 0;
            if (equipmentItems != null)
            {
                for (int i = 0; i < equipmentItems.Count; i++)
                {
                    EquipmentSaveDTO equipmentItem = equipmentItems[i];
                    if (equipmentItem == null ||
                        string.IsNullOrWhiteSpace(equipmentItem.slotId) ||
                        string.IsNullOrWhiteSpace(equipmentItem.itemId) ||
                        !equipmentItem.slotId.Contains("Backpack", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    BackpackDataSO backpackData = database.GetById<BackpackDataSO>(equipmentItem.itemId);
                    if (backpackData != null)
                    {
                        bagLevel = Mathf.Clamp(backpackData.backpackLevel, 0, 4);
                        break;
                    }
                }
            }

            lobbyInventory.SetBagLevel(bagLevel);
        }

        private static bool ApplyStashGridSlots(
            Transform root,
            IReadOnlyList<ItemSaveDTO> stashItems,
            IItemDatabase database)
        {
            if (root == null)
                return false;

            StashGridUI stashGridUI = root.GetComponentInParent<StashGridUI>(true);
            if (stashGridUI == null)
                stashGridUI = root.GetComponentInChildren<StashGridUI>(true);

            if (stashGridUI == null)
                return false;

            stashGridUI.ApplySavedStashItems(stashItems, database);
            return true;
        }

        private static List<ItemSaveDTO> CollectItemSlots(Transform root, string containerId)
        {
            List<ItemSaveDTO> items = new();
            int gridWidth = GetGridWidth(containerId);

            if (root == null)
                return items;

            InventorySlotUI[] slots = root.GetComponentsInChildren<InventorySlotUI>(true);
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                slot.PrepareForSaveSnapshot();

                if (!slot.HasItem || slot.CurrentItemData == null)
                    continue;

                if (string.Equals(containerId, InventoryContainerId, StringComparison.OrdinalIgnoreCase) &&
                    slot.SlotKind != InventorySlotKind.Bag)
                {
                    continue;
                }

                items.Add(new ItemSaveDTO
                {
                    itemId = slot.CurrentItemData.itemID,
                    instanceId = string.Empty,
                    containerId = containerId,
                    x = ToGridX(slot.SlotIndex, gridWidth),
                    y = ToGridY(slot.SlotIndex, gridWidth),
                    rotated = false,
                    stackCount = slot.CurrentStackCount
                });
            }

            return items;
        }

        private static List<ItemSaveDTO> CollectQuickSlotSlots(Transform root)
        {
            List<ItemSaveDTO> items = new();
            HashSet<InventorySlotUI> visitedSlots = new();

            if (root != null)
                AddQuickSlotItems(items, root.GetComponentsInChildren<InventorySlotUI>(true), visitedSlots);

            InventorySlotUI[] allSlots =
                FindObjectsByType<InventorySlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            AddQuickSlotItems(items, allSlots, visitedSlots);

            return items;
        }

        private static void AddQuickSlotItems(
            List<ItemSaveDTO> items,
            InventorySlotUI[] slots,
            HashSet<InventorySlotUI> visitedSlots)
        {
            if (items == null || slots == null)
                return;

            HashSet<int> usedQuickSlotIndices = BuildUsedQuickSlotIndices(items);
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null || visitedSlots == null || !visitedSlots.Add(slot))
                    continue;

                slot.PrepareForSaveSnapshot();
                if (slot.SlotKind != InventorySlotKind.QuickSlot || !slot.HasItem || slot.CurrentItemData == null)
                    continue;

                int slotIndex = ResolveQuickSlotIndex(slot, usedQuickSlotIndices);
                RemoveItemAtSlot(items, slotIndex, GetGridWidth(QuickSlotContainerId));
                usedQuickSlotIndices.Add(slotIndex);
                items.Add(new ItemSaveDTO
                {
                    itemId = slot.CurrentItemData.itemID,
                    instanceId = $"{QuickSlotContainerId}_{slotIndex}_{slot.CurrentItemData.itemID}",
                    containerId = QuickSlotContainerId,
                    x = slotIndex,
                    y = 0,
                    rotated = false,
                    stackCount = Mathf.Max(1, slot.CurrentStackCount),
                    currentDurability = GetDefaultDurability(slot.CurrentItemData),
                    currentAmmo = GetDefaultAmmo(slot.CurrentItemData)
                });
            }
        }

        private static HashSet<int> BuildUsedQuickSlotIndices(List<ItemSaveDTO> items)
        {
            HashSet<int> usedIndices = new();
            if (items == null)
                return usedIndices;

            for (int i = 0; i < items.Count; i++)
            {
                ItemSaveDTO item = items[i];
                if (item == null)
                    continue;

                int slotIndex = Mathf.Clamp(ToLinearSlotIndex(item.x, item.y, QuickSlotGridWidth), 0, QuickSlotGridWidth - 1);
                usedIndices.Add(slotIndex);
            }

            return usedIndices;
        }

        private static int ResolveQuickSlotIndex(InventorySlotUI slot, HashSet<int> usedIndices)
        {
            int requestedIndex = Mathf.Clamp(slot != null ? slot.SlotIndex : 0, 0, QuickSlotGridWidth - 1);
            if (usedIndices == null || !usedIndices.Contains(requestedIndex))
                return requestedIndex;

            for (int i = 0; i < QuickSlotGridWidth; i++)
            {
                if (!usedIndices.Contains(i))
                    return i;
            }

            return requestedIndex;
        }

        private static List<ItemSaveDTO> CollectStashSlots(Transform root)
        {
            if (root != null)
            {
                StashGridUI stashGridUI = root.GetComponentInParent<StashGridUI>(true);
                if (stashGridUI == null)
                    stashGridUI = root.GetComponentInChildren<StashGridUI>(true);

                if (stashGridUI != null)
                    return stashGridUI.CaptureSavedStashItems();
            }

            return CollectItemSlots(root, StashContainerId);
        }

        private static List<EquipmentSaveDTO> CollectEquipmentSlots(Transform root)
            {
                List<EquipmentSaveDTO> equipmentItems = new();
                HashSet<InventorySlotUI> visitedSlots = new();

                if (root != null)
                    AddEquipmentSlots(equipmentItems, root.GetComponentsInChildren<InventorySlotUI>(true),
                        visitedSlots);

                if (equipmentItems.Count == 0)
                {
                    InventorySlotUI[] allSlots =
                        FindObjectsByType<InventorySlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    AddEquipmentSlots(equipmentItems, allSlots, visitedSlots);
                }

                return equipmentItems;
            }

            private static void AddEquipmentSlots(
                List<EquipmentSaveDTO> equipmentItems,
                InventorySlotUI[] slots,
                HashSet<InventorySlotUI> visitedSlots)
            {
                if (equipmentItems == null || slots == null)
                    return;

                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot == null || visitedSlots == null || !visitedSlots.Add(slot))
                        continue;

                    slot.PrepareForSaveSnapshot();

                    string slotId = slot.GetEquipmentSaveSlotId();
                    if (string.IsNullOrWhiteSpace(slotId) || !slot.HasItem || slot.CurrentItemData == null)
                        continue;

                    equipmentItems.Add(new EquipmentSaveDTO
                    {
                        slotId = slotId,
                        itemId = slot.CurrentItemData.itemID,
                        instanceId = string.Empty,
                        loadedAmmoId = string.Empty,
                        currentAmmo = GetDefaultAmmo(slot.CurrentItemData),
                        durability = GetDefaultDurability(slot.CurrentItemData)
                    });
                }
            }

            private static int GetDefaultAmmo(ItemDataSO itemData)
            {
                return itemData is WeaponDataSO weaponData
                    ? Mathf.Max(0, weaponData.magSize)
                    : 0;
            }

            private static float GetDefaultDurability(ItemDataSO itemData)
            {
                return itemData switch
                {
                    HelmetDataSO helmetData => Mathf.Max(0f, helmetData.maxDurability),
                    ArmorDataSO armorData => Mathf.Max(0f, armorData.maxDurability),
                    _ => 0f
                };
            }

            private static EquipmentSaveDTO CloneEquipment(EquipmentSaveDTO source)
            {
                return new EquipmentSaveDTO
                {
                    slotId = source.slotId ?? string.Empty,
                    itemId = source.itemId ?? string.Empty,
                    instanceId = source.instanceId ?? string.Empty,
                    loadedAmmoId = source.loadedAmmoId ?? string.Empty,
                    currentAmmo = Mathf.Max(0, source.currentAmmo),
                    durability = Mathf.Max(0f, source.durability)
                };
            }

            private static List<ItemSaveDTO> CloneItems(IReadOnlyList<ItemSaveDTO> source)
            {
                List<ItemSaveDTO> items = new();

                if (source == null)
                    return items;

                for (int i = 0; i < source.Count; i++)
                {
                    ItemSaveDTO item = source[i];
                    if (item == null)
                        continue;

                    items.Add(new ItemSaveDTO
                    {
                        itemId = item.itemId,
                        instanceId = item.instanceId,
                        containerId = item.containerId,
                        x = item.x,
                        y = item.y,
                        rotated = item.rotated,
                        stackCount = item.stackCount,
                        currentDurability = item.currentDurability,
                        currentAmmo = item.currentAmmo
                    });
                }

            return items;
        }

            private static bool PruneQuickSlotItemsAgainstInventory(
                List<ItemSaveDTO> quickSlotItems,
                IReadOnlyList<ItemSaveDTO> inventoryItems)
            {
                if (quickSlotItems == null || quickSlotItems.Count == 0)
                    return false;

                bool changed = false;
                HashSet<int> assignedSlotIndices = new();

                for (int i = quickSlotItems.Count - 1; i >= 0; i--)
                {
                    ItemSaveDTO quickSlotItem = quickSlotItems[i];
                    int slotIndex = Mathf.Clamp(
                        ToLinearSlotIndex(quickSlotItem?.x ?? 0, quickSlotItem?.y ?? 0, QuickSlotGridWidth),
                        0,
                        QuickSlotGridWidth - 1);

                    if (quickSlotItem == null ||
                        string.IsNullOrWhiteSpace(quickSlotItem.itemId) ||
                        !assignedSlotIndices.Add(slotIndex))
                    {
                        quickSlotItems.RemoveAt(i);
                        changed = true;
                        continue;
                    }

                    quickSlotItem.containerId = QuickSlotContainerId;
                    quickSlotItem.x = slotIndex;
                    quickSlotItem.y = 0;

                    int safeStackCount = Mathf.Max(1, quickSlotItem.stackCount);
                    if (quickSlotItem.stackCount != safeStackCount)
                    {
                        quickSlotItem.stackCount = safeStackCount;
                        changed = true;
                    }
                }

                return changed;
            }

            private string ResolveChangedItemContainerId(InventorySlotUI slot)
            {
                if (slot == null)
                    return InventoryContainerId;

                if (stashSlotsRoot != null && slot.transform.IsChildOf(stashSlotsRoot))
                    return StashContainerId;

                if (quickSlotSlotsRoot != null && slot.transform.IsChildOf(quickSlotSlotsRoot))
                    if (quickSlotsRoot != null && slot.transform.IsChildOf(quickSlotsRoot))
                        return QuickSlotContainerId;

                if (slot.SlotKind == InventorySlotKind.QuickSlot)
                    return QuickSlotContainerId;

                if (inventorySlotsRoot != null && slot.transform.IsChildOf(inventorySlotsRoot))
                    return InventoryContainerId;

                string path = BuildTransformPath(slot.transform);
                if (path.Contains("quickslot", StringComparison.OrdinalIgnoreCase))
                    return QuickSlotContainerId;

                return path.Contains("stash", StringComparison.OrdinalIgnoreCase)
                    ? StashContainerId
                    : InventoryContainerId;
            }

            private static List<ItemSaveDTO> ResolveItemList(
                string containerId,
                List<ItemSaveDTO> inventoryItems,
                List<ItemSaveDTO> stashItems,
                List<ItemSaveDTO> quickSlotItems)
            {
                if (string.Equals(containerId, StashContainerId, StringComparison.OrdinalIgnoreCase))
                    return stashItems;

                if (string.Equals(containerId, QuickSlotContainerId, StringComparison.OrdinalIgnoreCase))
                    return quickSlotItems;

                return inventoryItems;
            }

            private static void RemoveItemAtSlot(List<ItemSaveDTO> items, int slotIndex, int gridWidth)
            {
                if (items == null)
                    return;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    ItemSaveDTO item = items[i];
                    if (item != null && ToLinearSlotIndex(item.x, item.y, gridWidth) == slotIndex)
                        items.RemoveAt(i);
                }
            }

            private static void RemoveEquipment(List<EquipmentSaveDTO> equipmentItems, string slotId)
            {
                if (equipmentItems == null || string.IsNullOrWhiteSpace(slotId))
                    return;

                for (int i = equipmentItems.Count - 1; i >= 0; i--)
                {
                    EquipmentSaveDTO item = equipmentItems[i];
                    if (item != null && string.Equals(item.slotId, slotId, StringComparison.OrdinalIgnoreCase))
                        equipmentItems.RemoveAt(i);
                }
            }

            private void ApplyItemSlots(Transform root, IReadOnlyList<ItemSaveDTO> items, IItemDatabase database,
                string containerId)
            {
                InventorySlotUI[] slots = GetSlots(root);
                ClearSlots(slots);
                int gridWidth = GetGridWidth(containerId);

                if (slots.Length == 0 || items == null)
                    return;

                for (int i = 0; i < items.Count; i++)
                {
                    ItemSaveDTO item = items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                        continue;

                    int slotIndex = ToLinearSlotIndex(item.x, item.y, gridWidth);
                    InventorySlotUI slot = FindSlotByIndex(slots, slotIndex);
                    if (slot == null)
                    {
                        Debug.LogWarning(
                            $"[LobbyInventoryStateUiBridge] ?щ’ ?몃뜳??{slotIndex}瑜?李얠? 紐삵빐 ?꾩씠?쒖쓣 蹂듭썝?섏? 紐삵뻽?듬땲?? itemId={item.itemId}",
                            this);
                        continue;
                    }

                    ItemDataSO itemData = database.GetById(item.itemId);
                    if (itemData == null)
                    {
                        Debug.LogWarning($"[LobbyInventoryStateUiBridge] ItemDataSO瑜?李얠? 紐삵뻽?듬땲?? itemId={item.itemId}",
                            this);
                        continue;
                    }

                    slot.SetItem(itemData, Mathf.Max(1, item.stackCount));
                }
            }

            private void ApplyEquipmentSlots(Transform root, IReadOnlyList<EquipmentSaveDTO> equipmentItems,
                IItemDatabase database)
            {
                InventorySlotUI[] slots = GetEquipmentSlotsForApply(root);
                PrepareEquipmentSlotsForTooltip(slots);
                ClearSlots(slots);

                if (slots.Length == 0 || equipmentItems == null)
                    return;

                for (int i = 0; i < equipmentItems.Count; i++)
                {
                    EquipmentSaveDTO equipmentItem = equipmentItems[i];
                    if (equipmentItem == null || string.IsNullOrWhiteSpace(equipmentItem.itemId))
                        continue;

                    InventorySlotUI slot = FindEquipmentSlot(slots, equipmentItem.slotId);
                    if (slot == null)
                    {
                        Debug.LogWarning(
                            $"[LobbyInventoryStateUiBridge] ?μ갑 ?щ’??李얠? 紐삵빐 ?꾩씠?쒖쓣 蹂듭썝?섏? 紐삵뻽?듬땲?? slotId={equipmentItem.slotId}, itemId={equipmentItem.itemId}",
                            this);
                        continue;
                    }

                    ItemDataSO itemData = database.GetById(equipmentItem.itemId);
                    if (itemData == null)
                    {
                        Debug.LogWarning(
                            $"[LobbyInventoryStateUiBridge] ItemDataSO瑜?李얠? 紐삵뻽?듬땲?? itemId={equipmentItem.itemId}", this);
                        continue;
                    }

                    slot.SetItem(itemData, 1);
                }
            }

            private void ApplyQuickSlotItems(Transform root, IReadOnlyList<ItemSaveDTO> quickSlotItems,
                IItemDatabase database)
            {
                InventorySlotUI[] slots = GetQuickSlotsForApply(root);
                ClearSlots(slots);

                if (slots.Length == 0 || quickSlotItems == null)
                    return;

                for (int i = 0; i < quickSlotItems.Count; i++)
                {
                    ItemSaveDTO item = quickSlotItems[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                        continue;

                    ItemDataSO itemData = database.GetById(item.itemId);
                    if (itemData == null)
                    {
                        Debug.LogWarning(
                            $"[LobbyInventoryStateUiBridge] QuickSlot ItemDataSO not found. itemId={item.itemId}",
                            this);
                        continue;
                    }

                    SetQuickSlotsByIndex(slots, Mathf.Max(0, item.x), itemData, Mathf.Max(1, item.stackCount));
                }
            }

            private static InventorySlotUI[] GetSlots(Transform root)
            {
                return root != null
                    ? root.GetComponentsInChildren<InventorySlotUI>(true)
                    : System.Array.Empty<InventorySlotUI>();
            }

            private static InventorySlotUI[] GetEquipmentSlotsForApply(Transform root)
            {
                InventorySlotUI[] rootSlots = GetSlots(root);
                if (rootSlots.Length > 0)
                    return rootSlots;

                InventorySlotUI[] allSlots =
                    FindObjectsByType<InventorySlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                List<InventorySlotUI> equipmentSlots = new();

                for (int i = 0; i < allSlots.Length; i++)
                {
                    InventorySlotUI slot = allSlots[i];
                    if (slot == null || string.IsNullOrWhiteSpace(slot.GetEquipmentSaveSlotId()))
                        continue;

                    equipmentSlots.Add(slot);
                }

                return equipmentSlots.ToArray();
            }

            private static InventorySlotUI[] GetQuickSlotsForApply(Transform root)
            {
                List<InventorySlotUI> quickSlots = new();
                HashSet<InventorySlotUI> visited = new();

                AddQuickSlotsForApply(quickSlots, visited, GetSlots(root));

                InventorySlotUI[] allSlots =
                    FindObjectsByType<InventorySlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                AddQuickSlotsForApply(quickSlots, visited, allSlots);

                return quickSlots.ToArray();
            }

            private static void AddQuickSlotsForApply(
                List<InventorySlotUI> quickSlots,
                HashSet<InventorySlotUI> visited,
                InventorySlotUI[] slots)
            {
                if (quickSlots == null || visited == null || slots == null)
                    return;

                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot == null || !visited.Add(slot))
                        continue;

                    slot.PrepareForSaveSnapshot();
                    if (slot.SlotKind == InventorySlotKind.QuickSlot)
                        quickSlots.Add(slot);
                }
            }

            private static void SetQuickSlotsByIndex(InventorySlotUI[] slots, int slotIndex, ItemDataSO itemData,
                int stackCount)
            {
                if (slots == null || itemData == null)
                    return;

                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot != null && slot.SlotIndex == slotIndex)
                        slot.SetItem(itemData, stackCount);
                }
            }

            private static void ClearSlots(InventorySlotUI[] slots)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null)
                        slots[i].ClearItem();
                }
            }

            private static InventorySlotUI FindSlotByIndex(InventorySlotUI[] slots, int slotIndex)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot != null && slot.SlotIndex == slotIndex)
                        return slot;
                }

                return null;
            }

        private static int GetGridWidth(string containerId)
        {
            if (string.Equals(containerId, QuickSlotContainerId, StringComparison.OrdinalIgnoreCase))
                return QuickSlotGridWidth;

            if (string.Equals(containerId, StashContainerId, StringComparison.OrdinalIgnoreCase))
                return StashGridWidth;

            return PlayerInventoryGridWidth;
        }

            private static int ToGridX(int slotIndex, int gridWidth)
            {
                int width = Mathf.Max(1, gridWidth);
                return Mathf.Max(0, slotIndex) % width;
            }

            private static int ToGridY(int slotIndex, int gridWidth)
            {
                int width = Mathf.Max(1, gridWidth);
                return Mathf.Max(0, slotIndex) / width;
            }

            private static int ToLinearSlotIndex(int x, int y, int gridWidth)
            {
                int width = Mathf.Max(1, gridWidth);
                if (y <= 0 && x >= width)
                    return x;

                return Mathf.Max(0, y) * width + Mathf.Max(0, x);
            }

            private static InventorySlotUI FindEquipmentSlot(InventorySlotUI[] slots, string slotId)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot != null && slot.SlotKind.ToString() == slotId)
                        return slot;
                }

                if (string.Equals(slotId, "primary1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(slotId, "EquipmentPrimaryWeapon", StringComparison.OrdinalIgnoreCase))
                {
                    return FindPrimaryWeaponSlot(slots, false);
                }

                if (string.Equals(slotId, "primary2", StringComparison.OrdinalIgnoreCase))
                {
                    return FindPrimaryWeaponSlot(slots, true);
                }

                return null;
            }

            private static InventorySlotUI FindPrimaryWeaponSlot(InventorySlotUI[] slots, bool secondSlot)
            {
                InventorySlotUI fallback = null;

                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot == null || slot.SlotKind != InventorySlotKind.EquipmentPrimaryWeapon)
                        continue;

                    fallback ??= slot;

                    string path = BuildTransformPath(slot.transform);
                    bool looksLikeSecondSlot =
                        path.Contains("primary2", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("_2", StringComparison.OrdinalIgnoreCase);

                    if (secondSlot == looksLikeSecondSlot)
                        return slot;
                }

                return secondSlot ? null : fallback;
            }

            private static string BuildTransformPath(Transform transform)
            {
                if (transform == null)
                    return string.Empty;

                string path = transform.name;
                Transform parent = transform.parent;

                while (parent != null)
                {
                    path = parent.name + "/" + path;
                    parent = parent.parent;
                }

                return path;
            }

            private IItemDatabase ResolveItemDatabase()
            {
                if (itemDatabase != null)
                    return itemDatabase;

                IItemDatabase database = ServiceLocator.Get<IItemDatabase>();
                if (database != null)
                    return database;

                itemDatabase = FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);
                return itemDatabase;
            }

            private void ResolveMissingReferences()
            {
                if (inventoryState == null)
                    inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

                if (inventorySlotsRoot == null)
                {
                    LobbyPlayerInventoryUI inventoryUI =
                        FindFirstObjectByType<LobbyPlayerInventoryUI>(FindObjectsInactive.Include);
                    if (inventoryUI != null)
                    {
                        inventorySlotsRoot = inventoryUI.transform;
                        Debug.Log(
                            $"[LobbyInventoryStateUiBridge] Auto-bound player inventory root={BuildTransformPath(inventorySlotsRoot)}",
                            this);
                    }
                }

                if (stashSlotsRoot == null)
                {
                    StashGridUI stashGridUI = FindFirstObjectByType<StashGridUI>(FindObjectsInactive.Include);
                    if (stashGridUI != null)
                    {
                        stashSlotsRoot = stashGridUI.transform;
                        Debug.Log(
                            $"[LobbyInventoryStateUiBridge] Auto-bound stash root={BuildTransformPath(stashSlotsRoot)}",
                            this);
                    }
                }

                if (quickSlotSlotsRoot == null)
                {
                    Transform quickSlotPanel = FindSceneTransformByName("QuickSlotPanel");
                    if (quickSlotPanel != null)
                    {
                        quickSlotSlotsRoot = quickSlotPanel;
                        Debug.Log(
                            $"[LobbyInventoryStateUiBridge] Auto-bound quick slot root={BuildTransformPath(quickSlotSlotsRoot)}",
                            this);
                    }
                }

                if (equipmentSlotsRoot == null)
                {
                    Transform equipmentPanel = FindSceneTransformByName("EquipmentPanel");
                    if (equipmentPanel != null)
                    {
                        equipmentSlotsRoot = equipmentPanel;
                        Debug.Log(
                            $"[LobbyInventoryStateUiBridge] Auto-bound equipment root={BuildTransformPath(equipmentSlotsRoot)}",
                            this);
                    }
                }

                if (quickSlotsRoot == null)
                {
                    Transform quickSlotPanel = FindSceneTransformByName("QuickSlotPanel");
                    if (quickSlotPanel != null)
                    {
                        quickSlotsRoot = quickSlotPanel;
                        Debug.Log(
                            $"[LobbyInventoryStateUiBridge] Auto-bound quickslot root={BuildTransformPath(quickSlotsRoot)}",
                            this);
                    }
                }

                if (itemDatabase == null)
                    itemDatabase = FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);

                if (tooltipUI == null)
                    tooltipUI = FindFirstObjectByType<ItemTooltipUI>(FindObjectsInactive.Include);
            }

            private void RefreshSlotViews()
            {
                RefreshLobbyInventory(inventorySlotsRoot);
                RefreshStash(stashSlotsRoot);
                RefreshStash(quickSlotSlotsRoot);
                RefreshStash(equipmentSlotsRoot);
                RefreshQuickSlots(quickSlotsRoot);
            }

            private static void RefreshLobbyInventory(Transform root)
            {
                if (root == null)
                    return;

                LobbyPlayerInventoryUI inventoryUI = root.GetComponentInParent<LobbyPlayerInventoryUI>(true);
                if (inventoryUI == null)
                    inventoryUI = root.GetComponentInChildren<LobbyPlayerInventoryUI>(true);

                if (inventoryUI != null)
                    inventoryUI.RefreshSlots();
            }

            private static void RefreshStash(Transform root)
            {
                if (root == null)
                    return;

                StashGridUI stashGridUI = root.GetComponentInParent<StashGridUI>(true);
                if (stashGridUI == null)
                    stashGridUI = root.GetComponentInChildren<StashGridUI>(true);

                if (stashGridUI != null)
                    stashGridUI.RefreshSlots();
            }

            private static void RefreshQuickSlots(Transform root)
            {
                if (root == null)
                    return;

                InventorySlotUI[] slots = root.GetComponentsInChildren<InventorySlotUI>(true);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null)
                        slots[i].PrepareForSaveSnapshot();
                }
            }

            private void PrepareEquipmentSlotsForTooltip(InventorySlotUI[] slots)
            {
                if (slots == null || slots.Length == 0)
                    return;

                ItemTooltipUI resolvedTooltip = tooltipUI;
                if (resolvedTooltip == null)
                    resolvedTooltip = FindFirstObjectByType<ItemTooltipUI>(FindObjectsInactive.Include);

                for (int i = 0; i < slots.Length; i++)
                {
                    InventorySlotUI slot = slots[i];
                    if (slot == null)
                        continue;

                    slot.PrepareForSaveSnapshot();
                    slot.SetTooltip(resolvedTooltip);
                }

                EnsureEquipmentRaycastCanvasGroups();
            }

            private void EnsureEquipmentRaycastCanvasGroups()
            {
                if (equipmentSlotsRoot == null)
                    return;

                CanvasGroup[] canvasGroups = equipmentSlotsRoot.GetComponentsInParent<CanvasGroup>(true);
                for (int i = 0; i < canvasGroups.Length; i++)
                {
                    CanvasGroup canvasGroup = canvasGroups[i];
                    if (canvasGroup == null)
                        continue;

                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }
            }

            private static Transform FindSceneTransformByName(string objectName)
            {
                if (string.IsNullOrWhiteSpace(objectName))
                    return null;

                Transform[] transforms = FindObjectsByType<Transform>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i] != null && transforms[i].name == objectName)
                        return transforms[i];
                }

                return null;
            }

#if UNITY_EDITOR
            [Button("李몄“ ?먮룞 ?먯깋")]
            private void AutoFindReferences()
            {
                if (inventoryState == null)
                    inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

                if (itemDatabase == null)
                    itemDatabase = FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);

                if (walletSystem == null)
                    walletSystem = FindFirstObjectByType<WalletSystem>(FindObjectsInactive.Include);
            }
#endif
        }
    }

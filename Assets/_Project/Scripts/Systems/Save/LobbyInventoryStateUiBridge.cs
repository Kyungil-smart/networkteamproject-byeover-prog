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
        private const int PlayerInventoryGridWidth = 4;
        private const int StashGridWidth = 10;

        [Header("저장 상태")]
        [SerializeField] private LobbyInventoryState inventoryState;

        [Header("아이템 데이터베이스")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("재화")]
        [SerializeField] private WalletSystem walletSystem;

        [Header("UI 루트")]
        [SerializeField] private Transform inventorySlotsRoot;
        [SerializeField] private Transform stashSlotsRoot;
        [SerializeField] private Transform equipmentSlotsRoot;

        [Header("동기화")]
        [SerializeField] private bool captureOnStart = true;

        private void Start()
        {
            ResolveMissingReferences();

            if (!captureOnStart)
                return;

            if (HasExistingInventoryState())
            {
                Debug.Log("[LobbyInventoryStateUiBridge] Existing lobby inventory state found. Skipping Start capture to avoid overwriting saved inventory with empty UI.", this);
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
                    (inventoryState.EquipmentItems != null && inventoryState.EquipmentItems.Count > 0));
        }

        [Button("UI 상태를 저장 상태로 반영")]
        public void CaptureUiToState()
        {
            ResolveMissingReferences();

            if (inventoryState == null)
            {
                Debug.LogWarning("[LobbyInventoryStateUiBridge] LobbyInventoryState가 연결되지 않았습니다.", this);
                return;
            }

            if (walletSystem != null)
                inventoryState.SetCredits(walletSystem.CurrentCredits);

            if (inventorySlotsRoot != null)
            {
                List<ItemSaveDTO> capturedInventoryItems = CollectItemSlots(inventorySlotsRoot, InventoryContainerId);
                if (capturedInventoryItems.Count > 0 || inventoryState.InventoryItems == null || inventoryState.InventoryItems.Count == 0)
                    inventoryState.SetInventoryItems(capturedInventoryItems);
                else
                    Debug.Log("[LobbyInventoryStateUiBridge] Inventory UI scan returned 0 items. Keeping existing inventory state to avoid wiping saved inventory.", this);
            }
            else
                Debug.LogWarning("[LobbyInventoryStateUiBridge] Inventory slots root missing. Keeping existing inventory state.", this);

            if (stashSlotsRoot != null)
            {
                List<ItemSaveDTO> capturedStashItems = CollectItemSlots(stashSlotsRoot, StashContainerId);
                if (capturedStashItems.Count > 0 || inventoryState.StashItems == null || inventoryState.StashItems.Count == 0)
                    inventoryState.SetStashItems(capturedStashItems);
                else
                    Debug.Log("[LobbyInventoryStateUiBridge] Stash UI scan returned 0 items. Keeping existing stash state to avoid wiping saved stash.", this);
            }
            else
                Debug.LogWarning("[LobbyInventoryStateUiBridge] Stash slots root missing. Keeping existing stash state.", this);

            List<EquipmentSaveDTO> capturedEquipmentItems = CollectEquipmentSlots(equipmentSlotsRoot);
            if (capturedEquipmentItems.Count > 0 || inventoryState.EquipmentItems == null || inventoryState.EquipmentItems.Count == 0)
                inventoryState.SetEquipmentItems(capturedEquipmentItems);
            else
                Debug.Log("[LobbyInventoryStateUiBridge] Equipment UI scan returned 0 items. Keeping existing equipment state to avoid wiping equipped lobby items.", this);
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
            bool inventoryChanged = false;
            bool stashChanged = false;

            for (int i = 0; i < changedSlots.Length; i++)
            {
                InventorySlotUI slot = changedSlots[i];
                if (slot == null)
                    continue;

                slot.PrepareForSaveSnapshot();

                if (slot.SlotKind != InventorySlotKind.Bag)
                    continue;

                string containerId = ResolveChangedItemContainerId(slot);
                List<ItemSaveDTO> targetItems = string.Equals(containerId, StashContainerId, StringComparison.OrdinalIgnoreCase)
                    ? stashItems
                    : inventoryItems;

                int slotIndex = Mathf.Max(0, slot.SlotIndex);
                int gridWidth = GetGridWidth(containerId);

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
                else
                    inventoryChanged = true;
            }

            if (inventoryChanged)
                inventoryState.SetInventoryItems(inventoryItems);

            if (stashChanged)
                inventoryState.SetStashItems(stashItems);

            if (inventoryChanged || stashChanged)
            {
                Debug.Log(
                    $"[LobbyInventoryStateUiBridge] Changed item slots captured. InventoryItems={inventoryItems.Count}, StashItems={stashItems.Count}",
                    this);
            }
        }

        [Button("저장 상태를 UI에 반영")]
        public void ApplyStateToUi()
        {
            ResolveMissingReferences();

            if (inventoryState == null)
            {
                Debug.LogWarning("[LobbyInventoryStateUiBridge] LobbyInventoryState가 연결되지 않았습니다.", this);
                return;
            }

            IItemDatabase database = ResolveItemDatabase();
            if (database == null)
            {
                Debug.LogWarning("[LobbyInventoryStateUiBridge] IItemDatabase를 찾지 못했습니다. itemId를 ItemDataSO로 복원할 수 없습니다.", this);
                return;
            }

            RefreshSlotViews();

            if (walletSystem != null && inventoryState.HasCredits)
                walletSystem.SetCreditsLocalTest(inventoryState.Credits);

            ApplyItemSlots(inventorySlotsRoot, inventoryState.InventoryItems, database, InventoryContainerId);
            ApplyItemSlots(stashSlotsRoot, inventoryState.StashItems, database, StashContainerId);
            ApplyEquipmentSlots(equipmentSlotsRoot, inventoryState.EquipmentItems, database);
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

        private static List<EquipmentSaveDTO> CollectEquipmentSlots(Transform root)
        {
            List<EquipmentSaveDTO> equipmentItems = new();
            HashSet<InventorySlotUI> visitedSlots = new();

            if (root != null)
                AddEquipmentSlots(equipmentItems, root.GetComponentsInChildren<InventorySlotUI>(true), visitedSlots);

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

        private string ResolveChangedItemContainerId(InventorySlotUI slot)
        {
            if (slot == null)
                return InventoryContainerId;

            if (stashSlotsRoot != null && slot.transform.IsChildOf(stashSlotsRoot))
                return StashContainerId;

            if (inventorySlotsRoot != null && slot.transform.IsChildOf(inventorySlotsRoot))
                return InventoryContainerId;

            string path = BuildTransformPath(slot.transform);
            return path.Contains("stash", StringComparison.OrdinalIgnoreCase)
                ? StashContainerId
                : InventoryContainerId;
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

        private void ApplyItemSlots(Transform root, IReadOnlyList<ItemSaveDTO> items, IItemDatabase database, string containerId)
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
                    Debug.LogWarning($"[LobbyInventoryStateUiBridge] 슬롯 인덱스 {slotIndex}를 찾지 못해 아이템을 복원하지 못했습니다. itemId={item.itemId}", this);
                    continue;
                }

                ItemDataSO itemData = database.GetById(item.itemId);
                if (itemData == null)
                {
                    Debug.LogWarning($"[LobbyInventoryStateUiBridge] ItemDataSO를 찾지 못했습니다. itemId={item.itemId}", this);
                    continue;
                }

                slot.SetItem(itemData, Mathf.Max(1, item.stackCount));
            }
        }

        private void ApplyEquipmentSlots(Transform root, IReadOnlyList<EquipmentSaveDTO> equipmentItems, IItemDatabase database)
        {
            InventorySlotUI[] slots = GetEquipmentSlotsForApply(root);
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
                    Debug.LogWarning($"[LobbyInventoryStateUiBridge] 장착 슬롯을 찾지 못해 아이템을 복원하지 못했습니다. slotId={equipmentItem.slotId}, itemId={equipmentItem.itemId}", this);
                    continue;
                }

                ItemDataSO itemData = database.GetById(equipmentItem.itemId);
                if (itemData == null)
                {
                    Debug.LogWarning($"[LobbyInventoryStateUiBridge] ItemDataSO를 찾지 못했습니다. itemId={equipmentItem.itemId}", this);
                    continue;
                }

                slot.SetItem(itemData, 1);
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
            return string.Equals(containerId, StashContainerId, StringComparison.OrdinalIgnoreCase)
                ? StashGridWidth
                : PlayerInventoryGridWidth;
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
                LobbyPlayerInventoryUI inventoryUI = FindFirstObjectByType<LobbyPlayerInventoryUI>(FindObjectsInactive.Include);
                if (inventoryUI != null)
                {
                    inventorySlotsRoot = inventoryUI.transform;
                    Debug.Log($"[LobbyInventoryStateUiBridge] Auto-bound player inventory root={BuildTransformPath(inventorySlotsRoot)}", this);
                }
            }

            if (stashSlotsRoot == null)
            {
                StashGridUI stashGridUI = FindFirstObjectByType<StashGridUI>(FindObjectsInactive.Include);
                if (stashGridUI != null)
                {
                    stashSlotsRoot = stashGridUI.transform;
                    Debug.Log($"[LobbyInventoryStateUiBridge] Auto-bound stash root={BuildTransformPath(stashSlotsRoot)}", this);
                }
            }

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);
        }

        private void RefreshSlotViews()
        {
            RefreshLobbyInventory(inventorySlotsRoot);
            RefreshStash(stashSlotsRoot);
            RefreshStash(equipmentSlotsRoot);
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

#if UNITY_EDITOR
        [Button("참조 자동 탐색")]
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

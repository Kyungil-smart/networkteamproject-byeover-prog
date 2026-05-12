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
                CaptureUiToState();
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
                inventoryState.SetCredits(walletSystem.Credits.Value);

            if (inventorySlotsRoot != null)
                inventoryState.SetInventoryItems(CollectItemSlots(inventorySlotsRoot, InventoryContainerId));
            else
                Debug.LogWarning("[LobbyInventoryStateUiBridge] Inventory slots root missing. Keeping existing inventory state.", this);

            if (stashSlotsRoot != null)
                inventoryState.SetStashItems(CollectItemSlots(stashSlotsRoot, StashContainerId));
            else
                Debug.LogWarning("[LobbyInventoryStateUiBridge] Stash slots root missing. Keeping existing stash state.", this);

            if (equipmentSlotsRoot != null)
                inventoryState.SetEquipmentItems(CollectEquipmentSlots(equipmentSlotsRoot));
            else
                Debug.LogWarning("[LobbyInventoryStateUiBridge] Equipment slots root missing. Keeping existing equipment state.", this);
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

            ApplyItemSlots(inventorySlotsRoot, inventoryState.InventoryItems, database);
            ApplyItemSlots(stashSlotsRoot, inventoryState.StashItems, database);
            ApplyEquipmentSlots(equipmentSlotsRoot, inventoryState.EquipmentItems, database);
        }

        private static List<ItemSaveDTO> CollectItemSlots(Transform root, string containerId)
        {
            List<ItemSaveDTO> items = new();

            if (root == null)
                return items;

            InventorySlotUI[] slots = root.GetComponentsInChildren<InventorySlotUI>(true);
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null || !slot.HasItem || slot.CurrentItemData == null)
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
                    x = slot.SlotIndex,
                    y = 0,
                    rotated = false,
                    stackCount = slot.CurrentStackCount
                });
            }

            return items;
        }

        private static List<EquipmentSaveDTO> CollectEquipmentSlots(Transform root)
        {
            List<EquipmentSaveDTO> equipmentItems = new();

            if (root == null)
                return equipmentItems;

            InventorySlotUI[] slots = root.GetComponentsInChildren<InventorySlotUI>(true);
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null || !slot.HasItem || slot.CurrentItemData == null)
                    continue;

                equipmentItems.Add(new EquipmentSaveDTO
                {
                    slotId = slot.SlotKind.ToString(),
                    itemId = slot.CurrentItemData.itemID,
                    instanceId = string.Empty,
                    loadedAmmoId = string.Empty,
                    currentAmmo = 0,
                    durability = 0f
                });
            }

            return equipmentItems;
        }

        private void ApplyItemSlots(Transform root, IReadOnlyList<ItemSaveDTO> items, IItemDatabase database)
        {
            InventorySlotUI[] slots = GetSlots(root);
            ClearSlots(slots);

            if (slots.Length == 0 || items == null)
                return;

            for (int i = 0; i < items.Count; i++)
            {
                ItemSaveDTO item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                    continue;

                InventorySlotUI slot = FindSlotByIndex(slots, item.x);
                if (slot == null)
                {
                    Debug.LogWarning($"[LobbyInventoryStateUiBridge] 슬롯 인덱스 {item.x}를 찾지 못해 아이템을 복원하지 못했습니다. itemId={item.itemId}", this);
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
            InventorySlotUI[] slots = GetSlots(root);
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

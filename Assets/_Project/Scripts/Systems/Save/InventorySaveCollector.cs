using System.Collections.Generic;

using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Systems;

namespace DeadZone.Systems.Save
{
    // 로비 저장 시 플레이어 인벤토리 상태를 수집
    // 기존 UI 상태뿐 아니라, 로컬 플레이어의 GridInventory.ServerGrid도 저장 상태로 반영
    public class InventorySaveCollector : MonoBehaviour
    {
        [Header("저장 상태")]
        [SerializeField]
        private LobbyInventoryState inventoryState;

        [Header("UI 동기화")]
        [SerializeField]
        private LobbyInventoryStateUiBridge uiBridge;

        [SerializeField]
        private bool captureUiBeforeCollect = true;

        [Header("플레이어 인벤토리 수집")]
        [SerializeField]
        private bool capturePlayerGridInventory = true;

        [SerializeField]
        private bool capturePlayerEquipmentSlots = true;

        [SerializeField]
        private GridInventory debugTargetInventory;

        [Header("로그")]
        [SerializeField]
        private bool logCollectResult = true;

        public void Collect(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            ResolveMissingReferences();

            if (inventoryState == null)
            {
                Debug.LogWarning("[InventorySaveCollector] LobbyInventoryState가 연결되지 않았습니다. 저장용 인벤토리 상태 오브젝트를 연결해야 합니다.", this);
                return;
            }

            if (captureUiBeforeCollect && uiBridge != null)
                uiBridge.CaptureUiToState();

            if (capturePlayerGridInventory)
                CaptureLocalPlayerGridInventoryToState();

            if (capturePlayerEquipmentSlots)
                CaptureLocalPlayerEquipmentSlotsToState();

            dto.hasCredits = inventoryState.HasCredits;
            dto.credits = inventoryState.Credits;
            bool uiCanAuthorInventory = captureUiBeforeCollect && uiBridge != null && uiBridge.CanAuthorInventorySection;
            bool uiCanAuthorStash = captureUiBeforeCollect && uiBridge != null && uiBridge.CanAuthorStashSection;
            bool uiCanAuthorEquipment = captureUiBeforeCollect && uiBridge != null && uiBridge.CanAuthorEquipmentSection;
            bool uiCanAuthorQuickSlot = captureUiBeforeCollect && uiBridge != null && uiBridge.CanAuthorQuickSlotSection;

            dto.hasInventorySection = uiCanAuthorInventory || HasUiInventoryState();
            dto.hasStashSection = uiCanAuthorStash ||
                                  (inventoryState.StashItems != null && inventoryState.StashItems.Count > 0);
            dto.hasEquipmentSection = uiCanAuthorEquipment || HasUiEquipmentState();
            dto.hasQuickSlotSection = uiCanAuthorQuickSlot || HasUiQuickSlotState();

            dto.inventoryItems.Clear();
            dto.stashItems.Clear();
            dto.quickSlotItems ??= new List<ItemSaveDTO>();
            dto.quickSlotItems.Clear();
            dto.equipmentItems.Clear();

            dto.inventoryItems.AddRange(inventoryState.InventoryItems);
            dto.stashItems.AddRange(inventoryState.StashItems);
            dto.quickSlotItems.AddRange(inventoryState.QuickSlotItems);
            dto.equipmentItems.AddRange(inventoryState.EquipmentItems);

            if (logCollectResult)
            {
                Debug.Log(
                    $"[InventorySaveCollector] 저장 데이터 수집 완료\n" +
                    $"InventoryItems: {dto.inventoryItems.Count}\n" +
                    $"StashItems: {dto.stashItems.Count}\n" +
                    $"QuickSlotItems: {dto.quickSlotItems.Count}\n" +
                    $"EquipmentItems: {dto.equipmentItems.Count}",
                    this
                );
            }
        }

        private void CaptureLocalPlayerGridInventoryToState()
        {
            GridInventory gridInventory = ResolveLocalPlayerGridInventory();

            if (gridInventory == null)
            {
                if (HasUiInventoryState())
                    Debug.Log("[InventorySaveCollector] Local player GridInventory not found. Keeping UI inventory state.", this);
                else
                    Debug.LogWarning("[InventorySaveCollector] Local player GridInventory not found and UI inventory state is empty.", this);
                return;
            }

            List<ItemSaveDTO> items = new();

            for (int i = 0; i < gridInventory.ServerGrid.Count; i++)
            {
                ItemSlotData slot = gridInventory.ServerGrid[i];
                string itemId = slot.itemId.ToString();

                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                items.Add(new ItemSaveDTO
                {
                    itemId = itemId,
                    instanceId = $"{itemId}_{slot.gridX}_{slot.gridY}_{i}",
                    containerId = "Inventory",
                    x = slot.gridX,
                    y = slot.gridY,
                    rotated = slot.rotated,
                    stackCount = Mathf.Max(1, slot.stackCount),
                    currentDurability = Mathf.Max(0f, slot.currentDurability),
                    currentAmmo = Mathf.Max(0, slot.currentAmmo)
                });
            }

            if (items.Count == 0 && inventoryState.InventoryItems != null && inventoryState.InventoryItems.Count > 0)
            {
                Debug.LogWarning(
                    "[InventorySaveCollector] GridInventory capture returned 0 items. Keeping UI-captured inventory state to avoid wiping lobby inventory before facility scene load.",
                    gridInventory);
                return;
            }

            inventoryState.SetInventoryItems(items);

            if (logCollectResult)
            {
                Debug.Log(
                    $"[InventorySaveCollector] 로컬 플레이어 GridInventory 수집 완료\n" +
                    $"오브젝트: {gridInventory.gameObject.name}\n" +
                    $"저장 아이템 수: {items.Count}",
                    gridInventory
                );
            }
        }

        private void CaptureLocalPlayerEquipmentSlotsToState()
        {
            EquipmentSlots equipmentSlots = ResolveLocalPlayerEquipmentSlots();

            if (equipmentSlots == null)
            {
                if (HasUiEquipmentState())
                    Debug.Log("[InventorySaveCollector] Local player EquipmentSlots not found. Keeping UI equipment state.", this);
                else
                    Debug.LogWarning("[InventorySaveCollector] Local player EquipmentSlots not found and UI equipment state is empty.", this);
                return;
            }

            List<EquipmentSaveDTO> items = new();

            AddEquipment(items, "EquipmentHead", equipmentSlots.HeadSlotId.Value.ToString(), string.Empty, 0, equipmentSlots.HelmetDurability.Value);
            AddEquipment(items, "EquipmentArmor", equipmentSlots.TorsoSlotId.Value.ToString(), string.Empty, 0, equipmentSlots.ArmorDurability.Value);
            AddEquipment(items, "EquipmentBackpack", equipmentSlots.BackpackSlotId.Value.ToString(), string.Empty, 0, 0f);
            AddWeaponEquipment(items, "EquipmentPrimaryWeapon", equipmentSlots.Primary1Id.Value.ToString(), equipmentSlots.Primary1State.Value);
            AddWeaponEquipment(items, "primary2", equipmentSlots.Primary2Id.Value.ToString(), equipmentSlots.Primary2State.Value);
            AddWeaponEquipment(items, "EquipmentSecondaryWeapon", equipmentSlots.SecondaryId.Value.ToString(), equipmentSlots.SecondaryState.Value);
            AddEquipment(items, "EquipmentMeleeWeapon", equipmentSlots.MeleeId.Value.ToString(), string.Empty, 0, 0f);

            MergeUiEquipmentState(items);
            inventoryState.SetEquipmentItems(items);

            if (logCollectResult)
            {
                Debug.Log(
                    $"[InventorySaveCollector] Local player EquipmentSlots captured\n" +
                    $"Object: {equipmentSlots.gameObject.name}\n" +
                    $"Saved equipment items: {items.Count}",
                    equipmentSlots
                );
            }
        }

        private bool HasUiInventoryState()
        {
            return inventoryState != null &&
                   inventoryState.InventoryItems != null &&
                   inventoryState.InventoryItems.Count > 0;
        }

        private bool HasUiEquipmentState()
        {
            return inventoryState != null &&
                   inventoryState.EquipmentItems != null &&
                   inventoryState.EquipmentItems.Count > 0;
        }

        private bool HasUiQuickSlotState()
        {
            return inventoryState != null &&
                   inventoryState.QuickSlotItems != null &&
                   inventoryState.QuickSlotItems.Count > 0;
        }

        private void MergeUiEquipmentState(List<EquipmentSaveDTO> serverItems)
        {
            if (serverItems == null || inventoryState?.EquipmentItems == null)
                return;

            for (int i = 0; i < inventoryState.EquipmentItems.Count; i++)
            {
                EquipmentSaveDTO uiItem = inventoryState.EquipmentItems[i];
                if (uiItem == null || string.IsNullOrWhiteSpace(uiItem.itemId))
                    continue;

                string canonicalSlotId = NormalizeEquipmentSlotId(uiItem.slotId);
                EquipmentSaveDTO serverItem = FindEquipmentItem(serverItems, canonicalSlotId);

                if (serverItem == null)
                {
                    serverItems.Add(CloneEquipmentItem(uiItem, canonicalSlotId));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(serverItem.itemId))
                {
                    serverItem.itemId = uiItem.itemId;
                    serverItem.instanceId = string.IsNullOrWhiteSpace(uiItem.instanceId)
                        ? $"{canonicalSlotId}_{uiItem.itemId}"
                        : uiItem.instanceId;
                }
            }
        }

        private static EquipmentSaveDTO FindEquipmentItem(List<EquipmentSaveDTO> items, string slotId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                EquipmentSaveDTO item = items[i];
                if (item != null &&
                    string.Equals(NormalizeEquipmentSlotId(item.slotId), slotId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static EquipmentSaveDTO CloneEquipmentItem(EquipmentSaveDTO source, string slotId)
        {
            return new EquipmentSaveDTO
            {
                slotId = slotId,
                itemId = source.itemId,
                instanceId = string.IsNullOrWhiteSpace(source.instanceId)
                    ? $"{slotId}_{source.itemId}"
                    : source.instanceId,
                loadedAmmoId = source.loadedAmmoId ?? string.Empty,
                currentAmmo = Mathf.Max(0, source.currentAmmo),
                durability = Mathf.Max(0f, source.durability)
            };
        }

        private static string NormalizeEquipmentSlotId(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                return string.Empty;

            return slotId switch
            {
                "EquipmentHead" => "EquipmentHead",
                "Head" => "EquipmentHead",
                "EquipmentArmor" => "EquipmentArmor",
                "Torso" => "EquipmentArmor",
                "EquipmentBackpack" => "EquipmentBackpack",
                "Backpack" => "EquipmentBackpack",
                "EquipmentPrimaryWeapon" => "EquipmentPrimaryWeapon",
                "Primary1" => "EquipmentPrimaryWeapon",
                "primary1" => "EquipmentPrimaryWeapon",
                "primary2" => "primary2",
                "Primary2" => "primary2",
                "EquipmentSecondaryWeapon" => "EquipmentSecondaryWeapon",
                "Secondary" => "EquipmentSecondaryWeapon",
                "EquipmentMeleeWeapon" => "EquipmentMeleeWeapon",
                "Melee" => "EquipmentMeleeWeapon",
                _ => slotId
            };
        }

        private static void AddWeaponEquipment(List<EquipmentSaveDTO> items, string slotId, string itemId, WeaponState weaponState)
        {
            AddEquipment(
                items,
                slotId,
                itemId,
                weaponState.loadedAmmoId.ToString(),
                weaponState.currentAmmo,
                0f);
        }

        private static void AddEquipment(
            List<EquipmentSaveDTO> items,
            string slotId,
            string itemId,
            string loadedAmmoId,
            int currentAmmo,
            float durability)
        {
            if (items == null || string.IsNullOrWhiteSpace(itemId))
                return;

            items.Add(new EquipmentSaveDTO
            {
                slotId = slotId,
                itemId = itemId,
                instanceId = $"{slotId}_{itemId}",
                loadedAmmoId = loadedAmmoId ?? string.Empty,
                currentAmmo = Mathf.Max(0, currentAmmo),
                durability = Mathf.Max(0f, durability)
            });
        }

        private GridInventory ResolveLocalPlayerGridInventory()
        {
            if (debugTargetInventory != null)
                return debugTargetInventory;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient localClient))
                {
                    if (localClient.PlayerObject != null)
                    {
                        GridInventory playerInventory = localClient.PlayerObject.GetComponent<GridInventory>();

                        if (playerInventory != null)
                            return playerInventory;

                        playerInventory = localClient.PlayerObject.GetComponentInChildren<GridInventory>(true);

                        if (playerInventory != null)
                            return playerInventory;
                    }
                }
            }

            GridInventory[] inventories = FindObjectsByType<GridInventory>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                GridInventory inventory = inventories[i];

                if (inventory == null)
                    continue;

                if (inventory.IsOwner)
                    return inventory;
            }

            if (inventories.Length == 1)
                return inventories[0];

            return null;
        }

        private EquipmentSlots ResolveLocalPlayerEquipmentSlots()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient localClient))
                {
                    if (localClient.PlayerObject != null)
                    {
                        EquipmentSlots equipmentSlots = localClient.PlayerObject.GetComponent<EquipmentSlots>();

                        if (equipmentSlots != null)
                            return equipmentSlots;

                        equipmentSlots = localClient.PlayerObject.GetComponentInChildren<EquipmentSlots>(true);

                        if (equipmentSlots != null)
                            return equipmentSlots;
                    }
                }
            }

            EquipmentSlots[] equipmentSlotsList = FindObjectsByType<EquipmentSlots>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < equipmentSlotsList.Length; i++)
            {
                EquipmentSlots equipmentSlots = equipmentSlotsList[i];

                if (equipmentSlots == null)
                    continue;

                if (equipmentSlots.IsOwner)
                    return equipmentSlots;
            }

            if (equipmentSlotsList.Length == 1)
                return equipmentSlotsList[0];

            return null;
        }

        private void ResolveMissingReferences()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (uiBridge == null)
                uiBridge = FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);
        }

#if UNITY_EDITOR
        [Button("참조 자동 탐색")]
        private void AutoFindReferences()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (uiBridge == null)
                uiBridge = FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);

            if (debugTargetInventory == null)
                debugTargetInventory = FindFirstObjectByType<GridInventory>(FindObjectsInactive.Include);
        }

        [Button("로컬 플레이어 인벤토리 수집 테스트")]
        private void DebugCaptureLocalPlayerInventory()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            CaptureLocalPlayerGridInventoryToState();
        }
#endif
    }
}

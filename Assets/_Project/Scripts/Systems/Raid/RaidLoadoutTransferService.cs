using System;
using System.Collections.Generic;

using DeadZone.Actors;
using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Save;

using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeadZone.Systems.Raid
{
    public static class RaidLoadoutTransferService
    {
        private static readonly Dictionary<ulong, RaidLoadoutSaveData> loadoutsByClientId = new();

        public static void Clear()
        {
            loadoutsByClientId.Clear();
        }

        public static void SaveLoadoutsForClients(IReadOnlyList<ulong> clientIds)
        {
            if (!TryGetServerNetworkManager(out NetworkManager networkManager))
                return;

            if (clientIds == null || clientIds.Count == 0)
            {
                Debug.LogWarning("[RaidLoadout] No clientIds supplied for loadout snapshot.");
                return;
            }

            RemoveUnexpectedLoadouts(clientIds);

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];

                if (loadoutsByClientId.TryGetValue(clientId, out RaidLoadoutSaveData submittedLoadout) &&
                    HasMeaningfulLoadout(submittedLoadout))
                {
                    Debug.Log(
                        $"[RaidLoadout] Using submitted lobby loadout clientId={clientId}, inventory={submittedLoadout.inventoryItems.Count}, quickSlots={submittedLoadout.quickSlotItems.Count}, equipment={CountEquippedItems(submittedLoadout.equipmentItems)}, current={submittedLoadout.currentEquippedItemId}");
                    continue;
                }

                bool hasSavedLobbyLoadout = TryBuildSavedLobbyLoadout(clientId, out RaidLoadoutSaveData savedLobbyLoadout);

                if (hasSavedLobbyLoadout)
                {
                    loadoutsByClientId[clientId] = savedLobbyLoadout;
                    Debug.Log(
                        $"[RaidLoadout] Saved loadout from lobby save snapshot clientId={clientId}, inventory={savedLobbyLoadout.inventoryItems.Count}, quickSlots={savedLobbyLoadout.quickSlotItems.Count}, equipment={CountEquippedItems(savedLobbyLoadout.equipmentItems)}, current={savedLobbyLoadout.currentEquippedItemId}");
                    continue;
                }

                if (!TryResolveLoadoutSource(networkManager, clientId, out GameObject playerObject))
                {
                    RaidLoadoutSaveData emptyLoadout = CreateEmptyLoadout(clientId);
                    loadoutsByClientId[clientId] = emptyLoadout;
                    Debug.Log($"[RaidLoadout] Saved empty loadout clientId={clientId}. No lobby inventory/equipment was selected.");
                    continue;
                }

                RaidLoadoutSaveData liveLoadout = CreateLoadout(clientId, playerObject);
                if (liveLoadout == null)
                    continue;

                loadoutsByClientId[clientId] = liveLoadout;

                Debug.Log(
                    $"[RaidLoadout] Saved loadout clientId={clientId}, inventory={liveLoadout.inventoryItems.Count}, equipment={CountEquippedItems(liveLoadout.equipmentItems)}, quickslots={liveLoadout.quickSlotItems.Count}",
                    playerObject);
            }
        }

        public static bool StoreLocalLobbyLoadoutForLocalClient()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            if (!TryBuildSavedLobbyLoadout(networkManager.LocalClientId, out RaidLoadoutSaveData loadout))
                return false;

            loadoutsByClientId[networkManager.LocalClientId] = loadout;
            return true;
        }

        public static string CreateLocalLobbyLoadoutJson()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return string.Empty;

            if (!TryBuildSavedLobbyLoadout(networkManager.LocalClientId, out RaidLoadoutSaveData loadout))
                return string.Empty;

            return JsonUtility.ToJson(loadout);
        }

        public static bool StoreSubmittedLobbyLoadout(ulong clientId, string loadoutJson)
        {
            if (string.IsNullOrWhiteSpace(loadoutJson))
                return false;

            RaidLoadoutSaveData loadout;

            try
            {
                loadout = JsonUtility.FromJson<RaidLoadoutSaveData>(loadoutJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RaidLoadout] Submitted loadout JSON could not be parsed. clientId={clientId}, error={ex.Message}");
                return false;
            }

            if (!HasMeaningfulLoadout(loadout))
                return false;

            loadout.clientId = clientId;
            loadoutsByClientId[clientId] = loadout;
            Debug.Log(
                $"[RaidLoadout] Submitted lobby loadout received clientId={clientId}, inventory={loadout.inventoryItems.Count}, quickSlots={loadout.quickSlotItems.Count}, equipment={CountEquippedItems(loadout.equipmentItems)}, current={loadout.currentEquippedItemId}");
            return true;
        }

        public static bool HasLoadoutForClient(ulong clientId)
        {
            return loadoutsByClientId.ContainsKey(clientId);
        }

        public static bool TryApplyLoadout(ulong clientId, GameObject playerObject)
        {
            if (!TryGetServerNetworkManager(out _))
                return false;

            if (playerObject == null)
            {
                Debug.LogWarning($"[RaidLoadout] Cannot apply loadout. PlayerObject is null clientId={clientId}");
                return false;
            }

            if (!loadoutsByClientId.TryGetValue(clientId, out RaidLoadoutSaveData loadout))
            {
                if (!TryBuildSavedLobbyLoadout(clientId, out loadout))
                {
                    loadout = CreateEmptyLoadout(clientId);
                    Debug.Log($"[RaidLoadout] Applying empty loadout clientId={clientId}. No lobby inventory/equipment was selected.");
                }

                loadoutsByClientId[clientId] = loadout;
                Debug.Log(
                    $"[RaidLoadout] Recovered loadout during apply clientId={clientId}, inventory={loadout.inventoryItems.Count}, quickSlots={loadout.quickSlotItems.Count}, equipment={CountEquippedItems(loadout.equipmentItems)}, current={loadout.currentEquippedItemId}",
                    playerObject);
            }

            EquipmentSlots equipment = playerObject.GetComponent<EquipmentSlots>();
            GridInventory inventory = playerObject.GetComponent<GridInventory>();

            if (equipment == null)
            {
                Debug.LogWarning($"[RaidLoadout] Missing EquipmentSlots on PlayerPrefab clientId={clientId}", playerObject);
                return false;
            }

            if (inventory == null)
            {
                Debug.LogWarning($"[RaidLoadout] Missing GridInventory on PlayerPrefab clientId={clientId}", playerObject);
                return false;
            }

            int equipmentCount = equipment.ImportSnapshot(loadout.equipmentItems, loadout.currentEquippedItemId);
            int inventoryCount = inventory.ImportSnapshot(loadout.inventoryItems);
            int quickSlotCount = ApplyQuickSlotsToUi(loadout.quickSlotItems);

            Debug.Log(
                $"[RaidLoadout] Applied inventory items={inventoryCount}, equipment={equipmentCount}, quickslots={quickSlotCount}, clientId={clientId}",
                playerObject);

            return true;
        }

        public static bool TryGetQuickSlotItemsForLocalClient(out IReadOnlyList<QuickSlotSaveData> quickSlotItems)
        {
            quickSlotItems = null;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            if (!loadoutsByClientId.TryGetValue(networkManager.LocalClientId, out RaidLoadoutSaveData loadout) ||
                loadout.quickSlotItems == null ||
                loadout.quickSlotItems.Count == 0)
            {
                return false;
            }

            quickSlotItems = loadout.quickSlotItems;
            return true;
        }

        public static int ApplyLocalQuickSlotsToUi()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return 0;

            return loadoutsByClientId.TryGetValue(networkManager.LocalClientId, out RaidLoadoutSaveData loadout)
                ? ApplyQuickSlotsToUi(loadout.quickSlotItems)
                : 0;
        }

        public static bool CaptureLocalQuickSlotsFromUi()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return false;

            if (!loadoutsByClientId.TryGetValue(networkManager.LocalClientId, out RaidLoadoutSaveData loadout))
                return false;

            loadout.quickSlotItems = CaptureCurrentQuickSlotSnapshot();
            loadoutsByClientId[networkManager.LocalClientId] = loadout;
            return true;
        }

        private static RaidLoadoutSaveData CreateEmptyLoadout(ulong clientId)
        {
            return new RaidLoadoutSaveData
            {
                clientId = clientId,
                currentEquippedItemId = string.Empty
            };
        }

        private static RaidLoadoutSaveData CreateLoadout(ulong clientId, GameObject playerObject)
        {
            GridInventory inventory = playerObject.GetComponent<GridInventory>();
            EquipmentSlots equipment = playerObject.GetComponent<EquipmentSlots>();

            if (inventory == null)
            {
                Debug.LogWarning($"[RaidLoadout] Cannot save inventory. Missing GridInventory clientId={clientId}", playerObject);
                return null;
            }

            if (equipment == null)
            {
                Debug.LogWarning($"[RaidLoadout] Cannot save equipment. Missing EquipmentSlots clientId={clientId}", playerObject);
                return null;
            }

            return new RaidLoadoutSaveData
            {
                clientId = clientId,
                inventoryItems = inventory.ExportSnapshot(),
                equipmentItems = equipment.ExportSnapshot(),
                quickSlotItems = CaptureCurrentQuickSlotSnapshot(),
                currentEquippedItemId = equipment.CurrentEquipped.Value.ToString()
            };
        }

        private static bool TryBuildSavedLobbyLoadout(ulong clientId, out RaidLoadoutSaveData loadout)
        {
            loadout = null;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || clientId != networkManager.LocalClientId)
                return false;

            LobbySaveDTO dto = CreateLobbySaveDTOFromSceneState();
            LobbySaveDTO fallbackDto = CreateLobbySaveDTOFromCloud();

            MergeMissingLobbyLoadoutSections(dto, fallbackDto);

            if (!HasMeaningfulLobbyInventory(dto))
                dto = fallbackDto;

            if (!HasMeaningfulLobbyInventory(dto))
                return false;

            loadout = ToRaidLoadout(clientId, dto);
            return HasMeaningfulLoadout(loadout);
        }

        private static LobbySaveDTO CreateLobbySaveDTOFromSceneState()
        {
            LobbyInventoryStateUiBridge uiBridge =
                Object.FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);
            uiBridge?.CaptureUiToState();

            LobbyInventoryState inventoryState =
                Object.FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (inventoryState == null)
                return null;

            LobbySaveDTO dto = new LobbySaveDTO
            {
                hasCredits = inventoryState.HasCredits,
                credits = inventoryState.Credits,
                hasInventorySection = true,
                hasStashSection = true,
                hasEquipmentSection = true,
                hasQuickSlotSection = true
            };

            AddRange(dto.inventoryItems, inventoryState.InventoryItems);
            AddRange(dto.stashItems, inventoryState.StashItems);
            AddRange(dto.quickSlotItems, inventoryState.QuickSlotItems);
            AddRange(dto.equipmentItems, inventoryState.EquipmentItems);

            return dto;
        }

        private static void MergeMissingLobbyLoadoutSections(LobbySaveDTO target, LobbySaveDTO fallback)
        {
            if (target == null || fallback == null)
                return;

            if (!HasItems(target.inventoryItems) && HasItems(fallback.inventoryItems))
            {
                target.inventoryItems.Clear();
                AddRange(target.inventoryItems, fallback.inventoryItems);
                target.hasInventorySection = fallback.hasInventorySection;
            }

            if (!HasItems(target.equipmentItems) && HasItems(fallback.equipmentItems))
            {
                target.equipmentItems.Clear();
                AddRange(target.equipmentItems, fallback.equipmentItems);
                target.hasEquipmentSection = fallback.hasEquipmentSection;
            }

            if (!HasItems(target.quickSlotItems) && HasItems(fallback.quickSlotItems))
            {
                target.quickSlotItems.Clear();
                AddRange(target.quickSlotItems, fallback.quickSlotItems);
                target.hasQuickSlotSection = fallback.hasQuickSlotSection;
            }
        }

        private static LobbySaveDTO CreateLobbySaveDTOFromCloud()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();

            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                cloudSaveSystem = Object.FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);

            if (cloudSaveSystem == null || !cloudSaveSystem.enabled || !cloudSaveSystem.HasLoadedData)
                return null;

            return cloudSaveSystem.CreateLobbySaveDTOFromCurrentData();
        }

        private static RaidLoadoutSaveData ToRaidLoadout(ulong clientId, LobbySaveDTO dto)
        {
            RaidLoadoutSaveData loadout = new RaidLoadoutSaveData
            {
                clientId = clientId
            };

            IReadOnlyList<ItemSaveDTO> inventoryItems = dto.inventoryItems;
            List<ItemSaveDTO> quickSlotItems = CreateValidQuickSlotItems(dto.quickSlotItems, dto.inventoryItems);
            List<ItemSaveDTO> ammoLookupItems = CreateAmmoLookupItems(dto.inventoryItems);

            if (inventoryItems != null)
            {
                for (int i = 0; i < inventoryItems.Count; i++)
                {
                    ItemSaveDTO item = inventoryItems[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                        continue;

                    loadout.inventoryItems.Add(new InventoryItemSaveData
                    {
                        itemId = item.itemId,
                        instanceId = item.instanceId,
                        gridX = ResolveGridX(item),
                        gridY = ResolveGridY(item),
                        rotated = item.rotated,
                        stackCount = Mathf.Max(1, item.stackCount),
                        currentDurability = Mathf.Max(0f, item.currentDurability),
                        currentAmmo = Mathf.Max(0, item.currentAmmo)
                    });
                }
            }

            if (quickSlotItems != null)
            {
                for (int i = 0; i < quickSlotItems.Count; i++)
                {
                    ItemSaveDTO item = quickSlotItems[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                        continue;

                    loadout.quickSlotItems.Add(new QuickSlotSaveData
                    {
                        itemId = item.itemId,
                        instanceId = item.instanceId,
                        slotIndex = Mathf.Clamp(item.x, 0, 5),
                        stackCount = Mathf.Max(1, item.stackCount),
                        currentDurability = Mathf.Max(0f, item.currentDurability),
                        currentAmmo = Mathf.Max(0, item.currentAmmo)
                    });
                }

            }

            if (dto.equipmentItems != null)
            {
                for (int i = 0; i < dto.equipmentItems.Count; i++)
                {
                    EquipmentSaveDTO equipment = dto.equipmentItems[i];
                    if (equipment == null || string.IsNullOrWhiteSpace(equipment.itemId))
                        continue;

                    string raidSlotId = ToRaidEquipmentSlotId(equipment.slotId);
                    if (string.IsNullOrWhiteSpace(raidSlotId))
                        continue;

                    string loadedAmmoId = equipment.loadedAmmoId;
                    int currentAmmo = Mathf.Max(0, equipment.currentAmmo);
                    if (string.IsNullOrWhiteSpace(loadedAmmoId) && currentAmmo > 0)
                    {
                        loadedAmmoId = ResolveLoadedAmmoId(equipment.itemId, ammoLookupItems);

                        if (!string.IsNullOrWhiteSpace(loadedAmmoId))
                        {
                            Debug.Log(
                                $"[RaidLoadout] Inferred loaded ammo for equipped weapon. weapon={equipment.itemId}, ammo={loadedAmmoId}, currentAmmo={currentAmmo}");
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[RaidLoadout] Equipped weapon has ammo count but no compatible loaded ammo id. weapon={equipment.itemId}, currentAmmo={currentAmmo}");
                        }
                    }

                    loadout.equipmentItems.Add(new EquipmentSaveData
                    {
                        slotId = raidSlotId,
                        itemId = equipment.itemId,
                        instanceId = equipment.instanceId,
                        loadedAmmoId = loadedAmmoId,
                        currentAmmo = currentAmmo,
                        currentDurability = Mathf.Max(0f, equipment.durability)
                    });

                    if (string.IsNullOrWhiteSpace(loadout.currentEquippedItemId) && IsEquippableWeaponSlot(raidSlotId))
                        loadout.currentEquippedItemId = equipment.itemId;
                }
            }

            return loadout;
        }

        private static List<QuickSlotSaveData> CaptureCurrentQuickSlotSnapshot()
        {
            List<QuickSlotSaveData> snapshot = new();
            InventorySlotUI[] quickSlots = ResolveQuickSlotSlots();

            for (int i = 0; i < quickSlots.Length; i++)
            {
                InventorySlotUI slot = quickSlots[i];
                if (slot == null || !slot.HasItem || slot.CurrentItemData == null)
                    continue;

                snapshot.Add(new QuickSlotSaveData
                {
                    itemId = slot.CurrentItemData.itemID,
                    instanceId = $"{slot.CurrentItemData.itemID}_quickslot_{slot.SlotIndex}",
                    slotIndex = Mathf.Clamp(slot.SlotIndex, 0, 5),
                    stackCount = Mathf.Max(1, slot.CurrentStackCount),
                    currentDurability = 0f,
                    currentAmmo = 0
                });
            }

            return snapshot;
        }

        private static int ApplyQuickSlotsToUi(IReadOnlyList<QuickSlotSaveData> quickSlotItems)
        {
            InventorySlotUI[] quickSlots = ResolveQuickSlotSlots();
            if (quickSlots.Length == 0)
                return 0;

            for (int i = 0; i < quickSlots.Length; i++)
                quickSlots[i]?.ClearItem();

            if (quickSlotItems == null || quickSlotItems.Count == 0)
                return 0;

            IItemDatabase itemDatabase = ResolveItemDatabase();
            if (itemDatabase == null)
            {
                Debug.LogWarning("[RaidLoadout] Cannot apply quickslots. ItemDatabase was not found.");
                return 0;
            }

            int applied = 0;

            for (int i = 0; i < quickSlotItems.Count; i++)
            {
                QuickSlotSaveData savedItem = quickSlotItems[i];
                if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.itemId))
                    continue;

                int slotIndex = Mathf.Clamp(savedItem.slotIndex, 0, 5);
                InventorySlotUI slot = FindQuickSlotByIndex(quickSlots, slotIndex);
                ItemDataSO itemData = itemDatabase.GetById(savedItem.itemId);

                if (slot == null || itemData == null)
                    continue;

                slot.SetItem(itemData, Mathf.Max(1, savedItem.stackCount));
                applied++;
            }

            return applied;
        }

        private static InventorySlotUI[] ResolveQuickSlotSlots()
        {
            Transform quickSlotPanel = FindQuickSlotPanel();
            if (quickSlotPanel == null)
                return System.Array.Empty<InventorySlotUI>();

            ItemTooltipUI tooltip = Object.FindFirstObjectByType<ItemTooltipUI>(FindObjectsInactive.Include);
            List<InventorySlotUI> slots = new();
            int index = 0;

            foreach (Transform child in quickSlotPanel)
            {
                if (child == null || child.GetComponent<RectTransform>() == null)
                    continue;

                InventorySlotUI slot = child.GetComponent<InventorySlotUI>();
                if (slot == null && child.GetComponent<UnityEngine.UI.Image>() != null)
                    slot = child.gameObject.AddComponent<InventorySlotUI>();

                if (slot == null)
                    continue;

                slot.PrepareDropSlotAsKind(tooltip, InventorySlotKind.QuickSlot, index);
                slots.Add(slot);
                index++;
            }

            foreach (InventorySlotUI slot in quickSlotPanel.GetComponentsInChildren<InventorySlotUI>(true))
            {
                if (slot == null || slots.Contains(slot))
                    continue;

                slot.PrepareDropSlotAsKind(tooltip, InventorySlotKind.QuickSlot);
                slots.Add(slot);
            }

            slots.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            return slots.ToArray();
        }

        private static Transform FindQuickSlotPanel()
        {
            Transform fallback = null;
            Transform[] transforms = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || candidate.name != "QuickSlotPanel")
                    continue;

                if (candidate.gameObject.activeInHierarchy)
                    return candidate;

                fallback ??= candidate;
            }

            return fallback;
        }

        private static InventorySlotUI FindQuickSlotByIndex(IReadOnlyList<InventorySlotUI> slots, int slotIndex)
        {
            if (slots == null)
                return null;

            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot != null && slot.SlotIndex == slotIndex)
                    return slot;
            }

            return slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex] : null;
        }

        private static IItemDatabase ResolveItemDatabase()
        {
            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                itemDatabase = Object.FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);

            return itemDatabase;
        }

        private static List<ItemSaveDTO> CreateAmmoLookupItems(IReadOnlyList<ItemSaveDTO> inventoryItems)
        {
            List<ItemSaveDTO> items = new();
            AddRange(items, inventoryItems);
            return items;
        }

        private static List<ItemSaveDTO> CreateValidQuickSlotItems(
            IReadOnlyList<ItemSaveDTO> quickSlotItems,
            IReadOnlyList<ItemSaveDTO> inventoryItems)
        {
            List<ItemSaveDTO> result = new();
            if (quickSlotItems == null || quickSlotItems.Count == 0)
                return result;

            Dictionary<string, int> availableCounts = new(System.StringComparer.OrdinalIgnoreCase);
            if (inventoryItems != null)
            {
                for (int i = 0; i < inventoryItems.Count; i++)
                {
                    ItemSaveDTO inventoryItem = inventoryItems[i];
                    if (inventoryItem == null || string.IsNullOrWhiteSpace(inventoryItem.itemId))
                        continue;

                    int stackCount = Mathf.Max(1, inventoryItem.stackCount);
                    if (availableCounts.TryGetValue(inventoryItem.itemId, out int currentCount))
                        availableCounts[inventoryItem.itemId] = currentCount + stackCount;
                    else
                        availableCounts.Add(inventoryItem.itemId, stackCount);
                }
            }

            HashSet<string> assignedItemIds = new(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < quickSlotItems.Count; i++)
            {
                ItemSaveDTO quickSlotItem = quickSlotItems[i];
                if (quickSlotItem == null ||
                    string.IsNullOrWhiteSpace(quickSlotItem.itemId) ||
                    !availableCounts.TryGetValue(quickSlotItem.itemId, out int availableCount) ||
                    availableCount <= 0 ||
                    !assignedItemIds.Add(quickSlotItem.itemId))
                {
                    continue;
                }

                result.Add(new ItemSaveDTO
                {
                    itemId = quickSlotItem.itemId,
                    instanceId = quickSlotItem.instanceId,
                    containerId = quickSlotItem.containerId,
                    x = quickSlotItem.x,
                    y = quickSlotItem.y,
                    rotated = quickSlotItem.rotated,
                    stackCount = Mathf.Clamp(quickSlotItem.stackCount, 1, availableCount),
                    currentDurability = Mathf.Max(0f, quickSlotItem.currentDurability),
                    currentAmmo = Mathf.Max(0, quickSlotItem.currentAmmo)
                });
            }

            return result;
        }

        private static int ResolveGridX(ItemSaveDTO item)
        {
            if (item == null)
                return 0;

            if (item.y == 0 && item.x >= GridInventory.BASE_WIDTH)
                return item.x % GridInventory.BASE_WIDTH;

            return Mathf.Max(0, item.x);
        }

        private static int ResolveGridY(ItemSaveDTO item)
        {
            if (item == null)
                return 0;

            if (item.y == 0 && item.x >= GridInventory.BASE_WIDTH)
                return item.x / GridInventory.BASE_WIDTH;

            return Mathf.Max(0, item.y);
        }

        private static string ResolveLoadedAmmoId(string weaponId, IReadOnlyList<ItemSaveDTO> inventoryItems)
        {
            if (string.IsNullOrWhiteSpace(weaponId) || inventoryItems == null)
                return string.Empty;

            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                itemDatabase = Object.FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);

            WeaponDataSO weapon = itemDatabase?.GetById<WeaponDataSO>(weaponId);
            if (weapon == null)
                return string.Empty;

            for (int i = 0; i < inventoryItems.Count; i++)
            {
                ItemSaveDTO item = inventoryItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.stackCount <= 0)
                    continue;

                AmmoDataSO ammo = itemDatabase.GetById<AmmoDataSO>(item.itemId);
                if (ammo != null && ammo.caliber == weapon.ammoType)
                    return ammo.itemID;
            }

            string defaultAmmoId = ResolveDefaultAmmoId(weapon.ammoType);
            AmmoDataSO defaultAmmo = itemDatabase.GetById<AmmoDataSO>(defaultAmmoId);
            if (defaultAmmo != null && defaultAmmo.caliber == weapon.ammoType)
                return defaultAmmo.itemID;

            return string.Empty;
        }

        private static string ResolveDefaultAmmoId(AmmoType ammoType)
        {
            return ammoType switch
            {
                AmmoType.AR => "Ammo_AR_BP",
                AmmoType.SMG => "Ammo_SMG_BP",
                AmmoType.Handgun => "Ammo_Handgun_BP",
                AmmoType.Sniper => "Ammo_Sniper_BP",
                AmmoType.Shotgun => "Ammo_SG_BP",
                _ => string.Empty
            };
        }

        private static string ToRaidEquipmentSlotId(string lobbySlotId)
        {
            if (string.IsNullOrWhiteSpace(lobbySlotId))
                return string.Empty;

            return lobbySlotId.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant() switch
            {
                "equipmenthead" or "head" => "Head",
                "equipmentarmor" or "torso" => "Torso",
                "equipmentbackpack" or "backpack" => "Backpack",
                "equipmentprimaryweapon" or "primary1" => "Primary1",
                "primary2" => "Primary2",
                "equipmentsecondaryweapon" or "secondary" => "Secondary",
                "equipmentmeleeweapon" or "melee" => "Melee",
                _ => string.Empty
            };
        }

        private static bool IsEquippableWeaponSlot(string slotId)
        {
            return slotId == "Primary1" || slotId == "Primary2" || slotId == "Secondary" || slotId == "Melee";
        }

        private static void AddRange<T>(List<T> target, IReadOnlyList<T> source)
        {
            if (target == null || source == null)
                return;

            for (int i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }

        private static bool HasMeaningfulLobbyInventory(LobbySaveDTO dto)
        {
            return dto != null &&
                   (HasItems(dto.inventoryItems) ||
                    HasItems(dto.quickSlotItems) ||
                    HasItems(dto.equipmentItems));
        }

        private static bool HasMeaningfulLoadout(RaidLoadoutSaveData loadout)
        {
            return loadout != null &&
                   (HasItems(loadout.inventoryItems) ||
                    HasItems(loadout.quickSlotItems) ||
                    CountEquippedItems(loadout.equipmentItems) > 0);
        }

        private static bool TryResolveLoadoutSource(
            NetworkManager networkManager,
            ulong clientId,
            out GameObject source)
        {
            source = null;

            if (networkManager == null)
                return false;

            if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null &&
                HasLoadoutComponents(client.PlayerObject.gameObject))
            {
                source = client.PlayerObject.gameObject;
                return true;
            }

            foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null || networkObject.OwnerClientId != clientId)
                    continue;

                GameObject candidate = networkObject.gameObject;
                if (!HasLoadoutComponents(candidate))
                    continue;

                source = candidate;
                return true;
            }

            return false;
        }

        private static bool HasLoadoutComponents(GameObject candidate)
        {
            return candidate != null &&
                   candidate.GetComponent<GridInventory>() != null &&
                   candidate.GetComponent<EquipmentSlots>() != null;
        }

        private static void RemoveUnexpectedLoadouts(IReadOnlyList<ulong> expectedClientIds)
        {
            if (expectedClientIds == null)
                return;

            List<ulong> keysToRemove = null;

            foreach (ulong clientId in loadoutsByClientId.Keys)
            {
                if (ContainsClientId(expectedClientIds, clientId))
                    continue;

                keysToRemove ??= new List<ulong>();
                keysToRemove.Add(clientId);
            }

            if (keysToRemove == null)
                return;

            for (int i = 0; i < keysToRemove.Count; i++)
                loadoutsByClientId.Remove(keysToRemove[i]);
        }

        private static bool ContainsClientId(IReadOnlyList<ulong> clientIds, ulong clientId)
        {
            if (clientIds == null)
                return false;

            for (int i = 0; i < clientIds.Count; i++)
            {
                if (clientIds[i] == clientId)
                    return true;
            }

            return false;
        }

        private static bool TryGetServerNetworkManager(out NetworkManager networkManager)
        {
            networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            {
                Debug.LogWarning("[RaidLoadout] Server NetworkManager is not ready.");
                return false;
            }

            return true;
        }

        private static int CountEquippedItems(IReadOnlyList<EquipmentSaveData> equipmentItems)
        {
            if (equipmentItems == null)
                return 0;

            int count = 0;

            for (int i = 0; i < equipmentItems.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(equipmentItems[i]?.itemId))
                    count++;
            }

            return count;
        }

        private static bool HasItems<T>(IReadOnlyList<T> items)
        {
            return items != null && items.Count > 0;
        }
    }
}

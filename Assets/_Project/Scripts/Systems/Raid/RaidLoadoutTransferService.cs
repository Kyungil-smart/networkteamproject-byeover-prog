using System.Collections.Generic;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Save;

using Unity.Netcode;
using UnityEngine;

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
            loadoutsByClientId.Clear();

            if (!TryGetServerNetworkManager(out NetworkManager networkManager))
                return;

            if (clientIds == null || clientIds.Count == 0)
            {
                Debug.LogWarning("[RaidLoadout] No clientIds supplied for loadout snapshot.");
                return;
            }

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];
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
                    $"[RaidLoadout] Saved loadout clientId={clientId}, inventory={liveLoadout.inventoryItems.Count}, equipment={CountEquippedItems(liveLoadout.equipmentItems)}",
                    playerObject);
            }
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

            Debug.Log(
                $"[RaidLoadout] Applied inventory items={inventoryCount}, equipment={equipmentCount}, clientId={clientId}",
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

            if (!HasMeaningfulLobbyInventory(dto))
                dto = CreateLobbySaveDTOFromCloud();

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
                credits = inventoryState.Credits
            };

            AddRange(dto.inventoryItems, inventoryState.InventoryItems);
            AddRange(dto.stashItems, inventoryState.StashItems);
            AddRange(dto.quickSlotItems, inventoryState.QuickSlotItems);
            AddRange(dto.equipmentItems, inventoryState.EquipmentItems);

            return dto;
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

            if (dto.quickSlotItems != null)
            {
                for (int i = 0; i < dto.quickSlotItems.Count; i++)
                {
                    ItemSaveDTO item = dto.quickSlotItems[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                        continue;

                    loadout.quickSlotItems.Add(new QuickSlotSaveData
                    {
                        itemId = item.itemId,
                        instanceId = item.instanceId,
                        slotIndex = Mathf.Max(0, item.x),
                        stackCount = Mathf.Max(1, item.stackCount),
                        currentDurability = Mathf.Max(0f, item.currentDurability),
                        currentAmmo = Mathf.Max(0, item.currentAmmo)
                    });
                }

                AddQuickSlotItemsToInventory(loadout.inventoryItems, dto.quickSlotItems);
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

        private static List<ItemSaveDTO> CreateAmmoLookupItems(IReadOnlyList<ItemSaveDTO> inventoryItems)
        {
            List<ItemSaveDTO> items = new();
            AddRange(items, inventoryItems);
            return items;
        }

        private static void AddQuickSlotItemsToInventory(
            List<InventoryItemSaveData> inventoryItems,
            IReadOnlyList<ItemSaveDTO> quickSlotItems)
        {
            if (inventoryItems == null || quickSlotItems == null || quickSlotItems.Count == 0)
                return;

            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                itemDatabase = Object.FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);

            bool[,] occupied = BuildOccupiedGrid(inventoryItems, itemDatabase);

            for (int i = 0; i < quickSlotItems.Count; i++)
            {
                ItemSaveDTO quickSlotItem = quickSlotItems[i];
                if (quickSlotItem == null || string.IsNullOrWhiteSpace(quickSlotItem.itemId))
                    continue;

                ItemDataSO itemData = itemDatabase?.GetById(quickSlotItem.itemId);
                Vector2Int itemSize = itemData != null ? itemData.gridSize : Vector2Int.one;

                if (!TryFindEmptyInventoryCell(occupied, itemSize, out int gridX, out int gridY))
                {
                    Debug.LogWarning($"[RaidLoadout] QuickSlot item could not be added to inventory snapshot. No empty grid cell. itemId={quickSlotItem.itemId}");
                    continue;
                }

                MarkOccupied(occupied, gridX, gridY, itemSize);
                inventoryItems.Add(new InventoryItemSaveData
                {
                    itemId = quickSlotItem.itemId,
                    instanceId = string.IsNullOrWhiteSpace(quickSlotItem.instanceId)
                        ? $"quickslot_{quickSlotItem.x}_{quickSlotItem.itemId}"
                        : quickSlotItem.instanceId,
                    gridX = gridX,
                    gridY = gridY,
                    rotated = false,
                    stackCount = Mathf.Max(1, quickSlotItem.stackCount),
                    currentDurability = Mathf.Max(0f, quickSlotItem.currentDurability),
                    currentAmmo = Mathf.Max(0, quickSlotItem.currentAmmo)
                });
            }
        }

        private static bool[,] BuildOccupiedGrid(
            IReadOnlyList<InventoryItemSaveData> inventoryItems,
            IItemDatabase itemDatabase)
        {
            bool[,] occupied = new bool[GridInventory.BASE_WIDTH, 10];

            for (int i = 0; i < inventoryItems.Count; i++)
            {
                InventoryItemSaveData item = inventoryItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                    continue;

                ItemDataSO itemData = itemDatabase?.GetById(item.itemId);
                Vector2Int size = itemData != null ? itemData.gridSize : Vector2Int.one;
                MarkOccupied(occupied, Mathf.Max(0, item.gridX), Mathf.Max(0, item.gridY), size);
            }

            return occupied;
        }

        private static bool TryFindEmptyInventoryCell(
            bool[,] occupied,
            Vector2Int itemSize,
            out int gridX,
            out int gridY)
        {
            gridX = 0;
            gridY = 0;

            int width = Mathf.Max(1, itemSize.x);
            int height = Mathf.Max(1, itemSize.y);

            for (int y = 0; y <= occupied.GetLength(1) - height; y++)
            {
                for (int x = 0; x <= occupied.GetLength(0) - width; x++)
                {
                    if (!CanPlaceInGrid(occupied, x, y, width, height))
                        continue;

                    gridX = x;
                    gridY = y;
                    return true;
                }
            }

            return false;
        }

        private static bool CanPlaceInGrid(bool[,] occupied, int gridX, int gridY, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int checkX = gridX + x;
                    int checkY = gridY + y;

                    if (checkX < 0 || checkX >= occupied.GetLength(0) ||
                        checkY < 0 || checkY >= occupied.GetLength(1) ||
                        occupied[checkX, checkY])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void MarkOccupied(bool[,] occupied, int gridX, int gridY, Vector2Int itemSize)
        {
            int width = Mathf.Max(1, itemSize.x);
            int height = Mathf.Max(1, itemSize.y);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int checkX = gridX + x;
                    int checkY = gridY + y;

                    if (checkX >= 0 && checkX < occupied.GetLength(0) &&
                        checkY >= 0 && checkY < occupied.GetLength(1))
                    {
                        occupied[checkX, checkY] = true;
                    }
                }
            }
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

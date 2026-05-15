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
        private const string LobbyInventoryContainerId = "inventory";
        private const string LobbyQuickSlotContainerId = "quickslot";
        private const string CashItemId = "USDollars";
        private const int FallbackCashItemCreditValue = 100;

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

        public static void SaveCurrentRaidLoadoutsForConnectedClients()
        {
            if (!TryGetServerNetworkManager(out NetworkManager networkManager))
                return;

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (!TryResolveLoadoutSource(networkManager, clientId, out GameObject playerObject))
                    continue;

                RaidLoadoutSaveData liveLoadout = CreateLoadout(clientId, playerObject);
                if (liveLoadout == null)
                    continue;

                loadoutsByClientId[clientId] = liveLoadout;

                Debug.Log(
                    $"[RaidLoadout] Saved current raid loadout clientId={clientId}, inventory={liveLoadout.inventoryItems.Count}, quickSlots={liveLoadout.quickSlotItems.Count}, equipment={CountEquippedItems(liveLoadout.equipmentItems)}",
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
            int quickSlotCount = inventory.ImportQuickSlotSnapshot(loadout.quickSlotItems);

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

        public static bool TryCreateLocalRaidReturnLobbySaveDTO(out LobbySaveDTO dto)
        {
            dto = CreateLobbySaveDTOFromCloud() ?? new LobbySaveDTO();

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogWarning("[RaidLoadout] Cannot create extraction save. NetworkManager is missing.");
                return false;
            }

            if (!TryResolveLocalPlayerObject(networkManager, out GameObject playerObject))
            {
                Debug.LogWarning($"[RaidLoadout] Cannot create extraction save. Local player was not found clientId={networkManager.LocalClientId}.");
                return false;
            }

            CaptureLocalQuickSlotsFromUi();

            RaidLoadoutSaveData loadout = CreateLoadout(networkManager.LocalClientId, playerObject);
            if (loadout == null)
                return false;

            ApplyRaidLoadoutToLobbyDto(dto, loadout);
            loadoutsByClientId[networkManager.LocalClientId] = loadout;

            Debug.Log(
                $"[RaidLoadout] Created extraction lobby save clientId={networkManager.LocalClientId}, inventory={dto.inventoryItems.Count}, quickSlots={dto.quickSlotItems.Count}, equipment={dto.equipmentItems.Count}",
                playerObject);
            return true;
        }

        public static bool TryCreateAbandonedRaidLobbySaveDTO(out LobbySaveDTO dto)
        {
            dto = CreateLobbySaveDTOFromCloud() ?? new LobbySaveDTO();

            dto.hasInventorySection = true;
            dto.hasEquipmentSection = true;
            dto.hasQuickSlotSection = true;
            dto.inventoryItems.Clear();
            dto.equipmentItems.Clear();
            dto.quickSlotItems.Clear();

            Debug.Log("[RaidLoadout] Created abandoned raid lobby save. Inventory/equipment/quickslots will be cleared.");
            return true;
        }

        public static void ClearLocalClientLoadout()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return;

            loadoutsByClientId.Remove(networkManager.LocalClientId);
        }

        private static RaidLoadoutSaveData CreateEmptyLoadout(ulong clientId)
        {
            return new RaidLoadoutSaveData
            {
                clientId = clientId,
                currentEquippedItemId = string.Empty
            };
        }

        private static bool TryResolveLocalPlayerObject(NetworkManager networkManager, out GameObject playerObject)
        {
            playerObject = null;

            if (networkManager == null || networkManager.SpawnManager == null)
                return false;

            ulong localClientId = networkManager.LocalClientId;

            foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null ||
                    !networkObject.IsPlayerObject ||
                    networkObject.OwnerClientId != localClientId ||
                    !HasLoadoutComponents(networkObject.gameObject))
                {
                    continue;
                }

                playerObject = networkObject.gameObject;
                return true;
            }

            return false;
        }

        private static void ApplyRaidLoadoutToLobbyDto(LobbySaveDTO dto, RaidLoadoutSaveData loadout)
        {
            dto.hasInventorySection = true;
            dto.hasEquipmentSection = true;
            dto.hasQuickSlotSection = true;

            dto.inventoryItems.Clear();
            dto.equipmentItems.Clear();
            dto.quickSlotItems.Clear();

            AddInventoryItemsToLobby(dto.inventoryItems, loadout.inventoryItems);
            AddEquipmentItemsToLobby(dto.equipmentItems, loadout.equipmentItems);
            ConvertCashItemsToCredits(dto);

            List<ItemSaveDTO> rawQuickSlotItems = CreateLobbyQuickSlotItems(loadout.quickSlotItems);
            List<ItemSaveDTO> validQuickSlotItems = CreateValidQuickSlotItems(rawQuickSlotItems);
            AddRange(dto.quickSlotItems, validQuickSlotItems);
        }

        private static void ConvertCashItemsToCredits(LobbySaveDTO dto)
        {
            if (dto == null || dto.inventoryItems == null)
                return;

            int cashCount = 0;
            for (int i = dto.inventoryItems.Count - 1; i >= 0; i--)
            {
                ItemSaveDTO item = dto.inventoryItems[i];
                if (item == null || !IsCashItem(item.itemId))
                    continue;

                cashCount += Mathf.Max(1, item.stackCount);
                dto.inventoryItems.RemoveAt(i);
            }

            if (cashCount <= 0)
                return;

            int creditValue = ResolveCashItemCreditValue();
            int creditAmount = cashCount * creditValue;
            dto.hasCredits = true;
            dto.credits = Mathf.Max(0, dto.credits) + creditAmount;

            Debug.Log($"[RaidLoadout] Converted cash items to credits. itemId={CashItemId}, count={cashCount}, credits={creditAmount}");
        }

        private static bool IsCashItem(string itemId)
        {
            return string.Equals(itemId, CashItemId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(itemId, $"ITM_{CashItemId}", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveCashItemCreditValue()
        {
            IItemDatabase itemDatabase = ResolveItemDatabase();
            ItemDataSO cashItem = itemDatabase?.GetById(CashItemId);
            return cashItem != null && cashItem.baseSellPrice > 0
                ? cashItem.baseSellPrice
                : FallbackCashItemCreditValue;
        }

        private static void AddInventoryItemsToLobby(
            List<ItemSaveDTO> target,
            IReadOnlyList<InventoryItemSaveData> source)
        {
            if (target == null || source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                InventoryItemSaveData item = source[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                    continue;

                target.Add(new ItemSaveDTO
                {
                    itemId = item.itemId,
                    instanceId = item.instanceId,
                    containerId = LobbyInventoryContainerId,
                    x = Mathf.Max(0, item.gridX),
                    y = Mathf.Max(0, item.gridY),
                    rotated = item.rotated,
                    stackCount = Mathf.Max(1, item.stackCount),
                    currentDurability = Mathf.Max(0f, item.currentDurability),
                    currentAmmo = Mathf.Max(0, item.currentAmmo)
                });
            }
        }

        private static void AddEquipmentItemsToLobby(
            List<EquipmentSaveDTO> target,
            IReadOnlyList<EquipmentSaveData> source)
        {
            if (target == null || source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                EquipmentSaveData equipment = source[i];
                if (equipment == null || string.IsNullOrWhiteSpace(equipment.itemId))
                    continue;

                string lobbySlotId = ToLobbyEquipmentSlotId(equipment.slotId);
                if (string.IsNullOrWhiteSpace(lobbySlotId))
                    continue;

                target.Add(new EquipmentSaveDTO
                {
                    slotId = lobbySlotId,
                    itemId = equipment.itemId,
                    instanceId = equipment.instanceId,
                    loadedAmmoId = equipment.loadedAmmoId,
                    currentAmmo = Mathf.Max(0, equipment.currentAmmo),
                    durability = Mathf.Max(0f, equipment.currentDurability)
                });
            }
        }

        private static List<ItemSaveDTO> CreateLobbyQuickSlotItems(IReadOnlyList<QuickSlotSaveData> source)
        {
            List<ItemSaveDTO> result = new();
            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                QuickSlotSaveData item = source[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId))
                    continue;

                int slotIndex = Mathf.Clamp(item.slotIndex, 0, 5);
                result.Add(new ItemSaveDTO
                {
                    itemId = item.itemId,
                    instanceId = item.instanceId,
                    containerId = LobbyQuickSlotContainerId,
                    x = slotIndex,
                    y = 0,
                    rotated = false,
                    stackCount = Mathf.Max(1, item.stackCount),
                    currentDurability = Mathf.Max(0f, item.currentDurability),
                    currentAmmo = Mathf.Max(0, item.currentAmmo)
                });
            }

            return result;
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
                quickSlotItems = inventory.ExportQuickSlotSnapshot(),
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
            List<ItemSaveDTO> quickSlotItems = CreateValidQuickSlotItems(dto.quickSlotItems);
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
            IReadOnlyList<ItemSaveDTO> quickSlotItems)
        {
            List<ItemSaveDTO> result = new();
            if (quickSlotItems == null || quickSlotItems.Count == 0)
                return result;

            HashSet<int> assignedSlotIndexes = new();
            for (int i = 0; i < quickSlotItems.Count; i++)
            {
                ItemSaveDTO quickSlotItem = quickSlotItems[i];
                int slotIndex = Mathf.Clamp(quickSlotItem?.x ?? -1, 0, 5);
                if (quickSlotItem == null ||
                    string.IsNullOrWhiteSpace(quickSlotItem.itemId) ||
                    !assignedSlotIndexes.Add(slotIndex))
                {
                    continue;
                }

                result.Add(new ItemSaveDTO
                {
                    itemId = quickSlotItem.itemId,
                    instanceId = quickSlotItem.instanceId,
                    containerId = quickSlotItem.containerId,
                    x = slotIndex,
                    y = quickSlotItem.y,
                    rotated = quickSlotItem.rotated,
                    stackCount = Mathf.Max(1, quickSlotItem.stackCount),
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

        private static string ToLobbyEquipmentSlotId(string raidSlotId)
        {
            if (string.IsNullOrWhiteSpace(raidSlotId))
                return string.Empty;

            return raidSlotId.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant() switch
            {
                "head" => "EquipmentHead",
                "torso" or "armor" => "EquipmentArmor",
                "backpack" => "EquipmentBackpack",
                "primary1" => "EquipmentPrimaryWeapon",
                "primary2" => "Primary2",
                "secondary" => "EquipmentSecondaryWeapon",
                "melee" => "EquipmentMeleeWeapon",
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

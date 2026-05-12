using System.Collections.Generic;

using DeadZone.Actors;

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

                if (!TryResolveLoadoutSource(networkManager, clientId, out GameObject playerObject))
                {
                    Debug.LogWarning($"[RaidLoadout] Cannot save loadout. Missing GridInventory/EquipmentSlots source clientId={clientId}");
                    continue;
                }

                SaveLoadout(clientId, playerObject);
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
                Debug.LogWarning($"[RaidLoadout] Missing loadout for clientId={clientId}");
                return false;
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

        private static void SaveLoadout(ulong clientId, GameObject playerObject)
        {
            GridInventory inventory = playerObject.GetComponent<GridInventory>();
            EquipmentSlots equipment = playerObject.GetComponent<EquipmentSlots>();

            if (inventory == null)
            {
                Debug.LogWarning($"[RaidLoadout] Cannot save inventory. Missing GridInventory clientId={clientId}", playerObject);
                return;
            }

            if (equipment == null)
            {
                Debug.LogWarning($"[RaidLoadout] Cannot save equipment. Missing EquipmentSlots clientId={clientId}", playerObject);
                return;
            }

            RaidLoadoutSaveData loadout = new()
            {
                clientId = clientId,
                inventoryItems = inventory.ExportSnapshot(),
                equipmentItems = equipment.ExportSnapshot(),
                currentEquippedItemId = equipment.CurrentEquipped.Value.ToString()
            };

            loadoutsByClientId[clientId] = loadout;

            Debug.Log(
                $"[RaidLoadout] Saved loadout clientId={clientId}, inventory={loadout.inventoryItems.Count}, equipment={CountEquippedItems(loadout.equipmentItems)}",
                playerObject);
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
    }
}

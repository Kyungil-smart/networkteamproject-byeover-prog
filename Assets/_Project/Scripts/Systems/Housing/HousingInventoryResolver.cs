using System;
using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    public static class HousingInventoryResolver
    {
        public static bool TryGetRequesterInventory(
            ulong requesterClientId,
            out IInventory inventory,
            out string failReason)
        {
            inventory = null;
            failReason = string.Empty;

            if (TryGetLobbyInventory(out inventory, out _))
                return true;

            if (NetworkManager.Singleton == null)
            {
                failReason = "NetworkManager가 없습니다.";
                return false;
            }

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requesterClientId, out NetworkClient client))
            {
                failReason = $"요청자 클라이언트를 찾지 못했습니다. ClientId: {requesterClientId}";
                return false;
            }

            if (client.PlayerObject == null)
            {
                failReason = $"요청자 PlayerObject가 없습니다. ClientId: {requesterClientId}";
                return false;
            }

            inventory = client.PlayerObject.GetComponent<IInventory>();
            if (inventory != null)
                return true;

            inventory = client.PlayerObject.GetComponentInChildren<IInventory>(true);
            if (inventory != null)
                return true;

            failReason = $"요청자 PlayerObject에서 IInventory를 찾지 못했습니다. PlayerObject: {client.PlayerObject.name}";
            return false;
        }

        public static bool TryGetLobbyInventory(out IInventory inventory, out MonoBehaviour inventoryBehaviour)
        {
            inventory = null;
            inventoryBehaviour = null;

            HousingLobbyInventoryBridge bridge = HousingLobbyInventoryBridge.GetOrCreate();
            if (bridge == null || !bridge.CanProvideInventoryData())
                return false;

            inventory = bridge;
            inventoryBehaviour = bridge;
            return true;
        }

        public static bool IsNetworkReady(out string failReason)
        {
            failReason = string.Empty;

            if (NetworkManager.Singleton == null)
            {
                failReason = "NetworkManager가 없습니다.";
                return false;
            }

            if (!NetworkManager.Singleton.IsListening)
            {
                failReason = "네트워크가 시작되지 않았습니다. Host 또는 Client 실행 후 요청해야 합니다.";
                return false;
            }

            return true;
        }

        public static bool TryGetLobbyFacilityLevel(FacilityType facilityType, out int level)
        {
            level = 1;
            string facilityId = NormalizeFacilityId(facilityType.ToString());

            LobbyFacilityState facilityState =
                UnityEngine.Object.FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState?.Facilities != null)
            {
                for (int i = 0; i < facilityState.Facilities.Count; i++)
                {
                    FacilitySaveDTO savedFacility = facilityState.Facilities[i];
                    if (savedFacility == null)
                        continue;

                    if (NormalizeFacilityId(savedFacility.facilityId) != facilityId)
                        continue;

                    level = Mathf.Max(1, savedFacility.level);
                    return true;
                }
            }

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                return false;

            PlayerHousingProgressDTO housingDto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();
            if (housingDto == null)
                return false;

            level = Mathf.Max(1, housingDto.GetLevel(facilityType));
            return true;
        }

        public static bool TrySetLobbyFacilityLevel(FacilityType facilityType, int level, out string failReason)
        {
            failReason = string.Empty;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null)
            {
                failReason = "CloudSaveSystem을 찾을 수 없습니다.";
                return false;
            }

            if (!cloudSaveSystem.HasLoadedData)
            {
                failReason = "Cloud Save 데이터가 아직 로드되지 않았습니다.";
                return false;
            }

            PlayerHousingProgressDTO housingDto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();
            if (housingDto == null)
            {
                failReason = "Cloud Save에서 하우징 진행도를 만들 수 없습니다.";
                return false;
            }

            int safeLevel = Mathf.Clamp(level, 1, 4);
            housingDto.SetLevel(facilityType, safeLevel);
            housingDto.Normalize();

            LobbyFacilityState facilityState =
                UnityEngine.Object.FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);
            if (facilityState != null)
                facilityState.SetFacilityLevel(facilityType.ToString(), safeLevel);

            LobbySaveService saveService =
                UnityEngine.Object.FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
            if (saveService != null && facilityState != null)
            {
                saveService.SaveCurrentStateToLocalJson("Housing facility level bridge sync");
                saveService.SaveLobbyDataToCloud();
            }

            _ = cloudSaveSystem.SaveHousingProgressAsync(housingDto);
            return true;
        }

        private static CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem != null)
                return cloudSaveSystem;

            return UnityEngine.Object.FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);
        }

        private static string NormalizeFacilityId(string facilityId)
        {
            return string.IsNullOrWhiteSpace(facilityId)
                ? string.Empty
                : facilityId.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }
    }

    [DisallowMultipleComponent]
    public sealed class HousingLobbyInventoryBridge : MonoBehaviour, IInventory
    {
        private const string StashContainerId = "stash";
        private const int DefaultStashSlotCapacity = 110;

        private static HousingLobbyInventoryBridge instance;

        public static HousingLobbyInventoryBridge GetOrCreate()
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<HousingLobbyInventoryBridge>(FindObjectsInactive.Include);
            if (instance != null)
                return instance;

            GameObject go = new GameObject(nameof(HousingLobbyInventoryBridge));
            instance = go.AddComponent<HousingLobbyInventoryBridge>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        public bool CanProvideInventoryData()
        {
            if (TryResolveInventoryState(out LobbyInventoryState inventoryState) && HasAnyLobbyInventory(inventoryState))
                return true;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            return cloudSaveSystem != null &&
                   cloudSaveSystem.HasLoadedData &&
                   cloudSaveSystem.CreateLobbySaveDTOFromCurrentData() != null;
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemID) || amount <= 0)
                return false;

            if (!TryGetWorkingDto(out LobbySaveDTO dto))
                return false;

            List<ItemSaveDTO> nextInventoryItems = CloneItems(dto.inventoryItems);
            List<ItemSaveDTO> nextStashItems = CloneItems(dto.stashItems);
            List<ItemSaveDTO> nextQuickSlotItems = CloneItems(dto.quickSlotItems);

            if (!AddItemToStash(nextStashItems, item, amount))
                return false;

            ApplyAndPersist(dto, nextInventoryItems, nextStashItems, nextQuickSlotItems);
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
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            if (!TryGetWorkingDto(out LobbySaveDTO dto))
                return false;

            List<ItemSaveDTO> nextInventoryItems = CloneItems(dto.inventoryItems);
            List<ItemSaveDTO> nextStashItems = CloneItems(dto.stashItems);
            List<ItemSaveDTO> nextQuickSlotItems = CloneItems(dto.quickSlotItems);

            int availableCount = GetItemCount(nextStashItems, itemId) + GetItemCount(nextInventoryItems, itemId);
            if (availableCount < count)
                return false;

            int remaining = count;
            remaining = RemoveItemCount(nextStashItems, itemId, remaining);
            remaining = RemoveItemCount(nextInventoryItems, itemId, remaining);

            if (remaining > 0)
                return false;

            ApplyAndPersist(dto, nextInventoryItems, nextStashItems, nextQuickSlotItems);
            return true;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            if (!TryGetWorkingDto(out LobbySaveDTO dto))
                return 0;

            return GetItemCount(dto.stashItems, itemId) + GetItemCount(dto.inventoryItems, itemId);
        }

        public bool TryCraftRecipeToStash(RecipeSO recipe, int resultCount, out string failReason)
        {
            failReason = string.Empty;

            if (recipe == null || recipe.result == null || string.IsNullOrWhiteSpace(recipe.result.itemID))
            {
                failReason = "제작 레시피 또는 결과 아이템 데이터가 비어 있습니다.";
                return false;
            }

            if (!TryGetWorkingDto(out LobbySaveDTO dto))
            {
                failReason = "로비 인벤토리 저장 데이터를 찾지 못했습니다.";
                return false;
            }

            List<ItemSaveDTO> nextInventoryItems = CloneItems(dto.inventoryItems);
            List<ItemSaveDTO> nextStashItems = CloneItems(dto.stashItems);
            List<ItemSaveDTO> nextQuickSlotItems = CloneItems(dto.quickSlotItems);

            if (!HasAllCraftIngredients(nextInventoryItems, nextStashItems, recipe, out failReason))
                return false;

            if (!ConsumeCraftIngredients(nextInventoryItems, nextStashItems, recipe))
            {
                failReason = "제작 재료 차감에 실패했습니다.";
                return false;
            }

            if (!AddItemToStash(nextStashItems, recipe.result, Mathf.Max(1, resultCount)))
            {
                failReason = "제작 결과를 받을 보관함 공간이 부족합니다.";
                return false;
            }

            ApplyAndPersist(dto, nextInventoryItems, nextStashItems, nextQuickSlotItems);
            return true;
        }

        private static bool TryGetWorkingDto(out LobbySaveDTO dto)
        {
            dto = null;

            if (TryResolveInventoryState(out LobbyInventoryState inventoryState) && HasAnyLobbyInventory(inventoryState))
            {
                dto = CreateDtoFromState(inventoryState);
                return true;
            }

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                return false;

            dto = cloudSaveSystem.CreateLobbySaveDTOFromCurrentData();
            return dto != null;
        }

        private static void ApplyAndPersist(
            LobbySaveDTO sourceDto,
            List<ItemSaveDTO> inventoryItems,
            List<ItemSaveDTO> stashItems,
            List<ItemSaveDTO> quickSlotItems)
        {
            LobbySaveDTO nextDto = CloneDto(sourceDto);
            nextDto.hasInventorySection = true;
            nextDto.hasStashSection = true;
            nextDto.hasQuickSlotSection = true;
            nextDto.inventoryItems = inventoryItems ?? new List<ItemSaveDTO>();
            nextDto.stashItems = stashItems ?? new List<ItemSaveDTO>();
            nextDto.quickSlotItems = quickSlotItems ?? new List<ItemSaveDTO>();

            bool hasInventoryState = TryResolveInventoryState(out LobbyInventoryState inventoryState);
            if (hasInventoryState)
            {
                inventoryState.SetInventoryItems(nextDto.inventoryItems);
                inventoryState.SetStashItems(nextDto.stashItems);
                inventoryState.SetQuickSlotItems(nextDto.quickSlotItems);
            }

            LobbyInventoryStateUiBridge uiBridge =
                FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);
            if (uiBridge != null)
                uiBridge.ApplyStateToUi();

            LobbySaveService saveService =
                FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
            if (saveService != null && hasInventoryState)
            {
                saveService.SaveCurrentStateToLocalJson("Housing lobby inventory bridge sync");
                saveService.SaveLobbyDataToCloud();
                return;
            }

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                return;

            LobbySaveDTO cloudDto = cloudSaveSystem.CreateLobbySaveDTOFromCurrentData();
            if (cloudDto == null)
                return;

            cloudDto.hasInventorySection = true;
            cloudDto.hasStashSection = true;
            cloudDto.hasQuickSlotSection = true;
            cloudDto.inventoryItems = CloneItems(nextDto.inventoryItems);
            cloudDto.stashItems = CloneItems(nextDto.stashItems);
            cloudDto.quickSlotItems = CloneItems(nextDto.quickSlotItems);
            _ = cloudSaveSystem.SaveLobbyDataAsync(cloudDto);
        }

        private static bool TryResolveInventoryState(out LobbyInventoryState inventoryState)
        {
            inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);
            return inventoryState != null;
        }

        private static CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem != null)
                return cloudSaveSystem;

            return FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);
        }

        private static LobbySaveDTO CreateDtoFromState(LobbyInventoryState inventoryState)
        {
            return new LobbySaveDTO
            {
                hasCredits = inventoryState.HasCredits,
                credits = inventoryState.Credits,
                hasInventorySection = true,
                hasStashSection = true,
                hasEquipmentSection = true,
                hasQuickSlotSection = true,
                inventoryItems = CloneItems(inventoryState.InventoryItems),
                stashItems = CloneItems(inventoryState.StashItems),
                quickSlotItems = CloneItems(inventoryState.QuickSlotItems),
                equipmentItems = CloneEquipmentItems(inventoryState.EquipmentItems)
            };
        }

        private static LobbySaveDTO CloneDto(LobbySaveDTO source)
        {
            if (source == null)
                return new LobbySaveDTO();

            return new LobbySaveDTO
            {
                hasCredits = source.hasCredits,
                credits = source.credits,
                hasInventorySection = source.hasInventorySection,
                hasStashSection = source.hasStashSection,
                hasEquipmentSection = source.hasEquipmentSection,
                hasQuickSlotSection = source.hasQuickSlotSection,
                inventoryItems = CloneItems(source.inventoryItems),
                stashItems = CloneItems(source.stashItems),
                quickSlotItems = CloneItems(source.quickSlotItems),
                equipmentItems = CloneEquipmentItems(source.equipmentItems),
                facilities = CloneFacilities(source.facilities)
            };
        }

        private static bool HasAnyLobbyInventory(LobbyInventoryState inventoryState)
        {
            if (inventoryState == null)
                return false;

            return HasAny(inventoryState.InventoryItems) ||
                   HasAny(inventoryState.StashItems) ||
                   HasAny(inventoryState.QuickSlotItems) ||
                   HasAny(inventoryState.EquipmentItems);
        }

        private static bool HasAny<T>(IReadOnlyCollection<T> items)
        {
            return items != null && items.Count > 0;
        }

        private static List<ItemSaveDTO> CloneItems(IReadOnlyList<ItemSaveDTO> source)
        {
            List<ItemSaveDTO> result = new List<ItemSaveDTO>();

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
                    containerId = item.containerId,
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

        private static List<EquipmentSaveDTO> CloneEquipmentItems(IReadOnlyList<EquipmentSaveDTO> source)
        {
            List<EquipmentSaveDTO> result = new List<EquipmentSaveDTO>();

            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                EquipmentSaveDTO item = source[i];
                if (item == null)
                    continue;

                result.Add(new EquipmentSaveDTO
                {
                    slotId = item.slotId,
                    itemId = item.itemId,
                    instanceId = item.instanceId,
                    loadedAmmoId = item.loadedAmmoId,
                    currentAmmo = Mathf.Max(0, item.currentAmmo),
                    durability = Mathf.Max(0f, item.durability)
                });
            }

            return result;
        }

        private static List<FacilitySaveDTO> CloneFacilities(IReadOnlyList<FacilitySaveDTO> source)
        {
            List<FacilitySaveDTO> result = new List<FacilitySaveDTO>();

            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                FacilitySaveDTO facility = source[i];
                if (facility == null)
                    continue;

                result.Add(new FacilitySaveDTO
                {
                    facilityId = facility.facilityId,
                    level = Mathf.Max(1, facility.level)
                });
            }

            return result;
        }

        private static int GetItemCount(IReadOnlyList<ItemSaveDTO> items, string itemId)
        {
            int count = 0;

            if (items == null)
                return count;

            for (int i = 0; i < items.Count; i++)
            {
                ItemSaveDTO item = items[i];
                if (item != null && string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    count += Mathf.Max(0, item.stackCount);
            }

            return count;
        }

        private static bool HasAllCraftIngredients(
            IReadOnlyList<ItemSaveDTO> inventoryItems,
            IReadOnlyList<ItemSaveDTO> stashItems,
            RecipeSO recipe,
            out string failReason)
        {
            failReason = string.Empty;

            if (recipe == null)
            {
                failReason = "제작 레시피가 비어 있습니다.";
                return false;
            }

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];
                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                {
                    failReason = "제작 재료 아이템 데이터가 비어 있습니다.";
                    return false;
                }

                string itemId = ingredient.item.itemID;
                int requiredAmount = Mathf.Max(1, ingredient.amount);
                int availableAmount = GetItemCount(stashItems, itemId) + GetItemCount(inventoryItems, itemId);

                if (availableAmount < requiredAmount)
                {
                    failReason = $"제작 재료가 부족합니다. itemId={itemId}, required={requiredAmount}, current={availableAmount}";
                    return false;
                }
            }

            return true;
        }

        private static bool ConsumeCraftIngredients(
            List<ItemSaveDTO> inventoryItems,
            List<ItemSaveDTO> stashItems,
            RecipeSO recipe)
        {
            if (recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];
                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                    return false;

                int remaining = Mathf.Max(1, ingredient.amount);
                remaining = RemoveItemCount(stashItems, ingredient.item.itemID, remaining);
                remaining = RemoveItemCount(inventoryItems, ingredient.item.itemID, remaining);

                if (remaining > 0)
                    return false;
            }

            return true;
        }

        private static int RemoveItemCount(List<ItemSaveDTO> items, string itemId, int amount)
        {
            int remaining = Mathf.Max(0, amount);

            if (items == null || remaining <= 0)
                return remaining;

            for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemSaveDTO item = items[i];
                if (item == null || !string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    continue;

                int consumeCount = Mathf.Min(Mathf.Max(0, item.stackCount), remaining);
                item.stackCount -= consumeCount;
                remaining -= consumeCount;

                if (item.stackCount <= 0)
                    items.RemoveAt(i);
            }

            return remaining;
        }

        private static bool AddItemToStash(List<ItemSaveDTO> stashItems, ItemDataSO item, int amount)
        {
            if (stashItems == null || item == null || amount <= 0)
                return false;

            int remaining = Mathf.Max(1, amount);
            int maxStack = Mathf.Max(1, item.maxStackSize);

            for (int i = 0; i < stashItems.Count && remaining > 0; i++)
            {
                ItemSaveDTO stashItem = stashItems[i];
                if (stashItem == null || !string.Equals(stashItem.itemId, item.itemID, StringComparison.OrdinalIgnoreCase))
                    continue;

                int available = maxStack - Mathf.Max(0, stashItem.stackCount);
                if (available <= 0)
                    continue;

                int addCount = Mathf.Min(available, remaining);
                stashItem.stackCount += addCount;
                remaining -= addCount;
            }

            while (remaining > 0)
            {
                int slotIndex = FindFirstFreeSlotIndex(stashItems);
                if (slotIndex < 0)
                    return false;

                int stackCount = Mathf.Min(maxStack, remaining);
                stashItems.Add(CreateSaveItem(item, slotIndex, stackCount));
                remaining -= stackCount;
            }

            return true;
        }

        private static int FindFirstFreeSlotIndex(List<ItemSaveDTO> stashItems)
        {
            for (int slotIndex = 0; slotIndex < DefaultStashSlotCapacity; slotIndex++)
            {
                bool occupied = false;

                if (stashItems != null)
                {
                    for (int i = 0; i < stashItems.Count; i++)
                    {
                        ItemSaveDTO item = stashItems[i];
                        if (item != null && ToLinearSlotIndex(item.x, item.y) == slotIndex)
                        {
                            occupied = true;
                            break;
                        }
                    }
                }

                if (!occupied)
                    return slotIndex;
            }

            return -1;
        }

        private static int ToLinearSlotIndex(int x, int y)
        {
            return y <= 0 && x >= 10
                ? x
                : Mathf.Max(0, y) * 10 + Mathf.Max(0, x);
        }

        private static ItemSaveDTO CreateSaveItem(ItemDataSO item, int slotIndex, int stackCount)
        {
            return new ItemSaveDTO
            {
                itemId = item.itemID,
                instanceId = $"{StashContainerId}_{slotIndex}_{item.itemID}",
                containerId = StashContainerId,
                x = slotIndex,
                y = 0,
                rotated = false,
                stackCount = Mathf.Max(1, stackCount),
                currentDurability = GetDefaultDurability(item),
                currentAmmo = item is WeaponDataSO weapon ? Mathf.Max(0, weapon.magSize) : 0
            };
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;

using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    // 에디터/개발 빌드 전용 보조 클래스입니다.
    // 네트워크 플레이어가 없는 하이드아웃 테스트에서 제작이 성공했을 때,
    // 메모리 디버그 인벤토리뿐 아니라 로비 보관함 저장 데이터에도 같은 제작 결과를 반영합니다.
    internal static class HousingOfflineCraftSaveSync
    {
        private const int DefaultStashSlotCapacity = 110;

        // 저장용 보관함 스냅샷에서 재료를 차감하고 제작 결과 아이템을 추가합니다.
        // 이 처리가 성공해야 로비로 돌아왔을 때 제작 결과가 실제 보관함에 보입니다.
        public static bool TryApplyCraftToLobbyStash(RecipeSO recipe, int resultCount, out string failReason)
        {
            failReason = string.Empty;

            if (recipe == null || recipe.result == null)
            {
                failReason = "오프라인 제작 저장 반영 실패: 레시피 또는 결과 아이템이 없습니다.";
                return false;
            }

            LobbyInventoryState inventoryState = Object.FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);
            if (inventoryState == null)
            {
                failReason = "오프라인 제작 저장 반영 실패: LobbyInventoryState를 찾지 못했습니다.";
                return false;
            }

            // 현재 상태를 직접 수정하지 않고 복사본에서 먼저 검증한 뒤, 성공 시 한 번에 교체합니다.
            List<ItemSaveDTO> nextStashItems = CloneItems(inventoryState.StashItems);

            if (!ConsumeIngredients(nextStashItems, recipe))
            {
                failReason = "오프라인 제작 저장 반영 실패: 저장용 보관함 재료가 부족합니다.";
                return false;
            }

            if (!AddResultItem(nextStashItems, recipe.result, Mathf.Max(1, resultCount)))
            {
                failReason = "오프라인 제작 저장 반영 실패: 저장용 보관함 공간이 부족합니다.";
                return false;
            }

            inventoryState.SetStashItems(nextStashItems);
            SaveLobbyState("Offline housing craft save sync");
            return true;
        }

        private static List<ItemSaveDTO> CloneItems(IReadOnlyList<ItemSaveDTO> source)
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
                    containerId = string.IsNullOrWhiteSpace(item.containerId) ? "stash" : item.containerId,
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

        private static bool ConsumeIngredients(List<ItemSaveDTO> stashItems, RecipeSO recipe)
        {
            if (stashItems == null || recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            // 먼저 전체 재료가 충분한지 검사합니다. 중간 차감 후 실패하는 상태를 막기 위한 선검증입니다.
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];
                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                    return false;

                if (GetItemCount(stashItems, ingredient.item.itemID) < Mathf.Max(1, ingredient.amount))
                    return false;
            }

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];
                RemoveItemCount(stashItems, ingredient.item.itemID, Mathf.Max(1, ingredient.amount));
            }

            return true;
        }

        private static bool AddResultItem(List<ItemSaveDTO> stashItems, ItemDataSO item, int amount)
        {
            if (stashItems == null || item == null || string.IsNullOrWhiteSpace(item.itemID) || amount <= 0)
                return false;

            int remaining = amount;
            int maxStack = Mathf.Max(1, item.maxStackSize);

            // 같은 아이템 스택에 먼저 합친 뒤, 남은 수량만 새 보관함 슬롯에 배치합니다.
            for (int i = 0; i < stashItems.Count && remaining > 0; i++)
            {
                ItemSaveDTO stashItem = stashItems[i];
                if (stashItem == null)
                    continue;

                if (!string.Equals(stashItem.itemId, item.itemID, System.StringComparison.OrdinalIgnoreCase))
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

        private static ItemSaveDTO CreateSaveItem(ItemDataSO item, int slotIndex, int stackCount)
        {
            // 제작 결과가 무기/방어구일 경우 기본 내구도와 장탄 수를 채워 저장 데이터로 만듭니다.
            return new ItemSaveDTO
            {
                itemId = item.itemID,
                instanceId = $"stash_{slotIndex}_{item.itemID}",
                containerId = "stash",
                x = slotIndex,
                y = 0,
                rotated = false,
                stackCount = Mathf.Max(1, stackCount),
                currentDurability = GetDefaultDurability(item),
                currentAmmo = item is WeaponDataSO weapon ? Mathf.Max(0, weapon.magSize) : 0
            };
        }

        private static int GetItemCount(List<ItemSaveDTO> stashItems, string itemId)
        {
            int count = 0;

            for (int i = 0; i < stashItems.Count; i++)
            {
                ItemSaveDTO item = stashItems[i];
                if (item != null && string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    count += Mathf.Max(0, item.stackCount);
            }

            return count;
        }

        private static void RemoveItemCount(List<ItemSaveDTO> stashItems, string itemId, int amount)
        {
            int remaining = Mathf.Max(0, amount);

            for (int i = stashItems.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemSaveDTO item = stashItems[i];
                if (item == null || !string.Equals(item.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int consumeCount = Mathf.Min(Mathf.Max(0, item.stackCount), remaining);
                item.stackCount -= consumeCount;
                remaining -= consumeCount;

                if (item.stackCount <= 0)
                    stashItems.RemoveAt(i);
            }
        }

        private static int FindFirstFreeSlotIndex(List<ItemSaveDTO> stashItems)
        {
            // StashGridUI가 사용하는 0 기반 선형 슬롯 위치와 맞춰 빈 칸을 찾습니다.
            for (int slotIndex = 0; slotIndex < DefaultStashSlotCapacity; slotIndex++)
            {
                bool occupied = false;

                for (int i = 0; i < stashItems.Count; i++)
                {
                    ItemSaveDTO item = stashItems[i];
                    if (item != null && item.x == slotIndex && item.y == 0)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                    return slotIndex;
            }

            return -1;
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

        private static void SaveLobbyState(string reason)
        {
            LobbySaveService saveService = Object.FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
            if (saveService == null)
            {
                Debug.LogWarning("[HousingOfflineCraftSaveSync] LobbySaveService를 찾지 못해 오프라인 제작 결과를 즉시 저장하지 못했습니다.");
                return;
            }

            // 하이드아웃에서 만든 결과가 로비 재진입 후에도 남도록 로컬 JSON과 Cloud Save를 함께 갱신합니다.
            saveService.SaveCurrentStateToLocalJson(reason);
            saveService.SaveLobbyDataToCloud();
        }
    }
}
#endif

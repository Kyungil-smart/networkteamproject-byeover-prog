using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 의료시설 제작을 서버에서 처리
    // 시설 오브젝트의 공용 레벨이 아니라, 요청한 플레이어의 의료시설 레벨을 기준으로 제작
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MedicalFacility))]
    public sealed class MedicalCraftingController : NetworkBehaviour
    {
        [Header("의료시설 레시피")]
        [SerializeField]
        private List<RecipeSO> recipes = new();

        [Header("디버그 제작")]
        [SerializeField]
        private string debugRecipeID;

        [Header("로그")]
        [SerializeField]
        private bool logCraftResult = true;

        private readonly List<ItemRequirement> consumedIngredients = new();

        public IReadOnlyList<RecipeSO> GetAllRecipes()
        {
            return recipes;
        }

        public void RequestCraft(string recipeID)
        {
            if (string.IsNullOrWhiteSpace(recipeID))
            {
                FailCraft(recipeID, "제작 요청 Recipe ID가 비어 있습니다.");
                return;
            }

            if (!HousingInventoryResolver.IsNetworkReady(out string failReason))
            {
                if (TryCraftWithLobbySave(recipeID))
                    return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TryCraftOfflineForDebug(recipeID))
                    return;
#endif
                FailCraft(recipeID, failReason);
                return;
            }

            RequestCraftRpc(recipeID);
        }

        private bool TryCraftWithLobbySave(string recipeID)
        {
            if (!HousingInventoryResolver.TryGetLobbyInventory(out IInventory inventory, out _))
                return false;

            if (!TryFindRecipe(recipeID, out RecipeSO recipe))
            {
                FailCraft(recipeID, "?덉떆?쇰? 李얠? 紐삵뻽?듬땲??");
                return true;
            }

            if (!IsRecipeValid(recipe))
                return true;

            int lobbyMedicalLevel = 1;
            if (HousingInventoryResolver.TryGetLobbyFacilityLevel(FacilityType.Medical, out int savedLevel))
                lobbyMedicalLevel = Mathf.Max(1, savedLevel);

            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);
            if (lobbyMedicalLevel < requiredLevel)
            {
                FailCraft(recipeID, $"Medical Lv.{requiredLevel} is required. Current Lv.{lobbyMedicalLevel}");
                return true;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (inventory is HousingLobbyInventoryBridge lobbyInventoryBridge)
            {
                if (!lobbyInventoryBridge.TryCraftRecipeToStash(recipe, resultCount, out string failReason))
                {
                    FailCraft(recipe.recipeID, failReason);
                    return true;
                }
            }
            else
            {
                if (!HasAllIngredients(inventory, recipe))
                {
                    FailCraft(recipeID, "?쒖옉 ?щ즺媛 遺議깊빀?덈떎.");
                    return true;
                }

                if (!CanAcceptCraftResult(inventory, recipe, resultCount))
                {
                    FailCraft(recipe.recipeID, "寃곌낵 ?꾩씠?쒖쓣 諛쏆쓣 ?몃깽?좊━ 怨듦컙??遺議깊빀?덈떎.");
                    return true;
                }

                if (!ConsumeAllIngredients(inventory, recipe))
                {
                    FailCraft(recipeID, "?쒖옉 ?щ즺 ?뚮え???ㅽ뙣?덉뒿?덈떎.");
                    return true;
                }

                if (!TryAddCraftResult(inventory, recipe, resultCount, out int addedCount))
                {
                    RemoveAddedCraftResult(inventory, recipe, addedCount);
                    RestoreConsumedIngredients(inventory);
                    FailCraft(recipeID, "寃곌낵 ?꾩씠??吏湲됱뿉 ?ㅽ뙣?덉뒿?덈떎. ?뚮え???щ즺瑜??섎룎?몄뒿?덈떎.");
                    return true;
                }

                consumedIngredients.Clear();
            }

            EventBus.Publish(new HousingCraftResultEvent
            {
                facilityName = "Medical",
                recipeId = recipe.recipeID,
                resultItemId = recipe.result.itemID,
                resultCount = resultCount,
                success = true,
                reason = "로비 보관함 제작 성공"
            });

            if (logCraftResult)
            {
                Debug.Log(
                    $"[MedicalCraftingController] Lobby save medical craft succeeded. RecipeID={recipe.recipeID}, Result={recipe.result.itemID} x{resultCount}",
                    this);
            }

            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool TryCraftOfflineForDebug(string recipeID)
        {
            IInventory inventory = HousingCraftMaterialDebugInventory.Instance;

            if (inventory == null)
                return false;

            if (!TryFindRecipe(recipeID, out RecipeSO recipe))
            {
                FailCraft(recipeID, "레시피를 찾지 못했습니다.");
                return true;
            }

            if (!IsRecipeValid(recipe))
                return true;

            int debugMedicalLevel = HousingCraftMaterialDebugInventory.ResolveFacilityLevel(FacilityType.Medical, 1);
            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);

            if (debugMedicalLevel < requiredLevel)
            {
                FailCraft(recipeID, $"의료시설 Lv.{requiredLevel} 이상이 필요합니다. 현재 의료시설 Lv.{debugMedicalLevel}");
                return true;
            }

            if (!HasAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "오프라인 테스트 인벤토리에 제작 재료가 부족합니다.");
                return true;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (!CanAcceptCraftResult(inventory, recipe, resultCount))
            {
                FailCraft(recipe.recipeID, "오프라인 테스트 인벤토리가 제작 결과를 받을 수 없습니다.");
                return true;
            }

            if (!ConsumeAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "오프라인 테스트 제작 재료 소모에 실패했습니다.");
                return true;
            }

            if (!TryAddCraftResult(inventory, recipe, resultCount, out int addedCount))
            {
                RemoveAddedCraftResult(inventory, recipe, addedCount);
                RestoreConsumedIngredients(inventory);
                FailCraft(recipeID, "오프라인 테스트 제작 결과 지급에 실패했습니다. 재료를 복구했습니다.");
                return true;
            }

            // 오프라인 테스트 제작은 메모리 인벤토리에만 결과가 들어가므로, 로비 보관함 저장 데이터에도 같은 제작 결과를 반영합니다.
            if (!HousingOfflineCraftSaveSync.TryApplyCraftToLobbyStash(recipe, resultCount, out string saveFailReason))
            {
                RemoveAddedCraftResult(inventory, recipe, addedCount);
                RestoreConsumedIngredients(inventory);
                FailCraft(recipe.recipeID, saveFailReason);
                return true;
            }

            consumedIngredients.Clear();

            EventBus.Publish(new HousingCraftResultEvent
            {
                facilityName = "Medical",
                recipeId = recipe.recipeID,
                resultItemId = recipe.result.itemID,
                resultCount = resultCount,
                success = true,
                reason = "오프라인 테스트 제작 성공"
            });

            if (logCraftResult)
            {
                Debug.Log(
                    $"[MedicalCraftingController] 오프라인 테스트 의료시설 제작 성공\n" +
                    $"RecipeID: {recipe.recipeID}\n" +
                    $"Result: {recipe.result.itemID} x{resultCount}",
                    this);
            }

            return true;
        }
#endif

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCraftRpc(string recipeID, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            ulong requesterClientId = rpcParams.Receive.SenderClientId;

            if (!HousingInventoryResolver.TryGetRequesterInventory(
                    requesterClientId,
                    out IInventory inventory,
                    out string inventoryFailReason))
            {
                FailCraft(recipeID, inventoryFailReason);
                return;
            }

            if (!PlayerHousingProgressResolver.TryGetProgress(
                    requesterClientId,
                    out PlayerHousingProgress progress))
            {
                FailCraft(recipeID, "요청자의 하우징 진행도를 찾을 수 없습니다.");
                return;
            }

            TryCraftWithRequesterData(recipeID, requesterClientId, inventory, progress);
        }

        private bool TryCraftWithRequesterData(
            string recipeID,
            ulong requesterClientId,
            IInventory inventory,
            PlayerHousingProgress progress)
        {
            if (!IsServer)
            {
                FailCraft(recipeID, "의료시설 제작은 서버에서만 처리할 수 있습니다.");
                return false;
            }

            if (inventory == null)
            {
                FailCraft(recipeID, "제작에 사용할 요청자 인벤토리가 없습니다.");
                return false;
            }

            if (progress == null)
            {
                FailCraft(recipeID, "요청자의 하우징 진행도가 없습니다.");
                return false;
            }

            if (!TryFindRecipe(recipeID, out RecipeSO recipe))
            {
                FailCraft(recipeID, "레시피를 찾지 못했습니다.");
                return false;
            }

            if (!IsRecipeValid(recipe))
                return false;

            int requesterMedicalLevel = progress.GetLevel(FacilityType.Medical);
            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);

            if (requesterMedicalLevel < requiredLevel)
            {
                FailCraft(
                    recipeID,
                    $"의료시설 Lv.{requiredLevel} 이상이 필요합니다. 현재 내 의료시설 Lv.{requesterMedicalLevel}"
                );
                return false;
            }

            if (!HasAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "제작 재료가 부족합니다.");
                return false;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (!CanAcceptCraftResult(inventory, recipe, resultCount))
            {
                FailCraft(recipe.recipeID, "결과 아이템을 받을 인벤토리 공간이 부족합니다.");
                return false;
            }

            if (!ConsumeAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "제작 재료 소모에 실패했습니다.");
                return false;
            }

            if (!TryAddCraftResult(inventory, recipe, resultCount, out int addedCount))
            {
                RemoveAddedCraftResult(inventory, recipe, addedCount);
                RestoreConsumedIngredients(inventory);
                FailCraft(recipeID, "결과 아이템 지급에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedIngredients.Clear();

            PlayerHousingSaveSyncer saveSyncer = progress.GetComponent<PlayerHousingSaveSyncer>();

            if (saveSyncer != null)
            {
                saveSyncer.RequestLobbyInventorySaveFromServer($"Medical craft inventory snapshot: {recipe.recipeID}");
            }
            else
            {
                Debug.LogWarning(
                    $"[MedicalCraftingController] PlayerHousingSaveSyncer가 없어 제작 결과 저장 요청을 보낼 수 없습니다. ClientId: {requesterClientId}",
                    progress
                );
            }

            EventBus.Publish(new HousingCraftResultEvent
            {
                facilityName = "Medical",
                recipeId = recipe.recipeID,
                resultItemId = recipe.result.itemID,
                resultCount = resultCount,
                success = true,
                reason = "제작 성공"
            });

            if (logCraftResult)
            {
                Debug.Log(
                    $"[MedicalCraftingController] 플레이어별 의료시설 제작 성공\n" +
                    $"ClientId: {requesterClientId}\n" +
                    $"RecipeID: {recipe.recipeID}\n" +
                    $"Result: {recipe.result.itemID} x{resultCount}",
                    this
                );
            }

            return true;
        }

        private bool TryFindRecipe(string recipeID, out RecipeSO recipe)
        {
            recipe = null;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO candidate = recipes[i];

                if (candidate == null)
                    continue;

                if (candidate.recipeID != recipeID)
                    continue;

                recipe = candidate;
                return true;
            }

            return false;
        }

        private bool IsRecipeValid(RecipeSO recipe)
        {
            if (recipe == null)
            {
                FailCraft(string.Empty, "레시피 데이터가 없습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.recipeID))
            {
                FailCraft(string.Empty, "recipeID가 비어 있는 레시피가 있습니다.");
                return false;
            }

            if (recipe.result == null)
            {
                FailCraft(recipe.recipeID, "제작 결과 아이템이 비어 있습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.result.itemID))
            {
                FailCraft(recipe.recipeID, "제작 결과 아이템의 itemID가 비어 있습니다.");
                return false;
            }

            return true;
        }

        private bool HasAllIngredients(IInventory inventory, RecipeSO recipe)
        {
            if (inventory == null || recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null)
                    return false;

                if (string.IsNullOrWhiteSpace(ingredient.item.itemID))
                    return false;

                int amount = Mathf.Max(1, ingredient.amount);

                if (!inventory.HasItem(ingredient.item.itemID, amount))
                    return false;
            }

            return true;
        }

        private bool ConsumeAllIngredients(IInventory inventory, RecipeSO recipe)
        {
            consumedIngredients.Clear();

            if (inventory == null || recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                {
                    RestoreConsumedIngredients(inventory);
                    return false;
                }

                int amount = Mathf.Max(1, ingredient.amount);

                if (!inventory.ConsumeItem(ingredient.item.itemID, amount))
                {
                    RestoreConsumedIngredients(inventory);
                    return false;
                }

                consumedIngredients.Add(new ItemRequirement
                {
                    item = ingredient.item,
                    amount = amount
                });
            }

            return true;
        }

        private bool CanAcceptCraftResult(IInventory inventory, RecipeSO recipe, int resultCount)
        {
            if (inventory == null || recipe == null || recipe.result == null)
                return false;

            int safeResultCount = Mathf.Max(1, resultCount);

            if (inventory is GridInventory gridInventory)
                return gridInventory.CanAddItem(recipe.result, safeResultCount);

            return true;
        }
        private bool TryAddCraftResult(IInventory inventory, RecipeSO recipe, int resultCount, out int addedCount)
        {
            addedCount = 0;

            if (inventory == null || recipe == null || recipe.result == null)
                return false;

            int safeResultCount = Mathf.Max(1, resultCount);

            for (int i = 0; i < safeResultCount; i++)
            {
                if (!inventory.TryAddItem(recipe.result, 1))
                    return false;

                addedCount++;
            }

            return true;
        }

        private void RemoveAddedCraftResult(IInventory inventory, RecipeSO recipe, int addedCount)
        {
            if (inventory == null || recipe == null || recipe.result == null)
                return;

            if (addedCount <= 0)
                return;

            if (string.IsNullOrWhiteSpace(recipe.result.itemID))
                return;

            inventory.ConsumeItem(recipe.result.itemID, addedCount);
        }

        private void RestoreConsumedIngredients(IInventory inventory)
        {
            if (inventory == null)
                return;

            for (int i = 0; i < consumedIngredients.Count; i++)
            {
                ItemRequirement ingredient = consumedIngredients[i];

                if (ingredient.item == null)
                    continue;

                int amount = Mathf.Max(1, ingredient.amount);
                inventory.TryAddItem(ingredient.item, amount);
            }

            consumedIngredients.Clear();
        }

        private void FailCraft(string recipeID, string reason)
        {
            EventBus.Publish(new HousingCraftResultEvent
            {
                facilityName = "Medical",
                recipeId = recipeID,
                resultItemId = string.Empty,
                resultCount = 0,
                success = false,
                reason = reason
            });

            if (!logCraftResult)
                return;

            Debug.LogWarning($"[MedicalCraftingController] 제작 실패: {reason} RecipeID: {recipeID}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 제작 실행")]
        private void DebugCraftRecipe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[MedicalCraftingController] 플레이 중에만 제작 테스트를 실행할 수 있습니다.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(debugRecipeID))
            {
                Debug.LogWarning("[MedicalCraftingController] Debug Recipe ID가 비어 있습니다.", this);
                return;
            }

            RequestCraft(debugRecipeID);
        }
#endif
    }
}

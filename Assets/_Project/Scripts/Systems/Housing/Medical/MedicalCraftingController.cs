using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MedicalFacility))]
    public sealed class MedicalCraftingController : NetworkBehaviour
    {
        [Header("의료시설 레시피")]
        [SerializeField]
        private List<RecipeSO> recipes = new();

        [Header("로그")]
        [SerializeField]
        private bool logCraftResult = true;

        private MedicalFacility medicalFacility;
        private readonly List<ItemRequirement> consumedIngredients = new();

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnValidate()
        {
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();
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
                FailCraft(recipeID, failReason);
                return;
            }

            RequestCraftRpc(recipeID);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCraftRpc(string recipeID, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            ulong requesterClientId = rpcParams.Receive.SenderClientId;

            if (!HousingInventoryResolver.TryGetRequesterInventory(
                    requesterClientId,
                    out IInventory inventory,
                    out string failReason))
            {
                FailCraft(recipeID, failReason);
                return;
            }

            TryCraftWithInventory(recipeID, inventory);
        }

        public bool TryCraftWithInventory(string recipeID, IInventory inventory)
        {
            if (!IsServer)
            {
                FailCraft(recipeID, "의료시설 제작은 서버에서만 처리할 수 있습니다.");
                return false;
            }

            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();

            if (medicalFacility == null)
            {
                FailCraft(recipeID, "MedicalFacility가 없습니다.");
                return false;
            }

            if (inventory == null)
            {
                FailCraft(recipeID, "제작에 사용할 실제 인벤토리가 없습니다.");
                return false;
            }

            if (!TryFindRecipe(recipeID, out RecipeSO recipe))
            {
                FailCraft(recipeID, "레시피를 찾지 못했습니다.");
                return false;
            }

            if (!IsRecipeValid(recipe))
                return false;

            int currentLevel = medicalFacility.GetCurrentLevel();
            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);

            if (currentLevel < requiredLevel)
            {
                FailCraft(recipeID, $"시설 레벨이 부족합니다. 현재 LV{currentLevel}, 필요 LV{requiredLevel}");
                return false;
            }

            if (!HasAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "제작 재료가 부족합니다.");
                return false;
            }

            if (!ConsumeAllIngredients(inventory, recipe))
            {
                FailCraft(recipeID, "제작 재료 소모에 실패했습니다.");
                return false;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (!TryAddCraftResult(inventory, recipe, resultCount, out int addedCount))
            {
                RemoveAddedCraftResult(inventory, recipe, addedCount);
                RestoreConsumedIngredients(inventory);
                FailCraft(recipeID, "결과 아이템 지급에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedIngredients.Clear();

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
                Debug.Log($"[MedicalCraftingController] 제작 성공: {recipe.recipeID} → {recipe.result.itemID} x{resultCount}", this);

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

        private bool TryAddCraftResult(IInventory inventory, RecipeSO recipe, int resultCount, out int addedCount)
        {
            addedCount = 0;

            if (inventory == null || recipe == null || recipe.result == null)
                return false;

            int safeResultCount = Mathf.Max(1, resultCount);

            for (int i = 0; i < safeResultCount; i++)
            {
                bool addResult = inventory.TryAddItem(recipe.result, 1);

                Debug.Log(
                    $"[MedicalCraftingController] 결과 아이템 지급 시도\n" +
                    $"아이템: {recipe.result.itemID}\n" +
                    $"현재 지급 인덱스: {i + 1}/{safeResultCount}\n" +
                    $"지급 성공 여부: {addResult}",
                    this
                );

                if (!addResult)
                {
                    Debug.LogWarning(
                        $"[MedicalCraftingController] 결과 아이템 지급 실패\n" +
                        $"아이템 ID: {recipe.result.itemID}\n" +
                        $"resultCount: {safeResultCount}",
                        this
                    );

                    return false;
                }

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
    }
}
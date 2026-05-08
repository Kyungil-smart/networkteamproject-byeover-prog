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
        [SerializeField] private List<RecipeSO> recipes = new();

        [Header("테스트 인벤토리")]
        [SerializeField] private bool useTestInventory = false;
        [SerializeField] private WorkbenchTestInventory testInventory;

        [Header("로그")]
        [SerializeField] private bool logCraftResult = true;

        private MedicalFacility medicalFacility;
        private readonly List<ItemRequirement> consumedIngredients = new();

        private void Awake()
        {
            medicalFacility = GetComponent<MedicalFacility>();
        }

        public void RequestCraft(string recipeID)
        {
            if (string.IsNullOrWhiteSpace(recipeID))
            {
                FailCraft(recipeID, "제작 요청 Recipe ID가 비어 있습니다.");
                return;
            }

            if (useTestInventory)
            {
                TryCraftWithInventory(recipeID, testInventory);
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                FailCraft(recipeID, "네트워크가 시작되지 않았습니다.");
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

            if (!TryGetRequesterInventory(requesterClientId, out IInventory inventory))
            {
                FailCraft(recipeID, $"제작 요청자의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}");
                return;
            }

            TryCraftWithInventory(recipeID, inventory);
        }

        private bool TryCraftWithInventory(string recipeID, IInventory inventory)
        {
            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();

            if (medicalFacility == null)
            {
                FailCraft(recipeID, "MedicalFacility가 없습니다.");
                return false;
            }

            if (inventory == null)
            {
                FailCraft(recipeID, "제작에 사용할 인벤토리가 없습니다.");
                return false;
            }

            if (!TryFindRecipe(recipeID, out RecipeSO recipe))
            {
                FailCraft(recipeID, "레시피를 찾지 못했습니다.");
                return false;
            }

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

            if (!inventory.TryAddItem(recipe.result, resultCount))
            {
                RestoreConsumedIngredients(inventory);
                FailCraft(recipeID, "결과 아이템 지급에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedIngredients.Clear();

            if (logCraftResult)
                Debug.Log($"[MedicalCraftingController] 제작 성공: {recipe.recipeID} → {recipe.result.itemID} x{resultCount}", this);

            return true;
        }

        private bool TryFindRecipe(string recipeID, out RecipeSO recipe)
        {
            recipe = null;

            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i] == null)
                    continue;

                if (recipes[i].recipeID != recipeID)
                    continue;

                recipe = recipes[i];
                return true;
            }

            return false;
        }

        private bool HasAllIngredients(IInventory inventory, RecipeSO recipe)
        {
            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
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

        private void RestoreConsumedIngredients(IInventory inventory)
        {
            for (int i = 0; i < consumedIngredients.Count; i++)
            {
                ItemRequirement ingredient = consumedIngredients[i];

                if (ingredient.item == null)
                    continue;

                inventory.TryAddItem(ingredient.item, Mathf.Max(1, ingredient.amount));
            }

            consumedIngredients.Clear();
        }

        private bool TryGetRequesterInventory(ulong requesterClientId, out IInventory inventory)
        {
            inventory = null;

            if (NetworkManager.Singleton == null)
                return false;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requesterClientId, out NetworkClient client))
                return false;

            if (client.PlayerObject == null)
                return false;

            inventory = client.PlayerObject.GetComponent<IInventory>();

            if (inventory != null)
                return true;

            inventory = client.PlayerObject.GetComponentInChildren<IInventory>(true);
            return inventory != null;
        }

        private void FailCraft(string recipeID, string reason)
        {
            if (!logCraftResult)
                return;

            Debug.LogWarning($"[MedicalCraftingController] 제작 실패: {reason} RecipeID: {recipeID}", this);
        }
    }
}
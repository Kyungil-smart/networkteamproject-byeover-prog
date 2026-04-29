using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 작업대 제작 요청을 처리하는 컨트롤러입니다.
    /// 레시피 목록과 레벨 제한은 WorkbenchRecipeCatalog가 담당하고,
    /// 이 스크립트는 IInventory 기준으로 재료 검사, 재료 소모, 결과 지급만 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Workbench))]
    [RequireComponent(typeof(WorkbenchRecipeCatalog))]
    public class WorkbenchCraftingController : NetworkBehaviour
    {
        [Header("작업대 레시피")]
        [SerializeField]
        [Tooltip("작업대 레시피 목록과 레벨 제한을 관리하는 카탈로그입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private WorkbenchRecipeCatalog recipeCatalog;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player 인벤토리 대신 WorkbenchTestInventory로 제작을 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("플레이어 인벤토리 완성 전까지 사용할 테스트용 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("디버그 제작")]
        [SerializeField]
        [Tooltip("Context Menu로 제작 테스트할 RecipeSO의 recipeID입니다.")]
        private string debugRecipeID;

        private readonly List<ItemRequirement> consumedIngredients = new List<ItemRequirement>();

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
            if (recipeCatalog == null)
                recipeCatalog = GetComponent<WorkbenchRecipeCatalog>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        public bool CanCraft(string recipeID)
        {
            IInventory inventory = GetActiveInventoryForCheck();

            if (inventory == null)
                return false;

            return CanCraftWithInventory(recipeID, inventory);
        }

        public bool CanCraftWithInventory(string recipeID, IInventory inventory)
        {
            if (inventory == null)
                return false;

            if (!TryGetUnlockedRecipe(recipeID, out RecipeSO recipe))
                return false;

            if (!IsRecipeValid(recipe))
                return false;

            return HasAllIngredients(inventory, recipe);
        }

        public void RequestCraft(string recipeID)
        {
            if (string.IsNullOrWhiteSpace(recipeID))
            {
                Debug.LogWarning("[WorkbenchCraftingController] 제작 요청 Recipe ID가 비어 있습니다.", this);
                return;
            }

            if (useTestInventory)
            {
                TryCraftWithInventory(recipeID, testInventory);
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
                Debug.LogWarning($"[WorkbenchCraftingController] 제작을 요청한 플레이어의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}", this);
                return;
            }

            TryCraftWithInventory(recipeID, inventory);
        }

        public bool TryCraftWithInventory(string recipeID, IInventory inventory)
        {
            if (inventory == null)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 제작에 사용할 인벤토리가 없습니다.", this);
                return false;
            }

            if (!TryGetUnlockedRecipe(recipeID, out RecipeSO recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 현재 작업대 레벨에서 사용할 수 없는 레시피입니다. RecipeID: {recipeID}", this);
                return false;
            }

            if (!IsRecipeValid(recipe))
                return false;

            if (!HasAllIngredients(inventory, recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 재료가 부족합니다. RecipeID: {recipe.recipeID}", this);
                return false;
            }

            if (!ConsumeAllIngredients(inventory, recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 재료 소모에 실패했습니다. RecipeID: {recipe.recipeID}", this);
                return false;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (!inventory.TryAddItem(recipe.result, resultCount))
            {
                RestoreConsumedIngredients(inventory);
                Debug.LogWarning($"[WorkbenchCraftingController] 결과 아이템 지급에 실패했습니다. 소모한 재료를 되돌렸습니다. RecipeID: {recipe.recipeID}", this);
                return false;
            }

            consumedIngredients.Clear();

            Debug.Log($"[WorkbenchCraftingController] 제작 성공: {recipe.recipeID} → {recipe.result.itemID} x{resultCount}", this);
            return true;
        }

        private bool TryGetUnlockedRecipe(string recipeID, out RecipeSO recipe)
        {
            recipe = null;

            if (recipeCatalog == null)
            {
                recipeCatalog = GetComponent<WorkbenchRecipeCatalog>();
            }

            if (recipeCatalog == null)
            {
                Debug.LogWarning("[WorkbenchCraftingController] WorkbenchRecipeCatalog가 없습니다.", this);
                return false;
            }

            return recipeCatalog.TryGetUnlockedRecipe(recipeID, out recipe);
        }

        private bool IsRecipeValid(RecipeSO recipe)
        {
            if (recipe == null)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 레시피 데이터가 없습니다.", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.recipeID))
            {
                Debug.LogWarning("[WorkbenchCraftingController] recipeID가 비어 있는 레시피가 있습니다.", this);
                return false;
            }

            if (recipe.result == null)
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 결과 아이템이 비어 있습니다. RecipeID: {recipe.recipeID}", this);
                return false;
            }

            return true;
        }

        private bool HasAllIngredients(IInventory inventory, RecipeSO recipe)
        {
            if (inventory == null)
                return false;

            if (recipe == null)
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

            if (inventory == null)
                return false;

            if (recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null)
                {
                    RestoreConsumedIngredients(inventory);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ingredient.item.itemID))
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

        private IInventory GetActiveInventoryForCheck()
        {
            if (useTestInventory)
                return testInventory;

            if (NetworkManager.Singleton == null)
                return null;

            if (!IsServer)
                return null;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            if (!TryGetRequesterInventory(localClientId, out IInventory inventory))
                return null;

            return inventory;
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
            return inventory != null;
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 제작 실행")]
        private void DebugCraftRecipe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 플레이 중에만 제작 테스트를 실행할 수 있습니다.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(debugRecipeID))
            {
                Debug.LogWarning("[WorkbenchCraftingController] Debug Recipe ID가 비어 있습니다.", this);
                return;
            }

            RequestCraft(debugRecipeID);
        }
#endif
    }
}
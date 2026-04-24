using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 작업대 제작 요청을 처리하는 중심 스크립트이다.
    // 현재는 UI와 Player 인벤토리가 아직 없으므로 WorkbenchTestInventory로 제작 로직을 먼저 검증한다.
    // 추후 Player 작업이 완료되면 실제 IInventory를 받아 제작 처리하도록 확장할 수 있다.
    public class WorkbenchCraftingController : NetworkBehaviour
    {
        [Header("테스트 모드")]

        [Tooltip("체크하면 Player 인벤토리 없이 WorkbenchTestInventory로 제작을 테스트합니다.")]
        [SerializeField] private bool useTestInventory = true;

        [Tooltip("플레이어 인벤토리 대신 사용할 테스트 인벤토리입니다.")]
        [SerializeField] private WorkbenchTestInventory testInventory;


        [Header("테스트용 작업대 레벨")]

        [Tooltip("Workbench 시설 연결 전까지 사용할 임시 작업대 레벨입니다.")]
        [Min(1)]
        [SerializeField] private int testWorkbenchLevel = 1;


        [Header("제작 레시피")]

        [Tooltip("이 작업대에서 제작 가능한 레시피 목록입니다.")]
        [SerializeField] private List<RecipeSO> recipes = new List<RecipeSO>();


        [Header("디버그 테스트")]

        [Tooltip("테스트용으로 제작할 레시피 ID입니다.")]
        [SerializeField] private string debugRecipeID;


        private readonly Dictionary<string, RecipeSO> recipeLookup = new Dictionary<string, RecipeSO>();


        private void Awake()
        {
            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();

            BuildRecipeLookup();
        }

        private void OnValidate()
        {
            if (testWorkbenchLevel < 1)
                testWorkbenchLevel = 1;

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        private void BuildRecipeLookup()
        {
            recipeLookup.Clear();

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (recipe == null)
                    continue;

                if (string.IsNullOrWhiteSpace(recipe.recipeID))
                    continue;

                if (recipeLookup.ContainsKey(recipe.recipeID))
                {
                    Debug.LogWarning($"[WorkbenchCraftingController] 중복된 레시피 ID가 있습니다: {recipe.recipeID}", this);
                    continue;
                }

                recipeLookup.Add(recipe.recipeID, recipe);
            }
        }

        public IReadOnlyList<RecipeSO> GetRecipes()
        {
            return recipes;
        }

        public void RequestCraft(string recipeID)
        {
            if (string.IsNullOrWhiteSpace(recipeID))
            {
                Debug.LogWarning("[WorkbenchCraftingController] 제작 요청 레시피 ID가 비어 있습니다.", this);
                return;
            }

            if (useTestInventory)
            {
                TryCraftWithInventory(recipeID, testInventory);
                return;
            }

            TryCraftServerRpc(recipeID);
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryCraftServerRpc(string recipeID, ServerRpcParams rpcParams = default)
        {
            ulong requesterClientId = rpcParams.Receive.SenderClientId;

            if (!TryGetRequesterInventory(requesterClientId, out IInventory inventory))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작을 요청한 플레이어의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}", this);
                return;
            }

            TryCraftWithInventory(recipeID, inventory);
        }

        private void TryCraftWithInventory(string recipeID, IInventory inventory)
        {
            BuildRecipeLookup();

            if (inventory == null)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 제작에 사용할 인벤토리가 없습니다.", this);
                return;
            }

            if (!TryGetRecipe(recipeID, out RecipeSO recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 레시피를 찾지 못했습니다. ID: {recipeID}", this);
                return;
            }

            if (!CanUseRecipeByWorkbenchLevel(recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 작업대 레벨이 부족합니다. 현재 레벨: {testWorkbenchLevel}, 필요 레벨: {recipe.requiredFacilityLevel}", this);
                return;
            }

            if (recipe.result == null)
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 결과 아이템이 비어 있습니다. RecipeID: {recipe.recipeID}", this);
                return;
            }

            if (!HasAllIngredients(inventory, recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 재료가 부족합니다. RecipeID: {recipe.recipeID}", this);
                return;
            }

            if (!ConsumeAllIngredients(inventory, recipe))
            {
                Debug.LogWarning($"[WorkbenchCraftingController] 제작 재료 소모에 실패했습니다. RecipeID: {recipe.recipeID}", this);
                return;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);
            bool resultAdded = inventory.TryAddItem(recipe.result, resultCount);

            if (!resultAdded)
            {
                RollbackIngredients(inventory, recipe);
                Debug.LogWarning($"[WorkbenchCraftingController] 결과 아이템 지급에 실패했습니다. 재료를 되돌렸습니다. RecipeID: {recipe.recipeID}", this);
                return;
            }

            Debug.Log($"[WorkbenchCraftingController] 제작 성공: {recipe.recipeID} → {recipe.result.itemID} x{resultCount}", this);
        }

        private bool TryGetRecipe(string recipeID, out RecipeSO recipe)
        {
            recipe = null;

            if (string.IsNullOrWhiteSpace(recipeID))
                return false;

            if (recipeLookup.TryGetValue(recipeID, out recipe) && recipe != null)
                return true;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO currentRecipe = recipes[i];

                if (currentRecipe == null)
                    continue;

                if (currentRecipe.recipeID != recipeID)
                    continue;

                recipe = currentRecipe;
                return true;
            }

            return false;
        }

        private bool CanUseRecipeByWorkbenchLevel(RecipeSO recipe)
        {
            if (recipe == null)
                return false;

            return testWorkbenchLevel >= recipe.requiredFacilityLevel;
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

                int amount = Mathf.Max(1, ingredient.amount);

                if (!inventory.HasItem(ingredient.item.itemID, amount))
                    return false;
            }

            return true;
        }

        private bool ConsumeAllIngredients(IInventory inventory, RecipeSO recipe)
        {
            if (inventory == null)
                return false;

            if (recipe == null)
                return false;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            List<ItemRequirement> consumedIngredients = new List<ItemRequirement>();

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null)
                {
                    RestoreConsumedIngredients(inventory, consumedIngredients);
                    return false;
                }

                int amount = Mathf.Max(1, ingredient.amount);
                bool consumed = inventory.ConsumeItem(ingredient.item.itemID, amount);

                if (!consumed)
                {
                    RestoreConsumedIngredients(inventory, consumedIngredients);
                    return false;
                }

                consumedIngredients.Add(ingredient);
            }

            return true;
        }

        private void RollbackIngredients(IInventory inventory, RecipeSO recipe)
        {
            if (inventory == null)
                return;

            if (recipe == null)
                return;

            if (recipe.ingredients == null)
                return;

            RestoreConsumedIngredients(inventory, recipe.ingredients);
        }

        private void RestoreConsumedIngredients(IInventory inventory, IReadOnlyList<ItemRequirement> ingredients)
        {
            if (inventory == null)
                return;

            if (ingredients == null)
                return;

            for (int i = 0; i < ingredients.Count; i++)
            {
                ItemRequirement ingredient = ingredients[i];

                if (ingredient.item == null)
                    continue;

                int amount = Mathf.Max(1, ingredient.amount);
                inventory.TryAddItem(ingredient.item, amount);
            }
        }


#if UNITY_EDITOR
        [ContextMenu("Debug Craft Recipe")]
        private void DebugCraftRecipe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(debugRecipeID))
            {
                Debug.LogWarning("[WorkbenchCraftingController] Debug Recipe ID가 비어 있습니다.", this);
                return;
            }

            if (useTestInventory)
            {
                TryCraftWithInventory(debugRecipeID, testInventory);
                return;
            }

            if (!IsServer)
            {
                Debug.LogWarning("[WorkbenchCraftingController] 실제 인벤토리 테스트는 서버 또는 호스트 상태에서만 가능합니다.", this);
                return;
            }

            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[WorkbenchCraftingController] NetworkManager가 없습니다.", this);
                return;
            }

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            if (!TryGetRequesterInventory(localClientId, out IInventory inventory))
            {
                Debug.LogWarning("[WorkbenchCraftingController] 로컬 플레이어 인벤토리를 찾지 못했습니다.", this);
                return;
            }

            TryCraftWithInventory(debugRecipeID, inventory);
        }
#endif
    }
}
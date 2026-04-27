using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 작업대 제작 요청을 처리하는 컨트롤러이다.
    /// 작업대 레벨은 Workbench 시설의 CurrentLevel을 기준으로 판단한다.
    /// 현재 Player GridInventory와 UI가 완성되지 않았으므로, 제작 재료 검증은 WorkbenchTestInventory로 테스트할 수 있다.
    /// </summary>
    public class WorkbenchCraftingController : NetworkBehaviour
    {
        private const int MinWorkbenchLevel = 1;
        private const int MaxWorkbenchLevel = 4;

        [Header("작업대 시설")]

        [Tooltip("제작 가능 레벨을 판단할 Workbench 시설입니다. 비어 있으면 같은 오브젝트에서 자동으로 찾습니다.")]
        [SerializeField] private Workbench workbenchFacility;


        [Header("테스트 인벤토리")]

        [Tooltip("체크하면 Player 인벤토리 대신 WorkbenchTestInventory로 제작을 테스트합니다.")]
        [SerializeField] private bool useTestInventory = true;

        [Tooltip("플레이어 인벤토리 대신 사용할 테스트 인벤토리입니다.")]
        [SerializeField] private WorkbenchTestInventory testInventory;


        [Header("제작 레시피")]

        [Tooltip("이 작업대에서 사용할 제작 레시피 목록입니다.")]
        [SerializeField] private List<RecipeSO> recipes = new List<RecipeSO>();


        [Header("디버그 테스트")]

        [Tooltip("테스트용으로 제작할 레시피 ID입니다.")]
        [SerializeField] private string debugRecipeID;


        private readonly Dictionary<string, RecipeSO> recipeLookup = new Dictionary<string, RecipeSO>();


        private void Awake()
        {
            FindRequiredComponents();
            BuildRecipeLookup();
        }

        private void OnValidate()
        {
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (workbenchFacility == null)
                workbenchFacility = GetComponent<Workbench>();

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
                {
                    Debug.LogWarning("[WorkbenchCraftingController] recipeID가 비어 있는 레시피가 있습니다.", this);
                    continue;
                }

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

        public IReadOnlyList<RecipeSO> GetUnlockedRecipes()
        {
            BuildRecipeLookup();

            List<RecipeSO> unlockedRecipes = new List<RecipeSO>();

            if (!HasWorkbenchFacility())
                return unlockedRecipes;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (recipe == null)
                    continue;

                if (!CanUseRecipeByWorkbenchLevel(recipe))
                    continue;

                unlockedRecipes.Add(recipe);
            }

            return unlockedRecipes;
        }

        public int GetCurrentWorkbenchLevel()
        {
            if (workbenchFacility == null)
                return 0;

            return Mathf.Clamp(workbenchFacility.CurrentLevel.Value, MinWorkbenchLevel, MaxWorkbenchLevel);
        }

        public bool CanCraft(string recipeID)
        {
            BuildRecipeLookup();

            if (!HasWorkbenchFacility())
                return false;

            if (string.IsNullOrWhiteSpace(recipeID))
                return false;

            if (!TryGetRecipe(recipeID, out RecipeSO recipe))
                return false;

            if (!CanUseRecipeByWorkbenchLevel(recipe))
                return false;

            if (recipe.result == null)
                return false;

            IInventory inventory = GetActiveInventory();

            if (inventory == null)
                return false;

            return HasAllIngredients(inventory, recipe);
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

        private IInventory GetActiveInventory()
        {
            if (useTestInventory)
                return testInventory;

            if (NetworkManager.Singleton == null)
                return null;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            if (!TryGetRequesterInventory(localClientId, out IInventory inventory))
                return null;

            return inventory;
        }

        private void TryCraftWithInventory(string recipeID, IInventory inventory)
        {
            BuildRecipeLookup();

            if (!HasWorkbenchFacility())
                return;

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
                int currentLevel = GetCurrentWorkbenchLevel();
                int requiredLevel = GetRequiredWorkbenchLevel(recipe);

                Debug.LogWarning($"[WorkbenchCraftingController] 작업대 레벨이 부족합니다. 현재 레벨: {currentLevel}, 필요 레벨: {requiredLevel}, RecipeID: {recipe.recipeID}", this);
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

        private bool HasWorkbenchFacility()
        {
            if (workbenchFacility != null)
                return true;

            workbenchFacility = GetComponent<Workbench>();

            if (workbenchFacility != null)
                return true;

            Debug.LogWarning("[WorkbenchCraftingController] Workbench 시설 컴포넌트가 없습니다. 제작 레벨을 판단할 수 없습니다.", this);
            return false;
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

            int currentLevel = GetCurrentWorkbenchLevel();

            if (currentLevel < MinWorkbenchLevel)
                return false;

            int requiredLevel = GetRequiredWorkbenchLevel(recipe);

            return currentLevel >= requiredLevel;
        }

        private int GetRequiredWorkbenchLevel(RecipeSO recipe)
        {
            if (recipe == null)
                return MaxWorkbenchLevel;

            return Mathf.Clamp(recipe.requiredFacilityLevel, MinWorkbenchLevel, MaxWorkbenchLevel);
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
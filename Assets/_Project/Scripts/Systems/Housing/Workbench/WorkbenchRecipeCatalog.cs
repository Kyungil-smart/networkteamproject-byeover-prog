using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 작업대 제작 레시피 목록과 레벨 제한을 관리
    // 실제 재료 검사와 결과 지급은 WorkbenchCraftingController가 담당
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Workbench))]
    public class WorkbenchRecipeCatalog : MonoBehaviour
    {
        [Header("작업대")]
        [SerializeField]
        [Tooltip("레시피 레벨 제한을 확인할 작업대입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private Workbench workbench;

        [Header("레시피 목록")]
        [SerializeField]
        [Tooltip("이 작업대에서 사용할 수 있는 전체 제작 레시피 목록입니다.")]
        private List<RecipeSO> recipes = new();

        [Header("로그")]
        [SerializeField]
        [Tooltip("레시피 데이터 문제를 Console에 출력합니다.")]
        private bool logRecipeValidation = true;

        private readonly List<RecipeSO> cachedUnlockedRecipes = new();

        public Workbench Workbench => workbench;
        public int CurrentWorkbenchLevel => workbench != null ? workbench.CurrentLevel.Value : 0;

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
            RemoveEmptyRecipes();
        }

        private void FindRequiredComponents()
        {
            if (workbench == null)
                workbench = GetComponent<Workbench>();
        }

        private void RemoveEmptyRecipes()
        {
            if (recipes == null)
                return;

            for (int i = recipes.Count - 1; i >= 0; i--)
            {
                if (recipes[i] == null)
                    recipes.RemoveAt(i);
            }
        }

        public IReadOnlyList<RecipeSO> GetAllRecipes()
        {
            return recipes;
        }

        public IReadOnlyList<RecipeSO> GetUnlockedRecipes()
        {
            cachedUnlockedRecipes.Clear();

            if (recipes == null || recipes.Count == 0)
                return cachedUnlockedRecipes;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (CanUseRecipe(recipe, out _))
                    cachedUnlockedRecipes.Add(recipe);
            }

            return cachedUnlockedRecipes;
        }

        public bool CanUseRecipe(RecipeSO recipe)
        {
            return CanUseRecipe(recipe, out _);
        }

        public bool CanUseRecipe(RecipeSO recipe, out string failReason)
        {
            failReason = string.Empty;

            if (recipe == null)
            {
                failReason = "레시피 데이터가 없습니다.";
                return false;
            }

            if (workbench == null)
            {
                failReason = "Workbench 컴포넌트가 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.recipeID))
            {
                failReason = "recipeID가 비어 있는 레시피가 있습니다.";
                return false;
            }

            if (recipe.result == null)
            {
                failReason = $"레시피 결과 아이템이 비어 있습니다. RecipeID: {recipe.recipeID}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.result.itemID))
            {
                failReason = $"레시피 결과 아이템의 itemID가 비어 있습니다. RecipeID: {recipe.recipeID}";
                return false;
            }

            if (IsValuableResult(recipe.result))
            {
                failReason = "귀중품은 작업대에서 제작할 수 없습니다.";
                return false;
            }

            int requiredLevel = GetRequiredWorkbenchLevel(recipe);

            if (CurrentWorkbenchLevel < requiredLevel)
            {
                failReason = $"작업대 Lv.{requiredLevel} 이상이 필요합니다. 현재 Lv.{CurrentWorkbenchLevel}";
                return false;
            }

            return true;
        }

        public bool TryGetRecipe(string recipeId, out RecipeSO recipe)
        {
            recipe = null;

            if (string.IsNullOrWhiteSpace(recipeId))
                return false;

            if (recipes == null || recipes.Count == 0)
                return false;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO current = recipes[i];

                if (current == null)
                    continue;

                if (current.recipeID == recipeId)
                {
                    recipe = current;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetUnlockedRecipe(string recipeId, out RecipeSO recipe, out string failReason)
        {
            recipe = null;
            failReason = string.Empty;

            if (!TryGetRecipe(recipeId, out recipe))
            {
                failReason = $"레시피를 찾지 못했습니다. RecipeID: {recipeId}";
                return false;
            }

            return CanUseRecipe(recipe, out failReason);
        }

        public int GetRequiredWorkbenchLevel(RecipeSO recipe)
        {
            if (recipe == null)
                return 1;

            int levelByRecipe = Mathf.Clamp(recipe.requiredFacilityLevel, 1, 4);
            int levelByRarity = GetRequiredLevelByRarity(recipe.requiredTier);
            return Mathf.Max(levelByRecipe, levelByRarity);
        }

        private static int GetRequiredLevelByRarity(RarityTier rarity)
        {
            switch (rarity)
            {
                case RarityTier.Common:
                    return 1;

                case RarityTier.Uncommon:
                    return 2;

                case RarityTier.Rare:
                    return 3;

                case RarityTier.Epic:
                    return 4;

                case RarityTier.Legendary:
                    return 4;

                default:
                    return 1;
            }
        }

        private static bool IsValuableResult(ItemDataSO item)
        {
            if (item == null)
                return false;

            return item.category == ItemCategory.Valuable || item.isValuable;
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 레시피 검증")]
        private void DebugValidateRecipes()
        {
            if (recipes == null || recipes.Count == 0)
            {
                Debug.LogWarning("[WorkbenchRecipeCatalog] 등록된 레시피가 없습니다.", this);
                return;
            }

            HashSet<string> usedRecipeIds = new();

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (recipe == null)
                {
                    Debug.LogWarning($"[WorkbenchRecipeCatalog] 비어 있는 레시피 슬롯이 있습니다. Index: {i}", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(recipe.recipeID))
                {
                    Debug.LogWarning($"[WorkbenchRecipeCatalog] recipeID가 비어 있습니다. Index: {i}", this);
                    continue;
                }

                if (!usedRecipeIds.Add(recipe.recipeID))
                    Debug.LogWarning($"[WorkbenchRecipeCatalog] 중복 recipeID가 있습니다: {recipe.recipeID}", this);

                if (recipe.result == null)
                    Debug.LogWarning($"[WorkbenchRecipeCatalog] 결과 아이템이 비어 있습니다. RecipeID: {recipe.recipeID}", this);

                if (CanUseRecipe(recipe, out string failReason))
                {
                    if (logRecipeValidation)
                        Debug.Log($"[WorkbenchRecipeCatalog] 사용 가능 레시피: {recipe.recipeID}", this);
                }
                else
                {
                    if (logRecipeValidation)
                        Debug.Log($"[WorkbenchRecipeCatalog] 현재 사용 불가 레시피: {recipe.recipeID} / 사유: {failReason}", this);
                }
            }
        }
#endif
    }
}
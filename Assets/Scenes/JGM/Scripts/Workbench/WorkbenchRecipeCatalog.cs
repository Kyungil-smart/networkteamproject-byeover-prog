using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 작업대에서 사용할 레시피 목록과 레벨 제한을 관리합니다.
    // 실제 제작, 재료 소모, UI 표시는 다른 스크립트가 담당합니다.
  
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Workbench))]
    public class WorkbenchRecipeCatalog : MonoBehaviour
    {
        [Header("작업대")]
        [SerializeField]
        [Tooltip("레시피 레벨 제한을 확인할 Workbench 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private Workbench workbench;

        [Header("레시피 목록")]
        [SerializeField]
        [Tooltip("이 작업대에서 사용할 수 있는 전체 제작 레시피 목록입니다.")]
        private List<RecipeSO> recipes = new();

        [Header("제작 규칙")]
        [SerializeField]
        [Tooltip("귀중품 카테고리 아이템 제작을 허용할지 여부입니다. 기본값은 false입니다.")]
        private bool allowValuableCrafting = false;

        private readonly List<RecipeSO> cachedUnlockedRecipes = new();

        public Workbench Workbench => workbench;

        public int CurrentWorkbenchLevel
        {
            get
            {
                if (workbench == null)
                    return 0;

                return workbench.CurrentLevel.Value;
            }
        }

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

                if (CanUseRecipe(recipe))
                    cachedUnlockedRecipes.Add(recipe);
            }

            return cachedUnlockedRecipes;
        }

        public bool CanUseRecipe(RecipeSO recipe)
        {
            if (recipe == null)
                return false;

            if (workbench == null)
                return false;

            if (recipe.result == null)
                return false;

            if (!allowValuableCrafting && recipe.result.category == ItemCategory.Valuable)
                return false;

            return CurrentWorkbenchLevel >= recipe.requiredFacilityLevel;
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

        public bool TryGetUnlockedRecipe(string recipeId, out RecipeSO recipe)
        {
            if (!TryGetRecipe(recipeId, out recipe))
                return false;

            return CanUseRecipe(recipe);
        }
    }
}
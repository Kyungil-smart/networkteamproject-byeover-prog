using System;
using System.Collections.Generic;

using TMPro;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors.UI.Hideout
{
    // 작업대/의료시설 아이템 제작 창을 관리
    // 레시피 목록 표시, 재료 검사, 재료 소모, 결과 아이템 지급을 담당
    [DisallowMultipleComponent]
    public sealed class FacilityCraftWindowUI : MonoBehaviour
    {
        [Serializable]
        private sealed class FacilityViewBinding
        {
            public HideoutCameraFacilitySelector.FacilityView facilityView;
            public FacilityBase facility;
        }

        [Header("창 루트")]
        [SerializeField] private GameObject windowRoot;

        [Header("상단 표시")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text messageText;

        [Header("시설 연결")]
        [SerializeField] private List<FacilityViewBinding> facilityBindings = new();

        [Header("인벤토리")]
        [SerializeField] private MonoBehaviour inventoryBehaviour;

        [Header("레시피 목록")]
        [SerializeField] private List<RecipeSO> workbenchRecipes = new();
        [SerializeField] private List<RecipeSO> medicalRecipes = new();

        [Header("레시피 UI")]
        [SerializeField] private Transform recipeListRoot;
        [SerializeField] private FacilityCraftRecipeRowUI recipeRowPrefab;

        [Header("로그")]
        [SerializeField] private bool showDebugLog = true;

        private readonly List<FacilityCraftRecipeRowUI> spawnedRows = new();
        private readonly List<ItemRequirement> consumedIngredients = new();

        private HideoutCameraFacilitySelector.FacilityView currentFacilityView =
            HideoutCameraFacilitySelector.FacilityView.None;

        private FacilityBase currentFacility;
        private IInventory inventory;
        private bool isInitialized;

        public bool IsOpen => windowRoot != null && windowRoot.activeSelf;
        public GameObject WindowRoot => windowRoot != null ? windowRoot : gameObject;

        private void Reset()
        {
            windowRoot = gameObject;
        }

        private void Awake()
        {
            Initialize();
        }

        public void Open(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            Initialize();

            if (!CanUseCraftWindow(facilityView))
            {
                Debug.LogWarning($"[FacilityCraftWindowUI] {facilityView} 시설은 제작 창을 열 수 없습니다.", this);
                return;
            }

            if (!TryFindFacility(facilityView, out FacilityBase facility))
            {
                Debug.LogWarning($"[FacilityCraftWindowUI] {facilityView}에 연결된 FacilityBase가 없습니다.", this);
                return;
            }

            currentFacilityView = facilityView;
            currentFacility = facility;

            ResolveInventory();

            if (windowRoot != null)
                windowRoot.SetActive(true);

            Refresh();

            DebugLog($"{facilityView} 제작 창을 열었습니다.");
        }

        public void Close()
        {
            Initialize();

            currentFacilityView = HideoutCameraFacilitySelector.FacilityView.None;
            currentFacility = null;

            if (windowRoot != null)
                windowRoot.SetActive(false);

            ClearTexts();
            ClearRows();

            DebugLog("제작 창을 닫았습니다.");
        }

        public void Refresh()
        {
            ResolveInventory();

            if (currentFacility == null)
            {
                ClearTexts();
                ClearRows();
                return;
            }

            RefreshTexts();
            RefreshRecipeRows();
        }

        private void TryCraftRecipe(RecipeSO recipe)
        {
            if (recipe == null)
            {
                SetMessage("레시피 데이터가 없습니다.");
                return;
            }

            ResolveInventory();

            if (currentFacility == null)
            {
                SetMessage("현재 선택된 시설이 없습니다.");
                return;
            }

            if (inventory == null)
            {
                SetMessage("인벤토리가 연결되지 않았습니다.");
                return;
            }

            if (!IsRecipeValid(recipe))
                return;

            int currentLevel = currentFacility.GetCurrentLevel();
            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);

            if (currentLevel < requiredLevel)
            {
                SetMessage($"시설 레벨이 부족합니다. 필요 LV{requiredLevel}");
                Refresh();
                return;
            }

            if (!HasAllIngredients(recipe))
            {
                SetMessage("제작 재료가 부족합니다.");
                Refresh();
                return;
            }

            if (!ConsumeAllIngredients(recipe))
            {
                SetMessage("제작 재료 소모에 실패했습니다.");
                Refresh();
                return;
            }

            int resultCount = Mathf.Max(1, recipe.resultCount);

            if (!inventory.TryAddItem(recipe.result, resultCount))
            {
                RestoreConsumedIngredients();
                SetMessage("결과 아이템 지급에 실패했습니다. 재료를 되돌렸습니다.");
                Refresh();
                return;
            }

            consumedIngredients.Clear();

            string resultName = string.IsNullOrWhiteSpace(recipe.result.displayName)
                ? recipe.result.itemID
                : recipe.result.displayName;

            SetMessage($"{resultName} x{resultCount} 제작 완료");
            DebugLog($"제작 성공: {recipe.recipeID} → {recipe.result.itemID} x{resultCount}");

            Refresh();
        }

        private bool IsRecipeValid(RecipeSO recipe)
        {
            if (recipe == null)
            {
                SetMessage("레시피가 없습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.recipeID))
            {
                SetMessage("Recipe ID가 비어 있습니다.");
                return false;
            }

            if (recipe.result == null)
            {
                SetMessage("결과 아이템이 설정되지 않았습니다.");
                return false;
            }

            return true;
        }

        private bool HasAllIngredients(RecipeSO recipe)
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

        private bool ConsumeAllIngredients(RecipeSO recipe)
        {
            consumedIngredients.Clear();

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ItemRequirement ingredient = recipe.ingredients[i];

                if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                {
                    RestoreConsumedIngredients();
                    return false;
                }

                int amount = Mathf.Max(1, ingredient.amount);

                if (!inventory.ConsumeItem(ingredient.item.itemID, amount))
                {
                    RestoreConsumedIngredients();
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

        private void RestoreConsumedIngredients()
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

        private void Initialize()
        {
            if (isInitialized)
                return;

            if (windowRoot == null)
                windowRoot = gameObject;

            ResolveInventory();

            isInitialized = true;
        }

        private void ResolveInventory()
        {
            inventory = null;

            if (inventoryBehaviour != null)
            {
                if (inventoryBehaviour is IInventory directInventory)
                {
                    inventory = directInventory;
                    return;
                }

                IInventory sameObjectInventory = inventoryBehaviour.GetComponent<IInventory>();

                if (sameObjectInventory != null)
                {
                    inventory = sameObjectInventory;
                    return;
                }

                IInventory childInventory = inventoryBehaviour.GetComponentInChildren<IInventory>(true);

                if (childInventory != null)
                {
                    inventory = childInventory;
                    return;
                }
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IInventory foundInventory)
                    continue;

                inventory = foundInventory;
                inventoryBehaviour = behaviours[i];
                return;
            }
        }

        private void RefreshTexts()
        {
            int currentLevel = currentFacility != null ? currentFacility.GetCurrentLevel() : 0;
            int maxLevel = currentFacility != null ? currentFacility.GetMaxLevel() : 0;

            if (titleText != null)
                titleText.text = GetTitleText(currentFacilityView);

            if (descriptionText != null)
                descriptionText.text = GetDescriptionText(currentFacilityView);

            if (levelText != null)
                levelText.text = $"현재 시설 레벨: LV {currentLevel} / {maxLevel}";
        }

        private void RefreshRecipeRows()
        {
            ClearRows();

            IReadOnlyList<RecipeSO> recipes = GetCurrentRecipes();

            if (recipes == null || recipes.Count == 0)
            {
                DebugLog($"{currentFacilityView} 제작 레시피가 없습니다.");
                return;
            }

            if (recipeListRoot == null)
            {
                Debug.LogWarning("[FacilityCraftWindowUI] Recipe List Root가 연결되지 않았습니다.", this);
                return;
            }

            if (recipeRowPrefab == null)
            {
                Debug.LogWarning("[FacilityCraftWindowUI] Recipe Row Prefab이 연결되지 않았습니다.", this);
                return;
            }

            int currentLevel = currentFacility != null ? currentFacility.GetCurrentLevel() : 0;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeSO recipe = recipes[i];

                if (recipe == null)
                    continue;

                FacilityCraftRecipeRowUI row = Instantiate(recipeRowPrefab, recipeListRoot);
                row.Set(recipe, currentLevel, inventory, TryCraftRecipe);

                spawnedRows.Add(row);
            }

            DebugLog($"{currentFacilityView} 레시피 Row {spawnedRows.Count}개를 생성했습니다.");
        }

        private IReadOnlyList<RecipeSO> GetCurrentRecipes()
        {
            return currentFacilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench => workbenchRecipes,
                HideoutCameraFacilitySelector.FacilityView.Medical => medicalRecipes,
                _ => null
            };
        }

        private bool TryFindFacility(
            HideoutCameraFacilitySelector.FacilityView facilityView,
            out FacilityBase facility)
        {
            facility = null;

            for (int i = 0; i < facilityBindings.Count; i++)
            {
                FacilityViewBinding binding = facilityBindings[i];

                if (binding == null)
                    continue;

                if (binding.facilityView != facilityView)
                    continue;

                facility = binding.facility;
                return facility != null;
            }

            return false;
        }

        private void ClearRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                if (spawnedRows[i] != null)
                    Destroy(spawnedRows[i].gameObject);
            }

            spawnedRows.Clear();
        }

        private void ClearTexts()
        {
            if (titleText != null)
                titleText.text = string.Empty;

            if (descriptionText != null)
                descriptionText.text = string.Empty;

            if (levelText != null)
                levelText.text = string.Empty;

            if (messageText != null)
                messageText.text = string.Empty;
        }

        private void SetMessage(string message)
        {
            if (messageText != null)
                messageText.text = message;

            DebugLog(message);
        }

        private bool CanUseCraftWindow(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView == HideoutCameraFacilitySelector.FacilityView.Workbench ||
                   facilityView == HideoutCameraFacilitySelector.FacilityView.Medical;
        }

        private string GetTitleText(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench => "총기 작업대 제작",
                HideoutCameraFacilitySelector.FacilityView.Medical => "의료시설 제작",
                _ => "아이템 제작"
            };
        }

        private string GetDescriptionText(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            return facilityView switch
            {
                HideoutCameraFacilitySelector.FacilityView.Workbench =>
                    "시설 레벨에 따라 총기 제작 목록이 표시됩니다.",

                HideoutCameraFacilitySelector.FacilityView.Medical =>
                    "시설 레벨에 따라 의료품 제작 목록이 표시됩니다.",

                _ => string.Empty
            };
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[FacilityCraftWindowUI] {message}", this);
        }
    }
}
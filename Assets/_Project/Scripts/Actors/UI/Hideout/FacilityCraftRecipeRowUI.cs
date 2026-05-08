using System;
using System.Collections.Generic;

using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors.UI.Hideout
{
    // 제작 레시피 한 줄 UI
    // 재료 슬롯, 결과 슬롯, 제작 상태 버튼을 카드형으로 표시
    [DisallowMultipleComponent]
    public sealed class FacilityCraftRecipeRowUI : MonoBehaviour
    {
        [Header("레벨")]
        [SerializeField] private TMP_Text levelText;

        [Header("재료 슬롯")]
        [SerializeField] private Transform ingredientSlotRoot;
        [SerializeField] private FacilityCraftItemSlotUI itemSlotPrefab;

        [Header("화살표")]
        [SerializeField] private TMP_Text arrowText;

        [Header("결과 슬롯")]
        [SerializeField] private Transform resultSlotRoot;

        [Header("제작 버튼")]
        [SerializeField] private Button craftButton;
        [SerializeField] private TMP_Text craftButtonText;

        [Header("색상")]
        [SerializeField] private Color craftableColor = Color.white;
        [SerializeField] private Color lockedColor = Color.gray;

        private readonly List<FacilityCraftItemSlotUI> spawnedSlots = new();

        private RecipeSO recipe;
        private Action<RecipeSO> onCraftRequested;

        public void Set(
            RecipeSO targetRecipe,
            int currentFacilityLevel,
            IInventory inventory,
            Action<RecipeSO> craftRequestCallback)
        {
            ClearSlots();

            recipe = targetRecipe;
            onCraftRequested = craftRequestCallback;

            if (recipe == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            int requiredLevel = Mathf.Max(1, recipe.requiredFacilityLevel);
            bool levelSatisfied = currentFacilityLevel >= requiredLevel;
            bool materialSatisfied = HasAllMaterials(recipe, inventory);
            bool canCraft = levelSatisfied && materialSatisfied;

            if (levelText != null)
            {
                levelText.text = $"LV{requiredLevel}";
                levelText.color = canCraft ? craftableColor : lockedColor;
            }

            if (arrowText != null)
            {
                arrowText.text = "→";
                arrowText.color = canCraft ? craftableColor : lockedColor;
            }

            CreateIngredientSlots(recipe, inventory);
            CreateResultSlot(recipe);

            SetCraftButton(levelSatisfied, materialSatisfied, canCraft);
        }

        private void SetCraftButton(bool levelSatisfied, bool materialSatisfied, bool canCraft)
        {
            if (craftButtonText != null)
            {
                if (!levelSatisfied)
                    craftButtonText.text = "레벨 부족";
                else if (!materialSatisfied)
                    craftButtonText.text = "재료 부족";
                else
                    craftButtonText.text = "제작 가능";
            }

            if (craftButton != null)
            {
                craftButton.onClick.RemoveAllListeners();
                craftButton.interactable = canCraft;

                if (canCraft)
                    craftButton.onClick.AddListener(RequestCraft);
            }
        }

        private void CreateIngredientSlots(RecipeSO targetRecipe, IInventory inventory)
        {
            if (ingredientSlotRoot == null || itemSlotPrefab == null)
                return;

            if (targetRecipe.ingredients == null || targetRecipe.ingredients.Count == 0)
                return;

            for (int i = 0; i < targetRecipe.ingredients.Count; i++)
            {
                ItemRequirement requirement = targetRecipe.ingredients[i];

                if (requirement.item == null)
                    continue;

                int ownedCount = 0;

                if (inventory != null && !string.IsNullOrWhiteSpace(requirement.item.itemID))
                    ownedCount = inventory.GetItemCount(requirement.item.itemID);

                FacilityCraftItemSlotUI slot = Instantiate(itemSlotPrefab, ingredientSlotRoot);
                slot.SetIngredient(requirement, ownedCount);

                spawnedSlots.Add(slot);
            }
        }

        private void CreateResultSlot(RecipeSO targetRecipe)
        {
            if (resultSlotRoot == null || itemSlotPrefab == null)
                return;

            if (targetRecipe.result == null)
                return;

            FacilityCraftItemSlotUI slot = Instantiate(itemSlotPrefab, resultSlotRoot);
            slot.SetResult(targetRecipe.result, targetRecipe.resultCount);

            spawnedSlots.Add(slot);
        }

        private void RequestCraft()
        {
            if (recipe == null)
                return;

            onCraftRequested?.Invoke(recipe);
        }

        private bool HasAllMaterials(RecipeSO targetRecipe, IInventory inventory)
        {
            if (targetRecipe == null)
                return false;

            if (targetRecipe.ingredients == null || targetRecipe.ingredients.Count == 0)
                return true;

            if (inventory == null)
                return false;

            for (int i = 0; i < targetRecipe.ingredients.Count; i++)
            {
                ItemRequirement requirement = targetRecipe.ingredients[i];

                if (requirement.item == null || string.IsNullOrWhiteSpace(requirement.item.itemID))
                    return false;

                int requiredAmount = Mathf.Max(1, requirement.amount);
                int currentAmount = inventory.GetItemCount(requirement.item.itemID);

                if (currentAmount < requiredAmount)
                    return false;
            }

            return true;
        }

        private void ClearSlots()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null)
                    Destroy(spawnedSlots[i].gameObject);
            }

            spawnedSlots.Clear();
        }

        private void OnDestroy()
        {
            ClearSlots();

            if (craftButton != null)
                craftButton.onClick.RemoveAllListeners();
        }
    }
}
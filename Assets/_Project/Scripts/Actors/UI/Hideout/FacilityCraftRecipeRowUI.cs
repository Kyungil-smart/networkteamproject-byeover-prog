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
    // 시설 레벨은 레벨 충족 여부만 보고, 제작 버튼은 레벨과 재료를 모두 확인해서 처리
    [DisallowMultipleComponent]
    public sealed class FacilityCraftRecipeRowUI : MonoBehaviour
    {
        [Header("레벨")]
        [Tooltip("레시피에 필요한 시설 레벨을 표시합니다. 현재 시설 레벨이 충분하면 재료가 부족해도 활성 색상으로 표시됩니다.")]
        [SerializeField] private TMP_Text levelText;

        [Header("재료 슬롯")]
        [Tooltip("재료 아이템 슬롯들이 생성될 부모 오브젝트입니다.")]
        [SerializeField] private Transform ingredientSlotRoot;

        [Tooltip("재료와 결과 아이템을 표시할 슬롯 프리팹입니다.")]
        [SerializeField] private FacilityCraftItemSlotUI itemSlotPrefab;

        [Header("화살표")]
        [Tooltip("재료에서 결과 아이템으로 이어지는 화살표 텍스트입니다.")]
        [SerializeField] private TMP_Text arrowText;

        [Header("결과 슬롯")]
        [Tooltip("결과 아이템 슬롯이 생성될 부모 오브젝트입니다.")]
        [SerializeField] private Transform resultSlotRoot;

        [Header("제작 버튼")]
        [Tooltip("제작 요청 버튼입니다. 시설 레벨과 재료가 모두 충족될 때만 클릭할 수 있습니다.")]
        [SerializeField] private Button craftButton;

        [Tooltip("제작 버튼에 표시될 상태 텍스트입니다.")]
        [SerializeField] private TMP_Text craftButtonText;

        [Header("색상")]
        [Tooltip("조건이 충족되었을 때 사용할 색상입니다.")]
        [SerializeField] private Color craftableColor = Color.white;

        [Tooltip("조건이 부족할 때 사용할 색상입니다.")]
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

            RefreshLevelText(requiredLevel, levelSatisfied);
            RefreshArrow(levelSatisfied);

            CreateIngredientSlots(recipe, inventory);
            CreateResultSlot(recipe);

            SetCraftButton(levelSatisfied, materialSatisfied, canCraft);
        }

        private void RefreshLevelText(int requiredLevel, bool levelSatisfied)
        {
            if (levelText == null)
                return;

            levelText.text = $"LV{requiredLevel}";
            levelText.color = levelSatisfied ? craftableColor : lockedColor;
        }

        private void RefreshArrow(bool levelSatisfied)
        {
            if (arrowText == null)
                return;

            arrowText.text = "→";
            arrowText.color = levelSatisfied ? craftableColor : lockedColor;
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
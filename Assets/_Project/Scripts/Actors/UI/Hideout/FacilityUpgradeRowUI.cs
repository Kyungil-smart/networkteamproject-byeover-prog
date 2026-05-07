using DeadZone.Core;
using DeadZone.Systems;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI.Hideout
{
    // 시설 업그레이드 한 줄을 표시하고, 버튼 클릭 시 목표 레벨을 전달
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeRowUI : MonoBehaviour
    {
        [Header("기본 UI")]
        [SerializeField]
        private TMP_Text levelText;

        [SerializeField]
        private Transform materialSlotRoot;

        [SerializeField]
        private FacilityUpgradeMaterialSlotUI materialSlotPrefab;

        [SerializeField]
        private Button upgradeButton;

        [SerializeField]
        private TMP_Text upgradeButtonText;

        [Header("상태 표시")]
        [SerializeField]
        private GameObject reachedMarkRoot;

        [SerializeField]
        private GameObject lockedMarkRoot;

        private readonly List<FacilityUpgradeMaterialSlotUI> spawnedSlots = new();

        private int targetLevel;
        private Action<int> upgradeClicked;

        private void Awake()
        {
            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(HandleUpgradeButtonClicked);
                upgradeButton.onClick.AddListener(HandleUpgradeButtonClicked);
            }
        }

        private void OnDestroy()
        {
            if (upgradeButton != null)
                upgradeButton.onClick.RemoveListener(HandleUpgradeButtonClicked);
        }

        public void Set(
            FacilityBase facility,
            int targetLevel,
            FacilityLevel levelData,
            IInventory inventory,
            Action<int> onUpgradeClicked)
        {
            this.targetLevel = targetLevel;
            upgradeClicked = onUpgradeClicked;

            if (facility == null || levelData == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            bool alreadyReached = facility.IsLevelAlreadyReached(targetLevel);
            bool isTargetLevel = facility.IsUpgradeTargetLevel(targetLevel);
            bool canUpgrade = facility.CanUpgradeToLevel(targetLevel, inventory);

            if (levelText != null)
                levelText.text = $"LV{targetLevel}";

            SetMaterials(levelData, inventory);

            if (upgradeButton != null)
                upgradeButton.interactable = canUpgrade;

            if (upgradeButtonText != null)
            {
                if (alreadyReached)
                    upgradeButtonText.text = "완료";
                else if (!isTargetLevel)
                    upgradeButtonText.text = "잠김";
                else
                    upgradeButtonText.text = "업그레이드";
            }

            if (reachedMarkRoot != null)
                reachedMarkRoot.SetActive(alreadyReached);

            if (lockedMarkRoot != null)
                lockedMarkRoot.SetActive(!alreadyReached && !isTargetLevel);
        }

        private void SetMaterials(FacilityLevel levelData, IInventory inventory)
        {
            ClearSlots();

            if (levelData.upgradeMaterials == null)
                return;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement requirement = levelData.upgradeMaterials[i];

                if (requirement.item == null)
                    continue;

                FacilityUpgradeMaterialSlotUI slot = GetOrCreateSlot();

                int requiredAmount = Mathf.Max(1, requirement.amount);
                int ownedAmount = inventory != null
                    ? inventory.GetItemCount(requirement.item.itemID)
                    : 0;

                slot.Set(requirement.item, ownedAmount, requiredAmount);
            }
        }

        private FacilityUpgradeMaterialSlotUI GetOrCreateSlot()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null && !spawnedSlots[i].gameObject.activeSelf)
                    return spawnedSlots[i];
            }

            FacilityUpgradeMaterialSlotUI slot = Instantiate(materialSlotPrefab, materialSlotRoot);
            spawnedSlots.Add(slot);
            return slot;
        }

        private void ClearSlots()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null)
                    spawnedSlots[i].Clear();
            }
        }

        private void HandleUpgradeButtonClicked()
        {
            upgradeClicked?.Invoke(targetLevel);
        }
    }
}
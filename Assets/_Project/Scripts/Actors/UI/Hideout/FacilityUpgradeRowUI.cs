using System;
using System.Collections.Generic;

using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Housing;

namespace DeadZone.Actors.UI.Hideout
{
    // 시설 업그레이드 한 줄 UI
    // 플레이어별 현재 시설 레벨을 기준으로 완료/잠김/업그레이드 가능 상태를 표시
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
            int playerCurrentLevel,
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

            bool alreadyReached = playerCurrentLevel >= targetLevel;
            bool isTargetLevel = targetLevel == playerCurrentLevel + 1;
            bool hasMaterials = HasAllMaterials(levelData, inventory);
            bool canUpgrade = !alreadyReached && isTargetLevel && hasMaterials;

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
                else if (!hasMaterials)
                    upgradeButtonText.text = "재료 부족";
                else
                    upgradeButtonText.text = "업그레이드";
            }

            if (reachedMarkRoot != null)
                reachedMarkRoot.SetActive(alreadyReached);

            if (lockedMarkRoot != null)
                lockedMarkRoot.SetActive(!alreadyReached && !isTargetLevel);
        }

        private bool HasAllMaterials(FacilityLevel levelData, IInventory inventory)
        {
            if (levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            if (inventory == null)
                return false;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement requirement = levelData.upgradeMaterials[i];

                if (requirement.item == null)
                    return false;

                if (string.IsNullOrWhiteSpace(requirement.item.itemID))
                    return false;

                int requiredAmount = Mathf.Max(1, requirement.amount);

                if (!inventory.HasItem(requirement.item.itemID, requiredAmount))
                    return false;
            }

            return true;
        }

        private void SetMaterials(FacilityLevel levelData, IInventory inventory)
        {
            ClearSlots();

            if (levelData == null || levelData.upgradeMaterials == null)
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
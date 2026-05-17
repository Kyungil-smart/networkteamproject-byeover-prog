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
    // 시설 업그레이드 한 줄 UI입니다.
    // 플레이어의 현재 시설 레벨을 기준으로 완료/잠김/업그레이드 가능 상태를 표시합니다.
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeRowUI : MonoBehaviour
    {
        [Header("기본 UI")]
        [Tooltip("해당 행의 업그레이드 목표 레벨을 표시하는 텍스트입니다. 예: LV2, LV3, LV4")]
        [SerializeField]
        private TMP_Text levelText;

        [Tooltip("업그레이드에 필요한 재료 슬롯들이 생성될 부모 오브젝트입니다.")]
        [SerializeField]
        private Transform materialSlotRoot;

        [Tooltip("업그레이드 재료 하나를 표시할 슬롯 프리팹입니다.")]
        [SerializeField]
        private FacilityUpgradeMaterialSlotUI materialSlotPrefab;

        [Tooltip("업그레이드를 요청하는 버튼입니다.")]
        [SerializeField]
        private Button upgradeButton;

        [Tooltip("업그레이드 버튼에 표시되는 상태 텍스트입니다.")]
        [SerializeField]
        private TMP_Text upgradeButtonText;

        [Header("상태 표시")]
        [Tooltip("이미 도달한 레벨일 때 켜지는 완료 표시 오브젝트입니다.")]
        [SerializeField]
        private GameObject reachedMarkRoot;

        [Tooltip("아직 순서가 오지 않은 레벨일 때 켜지는 잠김 표시 오브젝트입니다.")]
        [SerializeField]
        private GameObject lockedMarkRoot;

        [Header("레벨 텍스트 색상")]
        [Tooltip("이미 업그레이드가 완료되어 활성 상태로 표시할 레벨 텍스트 색상입니다.")]
        [SerializeField]
        private Color activeLevelTextColor = Color.white;

        [Tooltip("아직 업그레이드가 완료되지 않아 비활성 상태로 표시할 레벨 텍스트 색상입니다.")]
        [SerializeField]
        private Color inactiveLevelTextColor = Color.gray;

        [Tooltip("재료가 모두 있어 지금 업그레이드 가능한 레벨 텍스트 색상입니다.")]
        [SerializeField]
        private Color upgradeReadyLevelTextColor = Color.white;

        [Tooltip("지금 업그레이드 가능한 버튼 글씨 색상입니다.")]
        [SerializeField]
        private Color upgradeReadyButtonTextColor = Color.white;

        [Tooltip("잠김, 완료, 재료 부족 상태의 버튼 글씨 색상입니다.")]
        [SerializeField]
        private Color inactiveButtonTextColor = new Color(0.55f, 0.65f, 0.75f);

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

            RefreshLevelText(targetLevel, alreadyReached, canUpgrade);
            SetMaterials(levelData, inventory, !isTargetLevel);
            RefreshUpgradeButton(alreadyReached, isTargetLevel, hasMaterials, canUpgrade);
            RefreshStateMarks(alreadyReached, isTargetLevel);
        }

        private void RefreshLevelText(int targetLevel, bool alreadyReached, bool canUpgrade)
        {
            if (levelText == null)
                return;

            levelText.text = $"LV{targetLevel}";
            levelText.color = canUpgrade
                ? upgradeReadyLevelTextColor
                : alreadyReached
                    ? activeLevelTextColor
                    : inactiveLevelTextColor;
            levelText.fontStyle = canUpgrade || alreadyReached
                ? FontStyles.Bold
                : FontStyles.Normal;
            levelText.gameObject.SetActive(true);
        }

        private void RefreshUpgradeButton(
            bool alreadyReached,
            bool isTargetLevel,
            bool hasMaterials,
            bool canUpgrade)
        {
            bool isLocked = !alreadyReached && !isTargetLevel;

            if (upgradeButton != null)
                upgradeButton.interactable = canUpgrade;

            if (upgradeButtonText == null)
                return;

            if (alreadyReached)
                upgradeButtonText.text = "완료";
            else if (isLocked)
                upgradeButtonText.text = "잠김";
            else if (!hasMaterials)
                upgradeButtonText.text = "재료 부족";
            else
                upgradeButtonText.text = "업그레이드";
            upgradeButtonText.color = canUpgrade ? upgradeReadyButtonTextColor : inactiveButtonTextColor;
            upgradeButtonText.fontStyle = canUpgrade ? FontStyles.Bold : FontStyles.Normal;
        }

        private void RefreshStateMarks(bool alreadyReached, bool isTargetLevel)
        {
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

        private void SetMaterials(FacilityLevel levelData, IInventory inventory, bool forceInactive)
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

                slot.Set(requirement.item, ownedAmount, requiredAmount, forceInactive);
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

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    public abstract class FacilityBase : NetworkBehaviour
    {
        [Header("시설 데이터")]
        [SerializeField]
        [Tooltip("시설 타입, 레벨별 업그레이드 재료, 효과 설명을 가진 FacilityDataSO입니다.")]
        protected FacilityDataSO facilityData;

        public NetworkVariable<int> CurrentLevel = new(1);

        public FacilityType Type => facilityData != null ? facilityData.type : default;
        public FacilityDataSO FacilityData => facilityData;

        public override void OnNetworkSpawn()
        {
            CurrentLevel.OnValueChanged += HandleLevelChanged;

            if (IsServer)
                HandleLevelChanged(0, CurrentLevel.Value);
        }

        public override void OnNetworkDespawn()
        {
            CurrentLevel.OnValueChanged -= HandleLevelChanged;
        }

        private void HandleLevelChanged(int oldLevel, int newLevel)
        {
            OnLevelChanged(newLevel);

            if (!IsServer)
                return;

            EventBus.Publish(new FacilityUpgradedEvent
            {
                facilityType = Type,
                newLevel = newLevel
            });
        }

        protected abstract void OnLevelChanged(int newLevel);

        public bool CanSetLevel(int newLevel)
        {
            if (facilityData == null)
                return false;

            if (facilityData.levels == null || facilityData.levels.Length == 0)
                return false;

            return newLevel >= 1 && newLevel <= facilityData.levels.Length;
        }

        public bool TrySetLevelFromServer(int newLevel)
        {
            if (!IsServer)
                return false;

            if (!CanSetLevel(newLevel))
                return false;

            CurrentLevel.Value = newLevel;
            return true;
        }

        public FacilityLevel GetCurrentLevelData()
        {
            if (facilityData == null)
                return null;

            return facilityData.GetLevel(CurrentLevel.Value);
        }

        public FacilityLevel GetNextLevelData()
        {
            if (facilityData == null)
                return null;

            int nextLevel = CurrentLevel.Value + 1;

            if (!CanSetLevel(nextLevel))
                return null;

            return facilityData.GetLevel(nextLevel);
        }

        public FacilityDataSO GetFacilityData()
        {
            return facilityData;
        }

        public int GetCurrentLevel()
        {
            return CurrentLevel.Value;
        }

        public int GetMaxLevel()
        {
            if (facilityData == null || facilityData.levels == null)
                return 0;

            return facilityData.levels.Length;
        }

        public FacilityLevel GetLevelData(int targetLevel)
        {
            if (facilityData == null)
                return null;

            if (!CanSetLevel(targetLevel))
                return null;

            return facilityData.GetLevel(targetLevel);
        }

        public bool IsUpgradeTargetLevel(int targetLevel)
        {
            if (facilityData == null)
                return false;

            if (!CanSetLevel(targetLevel))
                return false;

            return targetLevel == CurrentLevel.Value + 1;
        }

        public bool IsLevelAlreadyReached(int targetLevel)
        {
            return CurrentLevel.Value >= targetLevel;
        }

        public bool CanUpgradeToLevel(int targetLevel, IInventory inventory)
        {
            if (facilityData == null)
                return false;

            if (inventory == null)
                return false;

            if (!IsUpgradeTargetLevel(targetLevel))
                return false;

            FacilityLevel levelData = GetLevelData(targetLevel);

            if (levelData == null)
                return false;

            return HasUpgradeMaterials(levelData, inventory);
        }

        public bool TryUpgradeToLevelFromServer(int targetLevel, IInventory inventory)
        {
            if (!IsServer)
                return false;

            if (!CanUpgradeToLevel(targetLevel, inventory))
                return false;

            FacilityLevel levelData = GetLevelData(targetLevel);

            if (levelData == null)
                return false;

            if (!ConsumeUpgradeMaterials(levelData, inventory))
                return false;

            CurrentLevel.Value = targetLevel;
            return true;
        }

        public bool IsMaxLevel()
        {
            if (facilityData == null || facilityData.levels == null)
                return true;

            return CurrentLevel.Value >= facilityData.levels.Length;
        }

        private bool HasUpgradeMaterials(FacilityLevel levelData, IInventory inventory)
        {
            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null)
                    return false;

                int requiredAmount = Mathf.Max(1, material.amount);

                if (!inventory.HasItem(material.item.itemID, requiredAmount))
                    return false;
            }

            return true;
        }

        private bool ConsumeUpgradeMaterials(FacilityLevel levelData, IInventory inventory)
        {
            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null)
                    return false;

                int requiredAmount = Mathf.Max(1, material.amount);

                if (!inventory.ConsumeItem(material.item.itemID, requiredAmount))
                    return false;
            }

            return true;
        }
    }
}
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 6개 시설 타입의 공통 베이스 클래스이다.
    /// 시설의 현재 레벨은 CurrentLevel이 관리하고,
    /// 레벨별 업그레이드 재료와 설명은 FacilityDataSO가 담당한다.
    /// </summary>
    public abstract class FacilityBase : NetworkBehaviour
    {
        private const int MinFacilityLevel = 1;

        [Header("Facility")]

        [Tooltip("시설의 레벨별 정적 데이터입니다.")]
        [SerializeField] protected FacilityDataSO facilityData;

        [Tooltip("플레이어가 시설과 상호작용할 수 있는 거리입니다.")]
        [SerializeField] protected float interactDistance = 2.5f;

        public NetworkVariable<int> CurrentLevel = new NetworkVariable<int>(MinFacilityLevel);

        public FacilityType Type => facilityData != null ? facilityData.type : default;

        public int CurrentLevelValue => CurrentLevel.Value;

        public override void OnNetworkSpawn()
        {
            CurrentLevel.OnValueChanged += HandleLevelChanged;

            if (IsServer && CurrentLevel.Value < MinFacilityLevel)
                CurrentLevel.Value = MinFacilityLevel;

            OnLevelChanged(CurrentLevel.Value);
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

        [ServerRpc(RequireOwnership = false)]
        public void TryUpgradeServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            ulong requesterClientId = rpcParams.Receive.SenderClientId;

            if (!TryGetRequesterInventory(requesterClientId, out IInventory inventory))
            {
                Debug.LogWarning($"[FacilityBase] 업그레이드를 요청한 플레이어의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}", this);
                return;
            }

            TryUpgradeWithInventory(inventory);
        }

        /// <summary>
        /// 전달받은 인벤토리를 기준으로 시설 업그레이드를 시도한다.
        /// 실제 플레이어 인벤토리와 테스트 인벤토리 모두 이 함수를 통해 같은 업그레이드 규칙을 사용한다.
        /// </summary>
        protected bool TryUpgradeWithInventory(IInventory inventory)
        {
            if (inventory == null)
            {
                Debug.LogWarning("[FacilityBase] 업그레이드에 사용할 인벤토리가 없습니다.", this);
                return false;
            }

            if (facilityData == null)
            {
                Debug.LogWarning("[FacilityBase] FacilityDataSO가 연결되어 있지 않습니다.", this);
                return false;
            }

            int currentLevel = Mathf.Max(MinFacilityLevel, CurrentLevel.Value);
            int nextLevel = currentLevel + 1;

            FacilityLevel nextLevelData = FindLevelData(nextLevel);

            if (nextLevelData == null)
            {
                Debug.LogWarning($"[FacilityBase] 다음 레벨 데이터가 없습니다. Facility: {Type}, CurrentLevel: {currentLevel}, NextLevel: {nextLevel}", this);
                return false;
            }

            if (!HasAllUpgradeMaterials(inventory, nextLevelData))
            {
                Debug.LogWarning($"[FacilityBase] 업그레이드 재료가 부족합니다. Facility: {Type}, NextLevel: {nextLevel}", this);
                return false;
            }

            if (!ConsumeUpgradeMaterials(inventory, nextLevelData))
            {
                Debug.LogWarning($"[FacilityBase] 업그레이드 재료 소모에 실패했습니다. Facility: {Type}, NextLevel: {nextLevel}", this);
                return false;
            }

            ApplyLevel(nextLevel);

            Debug.Log($"[FacilityBase] 시설 업그레이드 성공. Facility: {Type}, NewLevel: {nextLevel}", this);
            return true;
        }

        public FacilityLevel GetCurrentLevelData()
        {
            if (facilityData == null)
                return null;

            return FindLevelData(CurrentLevel.Value);
        }

        public FacilityLevel GetNextLevelData()
        {
            if (facilityData == null)
                return null;

            return FindLevelData(CurrentLevel.Value + 1);
        }

        public bool IsMaxLevel()
        {
            if (facilityData == null)
                return true;

            int maxLevel = GetMaxConfiguredLevel();
            return CurrentLevel.Value >= maxLevel;
        }

        private void ApplyLevel(int newLevel)
        {
            int previousLevel = CurrentLevel.Value;

            CurrentLevel.Value = newLevel;

            if (!IsSpawned)
            {
                OnLevelChanged(newLevel);
                return;
            }

            if (!IsServer)
                return;

            EventBus.Publish(new FacilityUpgradedEvent
            {
                facilityType = Type,
                newLevel = newLevel
            });
        }

        private FacilityLevel FindLevelData(int targetLevel)
        {
            if (facilityData == null)
                return null;

            if (facilityData.levels == null || facilityData.levels.Length == 0)
                return null;

            for (int i = 0; i < facilityData.levels.Length; i++)
            {
                FacilityLevel levelData = facilityData.levels[i];

                if (levelData == null)
                    continue;

                if (levelData.level == targetLevel)
                    return levelData;
            }

            return null;
        }

        private int GetMaxConfiguredLevel()
        {
            if (facilityData == null)
                return MinFacilityLevel;

            if (facilityData.levels == null || facilityData.levels.Length == 0)
                return MinFacilityLevel;

            int maxLevel = MinFacilityLevel;

            for (int i = 0; i < facilityData.levels.Length; i++)
            {
                FacilityLevel levelData = facilityData.levels[i];

                if (levelData == null)
                    continue;

                if (levelData.level > maxLevel)
                    maxLevel = levelData.level;
            }

            return maxLevel;
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

        private bool HasAllUpgradeMaterials(IInventory inventory, FacilityLevel levelData)
        {
            if (inventory == null)
                return false;

            if (levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement requirement = levelData.upgradeMaterials[i];

                if (requirement.item == null)
                    return false;

                int amount = Mathf.Max(1, requirement.amount);

                if (!inventory.HasItem(requirement.item.itemID, amount))
                    return false;
            }

            return true;
        }

        private bool ConsumeUpgradeMaterials(IInventory inventory, FacilityLevel levelData)
        {
            if (inventory == null)
                return false;

            if (levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            List<ItemRequirement> consumedMaterials = new List<ItemRequirement>();

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement requirement = levelData.upgradeMaterials[i];

                if (requirement.item == null)
                {
                    RestoreConsumedMaterials(inventory, consumedMaterials);
                    return false;
                }

                int amount = Mathf.Max(1, requirement.amount);
                bool consumed = inventory.ConsumeItem(requirement.item.itemID, amount);

                if (!consumed)
                {
                    RestoreConsumedMaterials(inventory, consumedMaterials);
                    return false;
                }

                consumedMaterials.Add(requirement);
            }

            return true;
        }

        private void RestoreConsumedMaterials(IInventory inventory, IReadOnlyList<ItemRequirement> consumedMaterials)
        {
            if (inventory == null)
                return;

            if (consumedMaterials == null)
                return;

            for (int i = 0; i < consumedMaterials.Count; i++)
            {
                ItemRequirement requirement = consumedMaterials[i];

                if (requirement.item == null)
                    continue;

                int amount = Mathf.Max(1, requirement.amount);
                inventory.TryAddItem(requirement.item, amount);
            }
        }
    }
}
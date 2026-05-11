using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 시설 업그레이드 요청을 서버에서 처리
    // SO는 업그레이드 재료 기준표만 제공하고, 실제 레벨 변경은 CurrentLevel NetworkVariable로 동기화
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeController : NetworkBehaviour
    {
        [Header("대상 시설")]
        [SerializeField] private FacilityBase targetFacility;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("테스트 전용입니다. 실제 네트워크 플레이에서는 꺼야 합니다.")]
        private bool useTestInventory = false;

        [SerializeField]
        [Tooltip("테스트 전용 인벤토리입니다. 실제 네트워크 플레이에서는 비워도 됩니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("로그")]
        [SerializeField] private bool logUpgradeResult = true;

        private readonly List<ItemRequirement> consumedMaterials = new();

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
        }

        private void FindRequiredComponents()
        {
            if (targetFacility == null)
                targetFacility = GetComponent<FacilityBase>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        public void RequestUpgrade()
        {
            if (targetFacility == null)
            {
                FailUpgrade("업그레이드 대상 시설이 없습니다.");
                return;
            }

            int currentLevel = targetFacility.GetCurrentLevel();
            int nextLevel = currentLevel + 1;

            if (currentLevel >= targetFacility.GetMaxLevel())
            {
                FailUpgrade("이미 최대 레벨입니다.");
                return;
            }

            if (!targetFacility.IsUpgradeTargetLevel(nextLevel))
            {
                FailUpgrade($"LV{nextLevel}은 현재 업그레이드 가능한 레벨이 아닙니다.");
                return;
            }

            if (useTestInventory)
            {
                TryUpgradeWithInventory(testInventory);
                return;
            }

            if (NetworkManager.Singleton == null)
            {
                FailUpgrade("NetworkManager가 없습니다.");
                return;
            }

            if (!NetworkManager.Singleton.IsListening)
            {
                FailUpgrade("네트워크가 시작되지 않았습니다. Host 또는 Client 실행 후 업그레이드해야 합니다.");
                return;
            }

            RequestUpgradeRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestUpgradeRpc(RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            ulong requesterClientId = rpcParams.Receive.SenderClientId;

            if (!TryGetRequesterInventory(requesterClientId, out IInventory inventory))
            {
                FailUpgrade($"업그레이드 요청자의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}");
                return;
            }

            TryUpgradeWithInventory(inventory);
        }

        public bool TryUpgradeWithInventory(IInventory inventory)
        {
            if (!IsServer && !useTestInventory)
            {
                FailUpgrade("시설 업그레이드는 서버에서만 처리할 수 있습니다.");
                return false;
            }

            if (targetFacility == null)
            {
                FailUpgrade("업그레이드 대상 시설이 없습니다.");
                return false;
            }

            if (inventory == null)
            {
                FailUpgrade("업그레이드에 사용할 인벤토리가 없습니다.");
                return false;
            }

            int currentLevel = targetFacility.GetCurrentLevel();
            int nextLevel = currentLevel + 1;

            if (currentLevel >= targetFacility.GetMaxLevel())
            {
                FailUpgrade("이미 최대 레벨입니다.");
                return false;
            }

            FacilityLevel nextLevelData = targetFacility.GetLevelData(nextLevel);

            if (nextLevelData == null)
            {
                FailUpgrade($"LV{nextLevel} 데이터가 FacilityDataSO에 없습니다.");
                return false;
            }

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                FailUpgrade($"LV{nextLevel} 업그레이드 재료가 부족합니다.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                FailUpgrade($"LV{nextLevel} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevel))
            {
                RestoreConsumedMaterials(inventory);
                FailUpgrade($"LV{nextLevel} 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();

            if (logUpgradeResult)
            {
                Debug.Log(
                    $"[FacilityUpgradeController] {targetFacility.name} 업그레이드 성공: LV{currentLevel} → LV{nextLevel}",
                    this);
            }

            return true;
        }

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (targetFacility == null)
                return false;

            if (!IsServer && !useTestInventory)
                return false;

            if (targetFacility.CurrentLevel == null)
                return false;

            targetFacility.CurrentLevel.Value = nextLevel;
            return true;
        }

        private bool HasAllMaterials(IInventory inventory, FacilityLevel levelData)
        {
            if (inventory == null || levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null)
                    return false;

                if (string.IsNullOrWhiteSpace(material.item.itemID))
                    return false;

                int amount = Mathf.Max(1, material.amount);

                if (!inventory.HasItem(material.item.itemID, amount))
                    return false;
            }

            return true;
        }

        private bool ConsumeAllMaterials(IInventory inventory, FacilityLevel levelData)
        {
            consumedMaterials.Clear();

            if (inventory == null || levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null || string.IsNullOrWhiteSpace(material.item.itemID))
                {
                    RestoreConsumedMaterials(inventory);
                    return false;
                }

                int amount = Mathf.Max(1, material.amount);

                if (!inventory.ConsumeItem(material.item.itemID, amount))
                {
                    RestoreConsumedMaterials(inventory);
                    return false;
                }

                consumedMaterials.Add(new ItemRequirement
                {
                    item = material.item,
                    amount = amount
                });
            }

            return true;
        }

        private void RestoreConsumedMaterials(IInventory inventory)
        {
            if (inventory == null)
                return;

            for (int i = 0; i < consumedMaterials.Count; i++)
            {
                ItemRequirement material = consumedMaterials[i];

                if (material.item == null)
                    continue;

                int amount = Mathf.Max(1, material.amount);
                inventory.TryAddItem(material.item, amount);
            }

            consumedMaterials.Clear();
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

            if (inventory != null)
                return true;

            inventory = client.PlayerObject.GetComponentInChildren<IInventory>(true);
            return inventory != null;
        }

        private void FailUpgrade(string reason)
        {
            if (!logUpgradeResult)
                return;

            string facilityName = targetFacility != null ? targetFacility.name : "None";
            Debug.LogWarning($"[FacilityUpgradeController] {facilityName} 업그레이드 실패: {reason}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 실행")]
        private void DebugUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FacilityUpgradeController] 플레이 중에만 업그레이드 테스트를 실행할 수 있습니다.", this);
                return;
            }

            RequestUpgrade();
        }
#endif
    }
}
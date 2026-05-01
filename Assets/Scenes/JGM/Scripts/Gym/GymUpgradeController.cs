using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 헬스장 업그레이드 요청, 재료 검사, 재료 소모, 레벨 증가를 담당합니다.
    /// 실제 Player Inventory가 완성되기 전까지는 WorkbenchTestInventory를 IInventory 대체 구현으로 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public sealed class GymUpgradeController : NetworkBehaviour
    {
        [Header("헬스장 시설")]
        [SerializeField]
        [Tooltip("업그레이드할 헬스장 시설입니다. 비워두면 같은 오브젝트에서 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase gymFacility;

        [SerializeField]
        [Tooltip("헬스장 업그레이드 재료가 들어 있는 Gym_Facility SO입니다.")]
        private FacilityDataSO gymFacilityData;

        [SerializeField]
        [Tooltip("헬스장 레벨 변경 후 소지 무게 보너스를 다시 계산할 컨트롤러입니다.")]
        private GymCarryWeightBonusController carryWeightBonusController;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player Inventory 대신 WorkbenchTestInventory로 업그레이드를 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("Network Host 실행 없이도 테스트용으로 레벨 변경을 허용할지 여부입니다.")]
        private bool allowOfflineTestUpgrade = true;

        [SerializeField]
        [Tooltip("Player Inventory 완성 전까지 사용할 테스트용 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("로그")]
        [SerializeField]
        [Tooltip("업그레이드 성공과 실패 로그를 Console에 출력할지 여부입니다.")]
        private bool logUpgradeResult = true;

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
            if (gymFacility == null)
                gymFacility = GetComponent<FacilityBase>();

            if (carryWeightBonusController == null)
                carryWeightBonusController = GetComponent<GymCarryWeightBonusController>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        public void RequestUpgrade()
        {
            if (useTestInventory)
            {
                TryUpgradeWithInventory(testInventory);
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
                LogWarning($"업그레이드를 요청한 플레이어의 인벤토리를 찾지 못했습니다. ClientId: {requesterClientId}");
                return;
            }

            TryUpgradeWithInventory(inventory);
        }

        public bool CanUpgradeWithInventory(IInventory inventory)
        {
            if (inventory == null)
                return false;

            if (!IsValidGymFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
                return false;

            return CanApplyUpgradeLevel();
        }

        public bool TryUpgradeWithInventory(IInventory inventory)
        {
            if (inventory == null)
            {
                LogWarning("업그레이드에 사용할 인벤토리가 없습니다.");
                return false;
            }

            if (!IsValidGymFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"헬스장 Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                PrintRequiredMaterials(nextLevelData, inventory);
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 헬스장 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"헬스장 Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("헬스장 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();
            carryWeightBonusController?.RefreshBonus(true);

            if (logUpgradeResult)
                Debug.Log($"[GymUpgradeController] 헬스장 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool IsValidGymFacility()
        {
            if (gymFacility == null)
            {
                LogWarning("FacilityBase가 연결되어 있지 않습니다.");
                return false;
            }

            if (gymFacility.Type != FacilityType.Gym)
            {
                LogWarning($"연결된 시설 타입이 Gym이 아닙니다. 현재 타입: {gymFacility.Type}");
                return false;
            }

            if (gymFacilityData == null)
            {
                LogWarning("Gym_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (gymFacilityData.type != FacilityType.Gym)
            {
                LogWarning($"Gym_Facility SO의 타입이 Gym이 아닙니다. 현재 타입: {gymFacilityData.type}");
                return false;
            }

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (gymFacility == null || gymFacilityData == null)
                return false;

            if (gymFacilityData.levels == null || gymFacilityData.levels.Length == 0)
            {
                LogWarning("Gym_Facility SO에 레벨 데이터가 없습니다.");
                return false;
            }

            int nextLevel = gymFacility.CurrentLevel.Value + 1;

            if (nextLevel > gymFacilityData.levels.Length)
            {
                LogWarning("헬스장이 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = gymFacilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"헬스장 Lv.{nextLevel} 데이터가 없습니다.");
                return false;
            }

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
                    amount = amount,
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

                if (material.item == null || material.amount <= 0)
                    continue;

                inventory.TryAddItem(material.item, material.amount);
            }

            consumedMaterials.Clear();
        }

        private bool CanApplyUpgradeLevel()
        {
            if (gymFacility == null)
                return false;

            if (!gymFacility.IsSpawned)
                return allowOfflineTestUpgrade;

            return IsServer;
        }

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (!CanApplyUpgradeLevel())
                return false;

            gymFacility.CurrentLevel.Value = nextLevel;
            return true;
        }

        private bool TryGetRequesterInventory(ulong clientId, out IInventory inventory)
        {
            inventory = null;

            if (NetworkManager.Singleton == null)
                return false;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return false;

            if (client.PlayerObject == null)
                return false;

            return client.PlayerObject.TryGetComponent(out inventory);
        }

        private void PrintRequiredMaterials(FacilityLevel levelData, IInventory inventory)
        {
            if (!logUpgradeResult)
                return;

            if (levelData == null || levelData.upgradeMaterials == null)
                return;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null)
                    continue;

                int requiredAmount = Mathf.Max(1, material.amount);
                int currentAmount = 0;

                if (inventory is WorkbenchTestInventory test)
                    currentAmount = test.GetItemCount(material.item.itemID);

                Debug.Log(
                    $"[GymUpgradeController] 필요 재료: {material.item.displayName}({material.item.itemID}) " +
                    $"필요 {requiredAmount}개 / 현재 {currentAmount}개",
                    this
                );
            }
        }

        private void LogWarning(string message)
        {
            if (!logUpgradeResult)
                return;

            Debug.LogWarning($"[GymUpgradeController] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 실행")]
        private void DebugUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GymUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            RequestUpgrade();
        }

        [ContextMenu("업그레이드 가능 여부 출력")]
        private void DebugCanUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GymUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            bool canUpgrade = useTestInventory && CanUpgradeWithInventory(testInventory);
            Debug.Log($"[GymUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
        }
#endif
    }
}

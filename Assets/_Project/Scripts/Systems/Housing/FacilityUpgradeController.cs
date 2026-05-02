using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 모든 하우징 시설의 공통 업그레이드를 처리합니다.
    /// 재료 검사, 재료 소모, 레벨 증가만 담당하고 시설별 효과는 각 Controller가 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public sealed class FacilityUpgradeController : NetworkBehaviour
    {
        [Header("시설")]
        [SerializeField]
        [Tooltip("업그레이드할 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private FacilityBase targetFacility;

        [SerializeField]
        [Tooltip("시설 업그레이드 레벨과 재료 정보가 들어 있는 FacilityDataSO입니다.")]
        private FacilityDataSO facilityData;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player Inventory 대신 테스트 인벤토리로 업그레이드를 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("Host 실행 없이도 Play Mode에서 레벨 변경 테스트를 허용합니다.")]
        private bool allowOfflineTestUpgrade = true;

        [SerializeField]
        [Tooltip("UI, Player Inventory 완성 전까지 사용할 테스트용 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("로그")]
        [SerializeField]
        [Tooltip("업그레이드 성공과 실패 로그를 Console에 출력합니다.")]
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
            if (targetFacility == null)
                targetFacility = GetComponent<FacilityBase>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        /// <summary>
        /// UI 버튼, 테스트 버튼, 상호작용 시스템에서 호출할 업그레이드 진입점입니다.
        /// </summary>
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

            if (!IsValidFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
                return false;

            return CanApplyUpgradeLevel();
        }

        public bool TryUpgradeWithInventory(IInventory inventory)
        {
            consumedMaterials.Clear();

            if (inventory == null)
            {
                LogWarning("업그레이드에 사용할 인벤토리가 없습니다.");
                return false;
            }

            if (!IsValidFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"{GetFacilityName()} Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                PrintRequiredMaterials(nextLevelData, inventory);
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 시설 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"{GetFacilityName()} Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("시설 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();
            NotifyDependentControllers();

            if (logUpgradeResult)
                Debug.Log($"[FacilityUpgradeController] {GetFacilityName()} 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool IsValidFacility()
        {
            if (targetFacility == null)
            {
                LogWarning("FacilityBase가 연결되어 있지 않습니다.");
                return false;
            }

            if (facilityData == null)
            {
                LogWarning("FacilityDataSO가 연결되어 있지 않습니다.");
                return false;
            }

            if (facilityData.type != targetFacility.Type)
            {
                LogWarning($"시설 타입과 FacilityDataSO 타입이 다릅니다. 시설: {targetFacility.Type}, SO: {facilityData.type}");
                return false;
            }

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (targetFacility == null || facilityData == null)
                return false;

            if (facilityData.levels == null || facilityData.levels.Length == 0)
            {
                LogWarning("FacilityDataSO에 레벨 데이터가 없습니다.");
                return false;
            }

            int currentLevel = Mathf.Max(1, targetFacility.CurrentLevel.Value);
            int nextLevel = currentLevel + 1;

            if (nextLevel > facilityData.levels.Length)
            {
                LogWarning($"{GetFacilityName()}은 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = facilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"{GetFacilityName()} Lv.{nextLevel} 데이터가 없습니다.");
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

                if (!IsValidMaterial(material))
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

                if (!IsValidMaterial(material))
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

        private bool IsValidMaterial(ItemRequirement material)
        {
            if (material.item == null)
            {
                LogWarning("업그레이드 재료 ItemDataSO가 비어 있습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(material.item.itemID))
            {
                LogWarning($"{material.item.name}의 itemID가 비어 있습니다.");
                return false;
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
            if (targetFacility == null)
                return false;

            if (!targetFacility.IsSpawned)
                return allowOfflineTestUpgrade;

            return IsServer;
        }

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (!CanApplyUpgradeLevel())
                return false;

            targetFacility.CurrentLevel.Value = nextLevel;
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

        private void NotifyDependentControllers()
        {
            // 현재 시설별 효과 컨트롤러들이 RefreshBonus / RefreshSize / RefreshRuntimeState 이름을 사용하고 있어
            // 공통 업그레이드 후 강제로 다시 계산할 수 있게 보조합니다.
            gameObject.SendMessage("RefreshBonus", true, SendMessageOptions.DontRequireReceiver);
            gameObject.SendMessage("RefreshSize", true, SendMessageOptions.DontRequireReceiver);
            gameObject.SendMessage("RefreshRuntimeState", true, SendMessageOptions.DontRequireReceiver);
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
                string currentAmountText = "확인 불가";

                if (inventory is WorkbenchTestInventory testInventory)
                    currentAmountText = testInventory.GetItemCount(material.item.itemID).ToString();

                Debug.Log(
                    $"[FacilityUpgradeController] 필요 재료: {material.item.displayName}({material.item.itemID}) " +
                    $"필요 {requiredAmount}개 / 현재 {currentAmountText}개",
                    this
                );
            }
        }

        private string GetFacilityName()
        {
            if (facilityData != null)
                return facilityData.type.ToString();

            if (targetFacility != null)
                return targetFacility.Type.ToString();

            return "Unknown Facility";
        }

        private void LogWarning(string message)
        {
            if (!logUpgradeResult)
                return;

            Debug.LogWarning($"[FacilityUpgradeController] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 실행")]
        private void DebugUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FacilityUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            RequestUpgrade();
        }

        [ContextMenu("업그레이드 가능 여부 출력")]
        private void DebugCanUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FacilityUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            bool canUpgrade = useTestInventory && CanUpgradeWithInventory(testInventory);
            Debug.Log($"[FacilityUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
        }
#endif
    }
}
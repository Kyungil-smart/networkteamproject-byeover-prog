using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 보관함 업그레이드 요청, 재료 검사, 재료 소모, 레벨 증가를 담당
    // 실제 Player Inventory가 완성되기 전까지는 WorkbenchTestInventory를 IInventory 대체 구현으로 사용
    [DisallowMultipleComponent]
    public sealed class StashUpgradeController : NetworkBehaviour
    {
        [Header("보관함 시설")]
        [SerializeField]
        [Tooltip("업그레이드할 보관함 시설입니다. 비워두면 같은 오브젝트에서 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase stashFacility;

        [SerializeField]
        [Tooltip("보관함 업그레이드 재료가 들어 있는 Stash_Facility SO입니다.")]
        private FacilityDataSO stashFacilityData;

        [SerializeField]
        [Tooltip("보관함 레벨 변경 후 스태쉬 크기를 다시 계산할 컨트롤러입니다.")]
        private StashSizeController sizeController;

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

        private readonly List<ItemRequirement> consumedMaterials = new List<ItemRequirement>();

        private bool useOfflineCurrentLevel;
        private int offlineCurrentLevel = 1;

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
            if (stashFacility == null)
                stashFacility = GetComponent<FacilityBase>();

            if (sizeController == null)
                sizeController = GetComponent<StashSizeController>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();
        }

        public void RequestUpgrade()
        {
            if (useTestInventory)
            {
                TryUpgradeWithInventory(testInventory, GetLocalRequesterClientId());
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

            TryUpgradeWithInventory(inventory, requesterClientId);
        }

        public bool CanUpgradeWithInventory(IInventory inventory)
        {
            if (inventory == null)
                return false;

            if (!IsValidStashFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
                return false;

            return CanApplyUpgradeLevel();
        }

        public bool TryUpgradeWithInventory(IInventory inventory, ulong? requesterClientId = null)
        {
            if (inventory == null)
            {
                LogWarning("업그레이드에 사용할 인벤토리가 없습니다.");
                return false;
            }

            if (!IsValidStashFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"보관함 Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                PrintRequiredMaterials(nextLevelData, inventory);
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 보관함 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"보관함 Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("보관함 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();
            sizeController?.RefreshSize(true);
            SyncRequesterHousingProgress(requesterClientId, nextLevelData.level);

            if (logUpgradeResult)
                Debug.Log($"[StashUpgradeController] 보관함 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool IsValidStashFacility()
        {
            if (stashFacility == null)
            {
                LogWarning("FacilityBase가 연결되어 있지 않습니다.");
                return false;
            }

            if (stashFacility.Type != FacilityType.Stash)
            {
                LogWarning($"연결된 시설 타입이 Stash가 아닙니다. 현재 타입: {stashFacility.Type}");
                return false;
            }

            if (stashFacilityData == null)
            {
                LogWarning("Stash_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (stashFacilityData.type != FacilityType.Stash)
            {
                LogWarning($"Stash_Facility SO의 타입이 Stash가 아닙니다. 현재 타입: {stashFacilityData.type}");
                return false;
            }

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (stashFacility == null || stashFacilityData == null)
                return false;

            if (stashFacilityData.levels == null || stashFacilityData.levels.Length == 0)
            {
                LogWarning("Stash_Facility SO에 레벨 데이터가 없습니다.");
                return false;
            }

            int currentLevel = GetCurrentStashLevel();
            int nextLevel = currentLevel + 1;

            if (nextLevel > stashFacilityData.levels.Length)
            {
                LogWarning("보관함이 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = stashFacilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"보관함 Lv.{nextLevel} 데이터가 없습니다.");
                return false;
            }

            return true;
        }

        private int GetCurrentStashLevel()
        {
            if (ShouldUseOfflineLevel())
            {
                if (!useOfflineCurrentLevel)
                {
                    offlineCurrentLevel = GetSafeCurrentLevel();
                    useOfflineCurrentLevel = true;
                }

                return Mathf.Max(1, offlineCurrentLevel);
            }

            return GetSafeCurrentLevel();
        }

        private int GetSafeCurrentLevel()
        {
            if (stashFacility == null)
                return 1;

            return Mathf.Max(1, stashFacility.CurrentLevel.Value);
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
            if (stashFacility == null)
                return false;

            if (!stashFacility.IsSpawned)
                return allowOfflineTestUpgrade;

            return IsServer;
        }

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (!CanApplyUpgradeLevel())
                return false;

            if (!stashFacility.IsSpawned)
            {
                offlineCurrentLevel = Mathf.Clamp(nextLevel, 1, 4);
                useOfflineCurrentLevel = true;
                sizeController?.SetOfflineTestLevel(offlineCurrentLevel);
                return true;
            }

            stashFacility.CurrentLevel.Value = nextLevel;
            return true;
        }

        private static ulong? GetLocalRequesterClientId()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId
                : null;
        }

        private void SyncRequesterHousingProgress(ulong? requesterClientId, int nextLevel)
        {
            if (!IsServer || !requesterClientId.HasValue)
                return;

            if (!PlayerHousingProgressResolver.TryGetProgress(requesterClientId.Value, out PlayerHousingProgress progress))
                return;

            if (!progress.TrySetLevelFromServer(FacilityType.Stash, nextLevel))
                return;

            PlayerHousingSaveSyncer saveSyncer = progress.GetComponent<PlayerHousingSaveSyncer>();
            if (saveSyncer != null)
                saveSyncer.RequestSaveFromServer("Stash 시설 업그레이드");
        }

        private bool ShouldUseOfflineLevel()
        {
            return allowOfflineTestUpgrade && stashFacility != null && !stashFacility.IsSpawned;
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
                    $"[StashUpgradeController] 필요 재료: {material.item.displayName}({material.item.itemID}) " +
                    $"필요 {requiredAmount}개 / 현재 {currentAmount}개",
                    this
                );
            }
        }

        private void LogWarning(string message)
        {
            if (!logUpgradeResult)
                return;

            Debug.LogWarning($"[StashUpgradeController] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 실행")]
        private void DebugUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            RequestUpgrade();
        }

        [ContextMenu("업그레이드 가능 여부 출력")]
        private void DebugCanUpgrade()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashUpgradeController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            bool canUpgrade = useTestInventory && CanUpgradeWithInventory(testInventory);
            Debug.Log($"[StashUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
        }
#endif
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 작업대 업그레이드 재료 검사, 재료 소모, 시설 레벨 증가를 담당합니다.
    /// Facilities.cs의 Workbench는 수정하지 않고, 작업대 업그레이드 기능만 별도 컨트롤러로 분리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Workbench))]
    public class WorkbenchUpgradeController : NetworkBehaviour
    {
        [Header("작업대")]
        [SerializeField]
        [Tooltip("업그레이드할 Workbench 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private Workbench workbench;

        [SerializeField]
        [Tooltip("Workbench에 연결된 것과 같은 Workbench_Facility SO를 넣습니다.")]
        private FacilityDataSO workbenchFacilityData;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player 인벤토리 대신 WorkbenchTestInventory로 업그레이드를 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("에디터 테스트 중 Host 없이 작업대 레벨 변경을 허용할지 여부입니다.")]
        private bool allowOfflineTestUpgrade = true;

        [SerializeField]
        [Tooltip("플레이어 인벤토리 완성 전까지 사용할 테스트용 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("로그")]
        [SerializeField]
        [Tooltip("업그레이드 성공/실패 로그를 Console에 출력할지 여부입니다.")]
        private bool logUpgradeResult = true;

        private readonly List<ItemRequirement> consumedMaterials = new List<ItemRequirement>();

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
            if (workbench == null)
                workbench = GetComponent<Workbench>();

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

            if (workbench == null)
            {
                LogWarning("Workbench가 연결되어 있지 않습니다.");
                return false;
            }

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"작업대 Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 작업대 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"작업대 Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("작업대 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();

            if (logUpgradeResult)
                Debug.Log($"[WorkbenchUpgradeController] 작업대 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (workbench == null)
            {
                LogWarning("Workbench가 없습니다.");
                return false;
            }

            if (workbenchFacilityData == null)
            {
                LogWarning("Workbench_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (workbenchFacilityData.levels == null || workbenchFacilityData.levels.Length == 0)
            {
                LogWarning("Workbench_Facility SO에 레벨 데이터가 없습니다.");
                return false;
            }

            int currentLevel = workbench.CurrentLevel.Value;
            int nextLevel = currentLevel + 1;

            if (nextLevel > workbenchFacilityData.levels.Length)
            {
                LogWarning("작업대가 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = workbenchFacilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"작업대 Lv.{nextLevel} 데이터가 없습니다.");
                return false;
            }

            return true;
        }

        private bool HasAllMaterials(IInventory inventory, FacilityLevel levelData)
        {
            if (inventory == null)
                return false;

            if (levelData == null)
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

            if (inventory == null)
                return false;

            if (levelData == null)
                return false;

            if (levelData.upgradeMaterials == null || levelData.upgradeMaterials.Count == 0)
                return true;

            for (int i = 0; i < levelData.upgradeMaterials.Count; i++)
            {
                ItemRequirement material = levelData.upgradeMaterials[i];

                if (material.item == null)
                {
                    RestoreConsumedMaterials(inventory);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(material.item.itemID))
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

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (workbench == null)
                return false;

            if (IsServer)
            {
                workbench.CurrentLevel.Value = nextLevel;
                return true;
            }

#if UNITY_EDITOR
            if (CanUseOfflineTestUpgrade())
            {
                workbench.CurrentLevel.Value = nextLevel;

                if (logUpgradeResult)
                {
                    Debug.Log(
                        $"[WorkbenchUpgradeController] 오프라인 테스트 모드로 작업대 레벨을 변경했습니다. Lv.{nextLevel}",
                        this
                    );
                }

                return true;
            }
#endif

            LogWarning("작업대 레벨 변경은 서버에서만 가능합니다. Host 모드로 실행했는지 확인하세요.");
            return false;
        }

        private bool CanApplyUpgradeLevel()
        {
            if (IsServer)
                return true;

#if UNITY_EDITOR
            return CanUseOfflineTestUpgrade();
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private bool CanUseOfflineTestUpgrade()
        {
            if (!allowOfflineTestUpgrade)
                return false;

            if (NetworkManager.Singleton == null)
                return true;

            return !NetworkManager.Singleton.IsListening;
        }
#endif

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

        private void LogWarning(string message)
        {
            if (!logUpgradeResult)
                return;

            Debug.LogWarning($"[WorkbenchUpgradeController] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 가능 여부 확인")]
        private void DebugCanUpgrade()
        {
            if (!Application.isPlaying)
            {
                LogWarning("플레이 중에만 업그레이드 테스트를 실행할 수 있습니다.");
                return;
            }

            bool canUpgrade = CanUpgradeWithInventory(testInventory);
            Debug.Log($"[WorkbenchUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
        }

        [ContextMenu("디버그 업그레이드 실행")]
        private void DebugUpgrade()
        {
            if (!Application.isPlaying)
            {
                LogWarning("플레이 중에만 업그레이드 테스트를 실행할 수 있습니다.");
                return;
            }

            RequestUpgrade();
        }
#endif
    }
}
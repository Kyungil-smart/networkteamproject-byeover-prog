using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 의료시설 업그레이드 요청, 재료 검사, 재료 소모, 레벨 증가를 담당합니다.
    /// 실제 Player 인벤토리가 완성되기 전에는 WorkbenchTestInventory를 IInventory 테스트 구현체로 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MedicalFacility))]
    public class MedicalUpgradeController : NetworkBehaviour
    {
        [Header("의료시설")]
        [SerializeField]
        [Tooltip("업그레이드할 의료시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private MedicalFacility medicalFacility;

        [SerializeField]
        [Tooltip("MedicalFacility에 연결된 것과 같은 Medical_Facility SO를 넣습니다.")]
        private FacilityDataSO medicalFacilityData;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player 인벤토리 대신 WorkbenchTestInventory로 업그레이드를 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("Host 실행 없이 에디터 테스트 중에도 의료시설 레벨 변경을 허용할지 여부입니다.")]
        private bool allowOfflineTestUpgrade = true;

        [SerializeField]
        [Tooltip("실제 인벤토리 완성 전까지 사용할 테스트 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("연동")]
        [SerializeField]
        [Tooltip("업그레이드 후 체력 보너스를 즉시 다시 계산할 컨트롤러입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private MedicalHealthBonusController healthBonusController;

        [Header("로그")]
        [SerializeField]
        [Tooltip("업그레이드 성공/실패 로그를 Console에 출력할지 여부입니다.")]
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
            if (medicalFacility == null)
                medicalFacility = GetComponent<MedicalFacility>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();

            if (healthBonusController == null)
                healthBonusController = GetComponent<MedicalHealthBonusController>();
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

            if (!IsValidMedicalFacility())
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

            if (!IsValidMedicalFacility())
                return false;

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"의료시설 Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 의료시설 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"의료시설 Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("의료시설 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();

            healthBonusController?.RecalculateBonusForTest();

            if (logUpgradeResult)
                Debug.Log($"[MedicalUpgradeController] 의료시설 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool IsValidMedicalFacility()
        {
            if (medicalFacility == null)
            {
                LogWarning("MedicalFacility가 연결되어 있지 않습니다.");
                return false;
            }

            if (medicalFacility.Type != FacilityType.Medical)
            {
                LogWarning($"연결된 시설 타입이 Medical이 아닙니다. 현재 타입: {medicalFacility.Type}");
                return false;
            }

            if (medicalFacilityData == null)
            {
                LogWarning("Medical_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (medicalFacilityData.type != FacilityType.Medical)
            {
                LogWarning($"Medical_Facility SO의 타입이 Medical이 아닙니다. 현재 타입: {medicalFacilityData.type}");
                return false;
            }

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (medicalFacility == null)
            {
                LogWarning("의료시설이 없습니다.");
                return false;
            }

            if (medicalFacilityData == null)
            {
                LogWarning("Medical_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (medicalFacilityData.levels == null || medicalFacilityData.levels.Length == 0)
            {
                LogWarning("Medical_Facility SO에 레벨 데이터가 없습니다.");
                return false;
            }

            int currentLevel = medicalFacility.CurrentLevel.Value;
            int nextLevel = currentLevel + 1;

            if (nextLevel > medicalFacilityData.levels.Length)
            {
                LogWarning("의료시설은 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = medicalFacilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"의료시설 Lv.{nextLevel} 데이터가 없습니다.");
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

        private bool ApplyUpgradeLevel(int nextLevel)
        {
            if (medicalFacility == null)
                return false;

            if (IsServer)
            {
                medicalFacility.CurrentLevel.Value = nextLevel;
                return true;
            }

#if UNITY_EDITOR
            if (CanUseOfflineTestUpgrade())
            {
                medicalFacility.CurrentLevel.Value = nextLevel;

                if (logUpgradeResult)
                    Debug.Log($"[MedicalUpgradeController] 오프라인 테스트 모드로 의료시설 레벨을 변경했습니다. Lv.{nextLevel}", this);

                return true;
            }
#endif

            LogWarning("의료시설 레벨 변경은 서버에서만 가능합니다. Host 모드로 실행했는지 확인하세요.");
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

            Debug.LogWarning($"[MedicalUpgradeController] {message}", this);
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
            Debug.Log($"[MedicalUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
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

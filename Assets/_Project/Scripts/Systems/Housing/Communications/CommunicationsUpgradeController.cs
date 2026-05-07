using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 통신장비 업그레이드 조건 검사, 재료 소모, 레벨 적용을 담당
    // 퀘스트 완료 조건은 CommunicationsQuestUnlockProvider에 위임하고, 인벤토리는 IInventory로만 다룹니다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CommunicationsFacility))]
    public class CommunicationsUpgradeController : NetworkBehaviour
    {
        [Header("통신장비")]
        [SerializeField]
        [Tooltip("업그레이드할 통신장비 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private CommunicationsFacility communicationsFacility;

        [SerializeField]
        [Tooltip("Communications_Facility SO를 넣습니다. Type은 CommStation이어야 합니다.")]
        private FacilityDataSO communicationsFacilityData;

        [SerializeField]
        [Tooltip("통신장비 업그레이드 전에 필요한 퀘스트 완료 조건을 검사하는 컴포넌트입니다.")]
        private CommunicationsQuestUnlockProvider questUnlockProvider;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("체크하면 실제 Player 인벤토리 대신 WorkbenchTestInventory로 업그레이드를 테스트합니다.")]
        private bool useTestInventory = true;

        [SerializeField]
        [Tooltip("에디터 테스트 중 Host 없이 통신장비 레벨 변경을 허용할지 여부입니다.")]
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
            if (communicationsFacility == null)
                communicationsFacility = GetComponent<CommunicationsFacility>();

            if (questUnlockProvider == null)
                questUnlockProvider = GetComponent<CommunicationsQuestUnlockProvider>();

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

            if (!IsValidCommunicationsFacility())
                return false;

            if (!CanPassQuestCondition(out int nextLevelByQuest, out string failReason))
            {
                LogWarning(failReason);
                return false;
            }

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (nextLevelByQuest != nextLevelData.level)
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

            if (!IsValidCommunicationsFacility())
                return false;

            if (!CanPassQuestCondition(out int nextLevelByQuest, out string failReason))
            {
                LogWarning(failReason);
                return false;
            }

            if (!TryGetNextLevelData(out FacilityLevel nextLevelData))
                return false;

            if (nextLevelByQuest != nextLevelData.level)
            {
                LogWarning($"퀘스트 조건의 다음 레벨({nextLevelByQuest})과 시설 데이터의 다음 레벨({nextLevelData.level})이 다릅니다.");
                return false;
            }

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"통신장비 Lv.{nextLevelData.level} 업그레이드 재료가 부족합니다.");
                return false;
            }

            if (!CanApplyUpgradeLevel())
            {
                LogWarning("현재 실행 상태에서는 통신장비 레벨을 변경할 수 없습니다. Host 실행 또는 Offline Test 허용 여부를 확인하세요.");
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                LogWarning($"통신장비 Lv.{nextLevelData.level} 업그레이드 재료 소모에 실패했습니다.");
                return false;
            }

            if (!ApplyUpgradeLevel(nextLevelData.level))
            {
                RestoreConsumedMaterials(inventory);
                LogWarning("통신장비 레벨 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.");
                return false;
            }

            consumedMaterials.Clear();

            if (logUpgradeResult)
                Debug.Log($"[CommunicationsUpgradeController] 통신장비 업그레이드 성공: Lv.{nextLevelData.level}", this);

            return true;
        }

        private bool CanPassQuestCondition(out int nextLevel, out string failReason)
        {
            nextLevel = 0;
            failReason = string.Empty;

            if (questUnlockProvider == null)
            {
                failReason = "CommunicationsQuestUnlockProvider가 연결되어 있지 않아 퀘스트 완료 조건을 확인할 수 없습니다.";
                return false;
            }

            return questUnlockProvider.CanUpgradeToNextLevel(out nextLevel, out failReason);
        }

        private bool IsValidCommunicationsFacility()
        {
            if (communicationsFacility == null)
            {
                LogWarning("CommunicationsFacility가 연결되어 있지 않습니다.");
                return false;
            }

            if (communicationsFacility.Type != FacilityType.CommStation)
            {
                LogWarning($"연결된 시설 타입이 CommStation이 아닙니다. 현재 타입: {communicationsFacility.Type}");
                return false;
            }

            if (communicationsFacilityData == null)
            {
                LogWarning("Communications_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (communicationsFacilityData.type != FacilityType.CommStation)
            {
                LogWarning($"Communications_Facility SO 타입이 CommStation이 아닙니다. 현재 타입: {communicationsFacilityData.type}");
                return false;
            }

            return true;
        }

        private bool TryGetNextLevelData(out FacilityLevel nextLevelData)
        {
            nextLevelData = null;

            if (communicationsFacility == null)
            {
                LogWarning("통신장비 시설이 없습니다.");
                return false;
            }

            if (communicationsFacilityData == null)
            {
                LogWarning("Communications_Facility SO가 연결되어 있지 않습니다.");
                return false;
            }

            if (communicationsFacilityData.levels == null || communicationsFacilityData.levels.Length == 0)
            {
                LogWarning("Communications_Facility SO에 레벨 데이터가 없습니다.");
                return false;
            }

            int currentLevel = communicationsFacility.CurrentLevel.Value;
            int nextLevel = currentLevel + 1;

            if (nextLevel > communicationsFacilityData.levels.Length)
            {
                LogWarning("통신장비가 이미 최대 레벨입니다.");
                return false;
            }

            nextLevelData = communicationsFacilityData.GetLevel(nextLevel);

            if (nextLevelData == null)
            {
                LogWarning($"통신장비 Lv.{nextLevel} 데이터가 없습니다.");
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
            if (communicationsFacility == null)
                return false;

            if (IsServer)
            {
                communicationsFacility.CurrentLevel.Value = nextLevel;
                return true;
            }

#if UNITY_EDITOR
            if (CanUseOfflineTestUpgrade())
            {
                communicationsFacility.CurrentLevel.Value = nextLevel;

                if (logUpgradeResult)
                {
                    Debug.Log(
                        $"[CommunicationsUpgradeController] 오프라인 테스트 모드로 통신장비 레벨을 변경했습니다. Lv.{nextLevel}",
                        this
                    );
                }

                return true;
            }
#endif

            LogWarning("통신장비 레벨 변경은 서버에서만 가능합니다. Host 모드로 실행했는지 확인하세요.");
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

            Debug.LogWarning($"[CommunicationsUpgradeController] {message}", this);
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
            Debug.Log($"[CommunicationsUpgradeController] 업그레이드 가능 여부: {canUpgrade}", this);
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

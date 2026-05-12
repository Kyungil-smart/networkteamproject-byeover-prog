using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 시설 업그레이드 요청을 서버에서 처리
    // 시설 오브젝트의 공용 레벨이 아니라, 요청한 플레이어의 개인 하우징 레벨을 변경
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeController : NetworkBehaviour
    {
        [Header("대상 시설")]
        [SerializeField]
        private FacilityBase targetFacility;

        [Header("로그")]
        [SerializeField]
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
        }

        public void RequestUpgrade()
        {
            if (targetFacility == null)
            {
                FailUpgrade("업그레이드 대상 시설이 없습니다.", 0, 0);
                return;
            }

            if (!HousingInventoryResolver.IsNetworkReady(out string failReason))
            {
                FailUpgrade(failReason, 0, 0);
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

            if (!HousingInventoryResolver.TryGetRequesterInventory(
                    requesterClientId,
                    out IInventory inventory,
                    out string inventoryFailReason))
            {
                FailUpgrade(inventoryFailReason, 0, 0);
                return;
            }

            if (!PlayerHousingProgressResolver.TryGetProgress(
                    requesterClientId,
                    out PlayerHousingProgress progress))
            {
                FailUpgrade("요청자의 하우징 진행도를 찾을 수 없습니다.", 0, 0);
                return;
            }

            TryUpgradeWithRequesterData(requesterClientId, inventory, progress);
        }

        private bool TryUpgradeWithRequesterData(
            ulong requesterClientId,
            IInventory inventory,
            PlayerHousingProgress progress)
        {
            if (!IsServer)
            {
                FailUpgrade("시설 업그레이드는 서버에서만 처리할 수 있습니다.", 0, 0);
                return false;
            }

            if (targetFacility == null)
            {
                FailUpgrade("업그레이드 대상 시설이 없습니다.", 0, 0);
                return false;
            }

            if (inventory == null)
            {
                FailUpgrade("업그레이드에 사용할 요청자 인벤토리가 없습니다.", 0, 0);
                return false;
            }

            if (progress == null)
            {
                FailUpgrade("요청자의 하우징 진행도가 없습니다.", 0, 0);
                return false;
            }

            FacilityType facilityType = targetFacility.Type;
            int currentLevel = progress.GetLevel(facilityType);
            int maxLevel = targetFacility.GetMaxLevel();
            int nextLevel = currentLevel + 1;

            if (maxLevel <= 0)
            {
                FailUpgrade("FacilityDataSO의 레벨 데이터가 없습니다.", currentLevel, currentLevel);
                return false;
            }

            if (currentLevel >= maxLevel)
            {
                FailUpgrade("이미 최대 레벨입니다.", currentLevel, currentLevel);
                return false;
            }

            FacilityLevel nextLevelData = targetFacility.GetLevelData(nextLevel);

            if (nextLevelData == null)
            {
                FailUpgrade($"LV{nextLevel} 데이터가 FacilityDataSO에 없습니다.", currentLevel, currentLevel);
                return false;
            }

            if (!HasAllMaterials(inventory, nextLevelData))
            {
                FailUpgrade($"LV{nextLevel} 업그레이드 재료가 부족합니다.", currentLevel, currentLevel);
                return false;
            }

            if (!ConsumeAllMaterials(inventory, nextLevelData))
            {
                FailUpgrade($"LV{nextLevel} 업그레이드 재료 소모에 실패했습니다.", currentLevel, currentLevel);
                return false;
            }

            if (!progress.TrySetLevelFromServer(facilityType, nextLevel))
            {
                RestoreConsumedMaterials(inventory);
                FailUpgrade($"LV{nextLevel} 적용에 실패했습니다. 소모한 재료를 되돌렸습니다.", currentLevel, currentLevel);
                return false;
            }

            consumedMaterials.Clear();

            PlayerHousingSaveSyncer saveSyncer = progress.GetComponent<PlayerHousingSaveSyncer>();

            if (saveSyncer != null)
            {
                saveSyncer.RequestSaveFromServer($"{facilityType} 시설 업그레이드");
            }
            else
            {
                Debug.LogWarning(
                    $"[FacilityUpgradeController] PlayerHousingSaveSyncer가 없어 시설 레벨 저장 요청을 보낼 수 없습니다. ClientId: {requesterClientId}",
                    progress
                );
            }

            PublishUpgradeResult(currentLevel, nextLevel, true, "업그레이드 성공");

            EventBus.Publish(new FacilityUpgradedEvent
            {
                facilityType = facilityType,
                newLevel = nextLevel
            });

            if (logUpgradeResult)
            {
                Debug.Log(
                    $"[FacilityUpgradeController] 플레이어별 시설 업그레이드 성공\n" +
                    $"ClientId: {requesterClientId}\n" +
                    $"시설: {facilityType}\n" +
                    $"레벨: LV{currentLevel} → LV{nextLevel}",
                    this
                );
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

        private void FailUpgrade(string reason, int previousLevel, int currentLevel)
        {
            PublishUpgradeResult(previousLevel, currentLevel, false, reason);

            if (!logUpgradeResult)
                return;

            string facilityName = targetFacility != null ? targetFacility.name : "None";
            Debug.LogWarning($"[FacilityUpgradeController] {facilityName} 업그레이드 실패: {reason}", this);
        }

        private void PublishUpgradeResult(int previousLevel, int currentLevel, bool success, string reason)
        {
            string facilityName = targetFacility != null ? targetFacility.name : "None";

            EventBus.Publish(new HousingUpgradeResultEvent
            {
                facilityName = facilityName,
                previousLevel = previousLevel,
                currentLevel = currentLevel,
                success = success,
                reason = reason
            });
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 업그레이드 요청")]
        private void DebugForceUpgradeRequest()
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
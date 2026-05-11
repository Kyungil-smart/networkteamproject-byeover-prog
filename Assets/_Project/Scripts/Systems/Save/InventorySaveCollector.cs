using System.Collections.Generic;

using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Systems;

namespace DeadZone.Systems.Save
{
    // 로비 저장 시 플레이어 인벤토리 상태를 수집
    // 기존 UI 상태뿐 아니라, 로컬 플레이어의 GridInventory.ServerGrid도 저장 상태로 반영
    public class InventorySaveCollector : MonoBehaviour
    {
        [Header("저장 상태")]
        [SerializeField]
        private LobbyInventoryState inventoryState;

        [Header("UI 동기화")]
        [SerializeField]
        private LobbyInventoryStateUiBridge uiBridge;

        [SerializeField]
        private bool captureUiBeforeCollect = true;

        [Header("플레이어 인벤토리 수집")]
        [SerializeField]
        private bool capturePlayerGridInventory = true;

        [SerializeField]
        private GridInventory debugTargetInventory;

        [Header("로그")]
        [SerializeField]
        private bool logCollectResult = true;

        public void Collect(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            if (inventoryState == null)
            {
                Debug.LogWarning("[InventorySaveCollector] LobbyInventoryState가 연결되지 않았습니다. 저장용 인벤토리 상태 오브젝트를 연결해야 합니다.", this);
                return;
            }

            if (captureUiBeforeCollect && uiBridge != null)
                uiBridge.CaptureUiToState();

            if (capturePlayerGridInventory)
                CaptureLocalPlayerGridInventoryToState();

            dto.hasCredits = inventoryState.HasCredits;
            dto.credits = inventoryState.Credits;

            dto.inventoryItems.Clear();
            dto.stashItems.Clear();
            dto.equipmentItems.Clear();

            dto.inventoryItems.AddRange(inventoryState.InventoryItems);
            dto.stashItems.AddRange(inventoryState.StashItems);
            dto.equipmentItems.AddRange(inventoryState.EquipmentItems);

            if (logCollectResult)
            {
                Debug.Log(
                    $"[InventorySaveCollector] 저장 데이터 수집 완료\n" +
                    $"InventoryItems: {dto.inventoryItems.Count}\n" +
                    $"StashItems: {dto.stashItems.Count}\n" +
                    $"EquipmentItems: {dto.equipmentItems.Count}",
                    this
                );
            }
        }

        private void CaptureLocalPlayerGridInventoryToState()
        {
            GridInventory gridInventory = ResolveLocalPlayerGridInventory();

            if (gridInventory == null)
            {
                Debug.LogWarning("[InventorySaveCollector] 로컬 플레이어 GridInventory를 찾지 못했습니다.", this);
                return;
            }

            List<ItemSaveDTO> items = new();

            for (int i = 0; i < gridInventory.ServerGrid.Count; i++)
            {
                ItemSlotData slot = gridInventory.ServerGrid[i];
                string itemId = slot.itemId.ToString();

                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                items.Add(new ItemSaveDTO
                {
                    itemId = itemId,
                    instanceId = $"{itemId}_{slot.gridX}_{slot.gridY}_{i}",
                    containerId = "Inventory",
                    x = slot.gridX,
                    y = slot.gridY,
                    rotated = slot.rotated,
                    stackCount = Mathf.Max(1, slot.stackCount)
                });
            }

            inventoryState.SetInventoryItems(items);

            if (logCollectResult)
            {
                Debug.Log(
                    $"[InventorySaveCollector] 로컬 플레이어 GridInventory 수집 완료\n" +
                    $"오브젝트: {gridInventory.gameObject.name}\n" +
                    $"저장 아이템 수: {items.Count}",
                    gridInventory
                );
            }
        }

        private GridInventory ResolveLocalPlayerGridInventory()
        {
            if (debugTargetInventory != null)
                return debugTargetInventory;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient localClient))
                {
                    if (localClient.PlayerObject != null)
                    {
                        GridInventory playerInventory = localClient.PlayerObject.GetComponent<GridInventory>();

                        if (playerInventory != null)
                            return playerInventory;

                        playerInventory = localClient.PlayerObject.GetComponentInChildren<GridInventory>(true);

                        if (playerInventory != null)
                            return playerInventory;
                    }
                }
            }

            GridInventory[] inventories = FindObjectsByType<GridInventory>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                GridInventory inventory = inventories[i];

                if (inventory == null)
                    continue;

                if (inventory.IsOwner)
                    return inventory;
            }

            if (inventories.Length == 1)
                return inventories[0];

            return null;
        }

#if UNITY_EDITOR
        [Button("참조 자동 탐색")]
        private void AutoFindReferences()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (uiBridge == null)
                uiBridge = FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);

            if (debugTargetInventory == null)
                debugTargetInventory = FindFirstObjectByType<GridInventory>(FindObjectsInactive.Include);
        }

        [Button("로컬 플레이어 인벤토리 수집 테스트")]
        private void DebugCaptureLocalPlayerInventory()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            CaptureLocalPlayerGridInventoryToState();
        }
#endif
    }
}
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class InventorySaveCollector : MonoBehaviour
    {
        [Header("저장 상태")]
        [SerializeField] private LobbyInventoryState inventoryState;

        [Header("UI 동기화")]
        [SerializeField] private LobbyInventoryStateUiBridge uiBridge;
        [SerializeField] private bool captureUiBeforeCollect = true;

        public void Collect(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            if (captureUiBeforeCollect && uiBridge != null)
                uiBridge.CaptureUiToState();

            if (inventoryState == null)
            {
                Debug.LogWarning("[InventorySaveCollector] LobbyInventoryState가 연결되지 않았습니다. 저장용 인벤토리 상태 오브젝트를 연결해야 합니다.", this);
                return;
            }

            dto.hasCredits = inventoryState.HasCredits;
            dto.credits = inventoryState.Credits;
            dto.inventoryItems.AddRange(inventoryState.InventoryItems);
            dto.stashItems.AddRange(inventoryState.StashItems);
            dto.equipmentItems.AddRange(inventoryState.EquipmentItems);
        }

#if UNITY_EDITOR
        [Button("참조 자동 탐색")]
        private void AutoFindReferences()
        {
            if (inventoryState == null)
                inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (uiBridge == null)
                uiBridge = FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);
        }
#endif
    }
}

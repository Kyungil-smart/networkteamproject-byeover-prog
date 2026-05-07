using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeadZone.Actors.UI
{
    public class InventoryInputController : MonoBehaviour
    {
        [BoxGroup("UI 연결")]
        [Tooltip("Tab 키로 열고 닫을 InventoryUI입니다.")]
        [SerializeField] private InventoryUI inventoryUI;

        private void Update()
        {
            if (Keyboard.current == null)
                return;

            if (InGameMenuUI.IsAnyMenuBlockingInput())
                return;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                ToggleInventory();
            }
        }

        [BoxGroup("테스트")]
        [Button("인벤토리 토글")]
        private void ToggleInventory()
        {
            if (inventoryUI == null)
            {
                Debug.LogWarning("[InventoryInputController] InventoryUI가 연결되지 않았습니다.");
                return;
            }

            inventoryUI.Toggle();
        }
    }
}

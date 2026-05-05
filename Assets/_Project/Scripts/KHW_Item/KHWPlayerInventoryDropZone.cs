using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 플레이어 인벤토리 UI 영역에 붙이는 테스트용 DropZone입니다.
    /// 파밍 상자 슬롯을 이 영역에 드롭하면 해당 아이템을 서버 RPC로 플레이어 GridInventory에 넣습니다.
    /// </summary>
    public class KHWPlayerInventoryDropZone : MonoBehaviour, IDropHandler
    {
        [Header("드롭존 설명")]
        [Tooltip("파밍 상자 슬롯을 이 UI 영역 위에 드롭하면 플레이어 인벤토리로 이동합니다.")]
        [SerializeField] private string description = "파밍 아이템을 여기에 드롭";

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.pointerDrag == null) return;

            KHWContainerSlotUI slotUi = eventData.pointerDrag.GetComponent<KHWContainerSlotUI>();
            if (slotUi == null) return;

            slotUi.RequestMoveToPlayerInventory();
        }
    }
}

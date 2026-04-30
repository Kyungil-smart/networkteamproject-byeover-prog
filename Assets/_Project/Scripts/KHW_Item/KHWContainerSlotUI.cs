using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 파밍 상자 UI의 정사각형 슬롯 1개입니다.
    /// 아이템 아이콘 표시, 수량 표시, 드래그 시작/이동/종료를 담당합니다.
    /// </summary>
    public class KHWContainerSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("슬롯 이미지")]
        [Tooltip("아이템 아이콘을 표시할 Image입니다.")]
        [SerializeField] private Image iconImage;

        [Header("수량 텍스트")]
        [Tooltip("아이템 수량을 표시할 TMP 텍스트입니다.")]
        [SerializeField] private TextMeshProUGUI amountText;

        [Header("드래그 설정")]
        [Tooltip("드래그 중 원래 슬롯이 Drop 이벤트를 막지 않도록 Raycast를 끄기 위해 사용합니다.")]
        [SerializeField] private CanvasGroup canvasGroup;

        private KHWContainerLootWindowUI ownerWindow;
        private KHWLootContainer ownerContainer;
        private int slotIndex;
        private ItemDataSO currentItem;
        private int currentAmount;
        private Transform originalParent;
        private Canvas rootCanvas;

        public int SlotIndex
        {
            get
            {
                return slotIndex;
            }
        }

        public KHWLootContainer OwnerContainer
        {
            get
            {
                return ownerContainer;
            }
        }

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            rootCanvas = GetComponentInParent<Canvas>();
        }

        public void SetSlot(KHWContainerLootWindowUI window, KHWLootContainer container, int index, ItemDataSO item, int amount)
        {
            ownerWindow = window;
            ownerContainer = container;
            slotIndex = index;
            currentItem = item;
            currentAmount = amount;

            if (iconImage != null)
            {
                iconImage.sprite = item != null ? item.icon : null;
                iconImage.enabled = item != null && item.icon != null;
            }

            if (amountText != null)
            {
                if (item != null && amount > 1)
                {
                    amountText.text = amount.ToString();
                }
                else
                {
                    amountText.text = "";
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // [KHW 추가 기능]
            // 빈 슬롯은 드래그하지 않습니다.
            // 드래그 중에는 CanvasGroup.blocksRaycasts를 false로 만들어 DropZone이 OnDrop을 받을 수 있게 합니다.
            if (currentItem == null) return;

            originalParent = transform.parent;

            if (rootCanvas != null)
            {
                transform.SetParent(rootCanvas.transform, true);
            }

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = 0.7f;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (currentItem == null) return;
            transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (originalParent != null)
            {
                transform.SetParent(originalParent, true);
            }

            transform.localPosition = Vector3.zero;

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.alpha = 1f;
            }
        }

        public void RequestMoveToPlayerInventory()
        {
            // [KHW 추가 기능]
            // DropZone에서 호출됩니다. 실제 이동은 서버 RPC가 처리합니다.
            if (ownerContainer == null) return;
            if (currentItem == null) return;

            ownerContainer.TransferSlotToPlayerInventoryServerRpc(slotIndex);
        }
    }
}

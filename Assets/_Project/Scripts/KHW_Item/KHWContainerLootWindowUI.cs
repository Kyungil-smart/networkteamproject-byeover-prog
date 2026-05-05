using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 파밍 상자와 상호작용했을 때 열리는 테스트용 6칸 UI입니다.
    /// 상자 슬롯의 아이템 아이콘과 수량을 보여주고, 드래그 앤 드롭으로 플레이어 인벤토리에 넣습니다.
    /// </summary>
    public class KHWContainerLootWindowUI : MonoBehaviour
    {
        [Header("UI 루트")]
        [Tooltip("파밍 상자 UI 전체 패널입니다. Close 시 이 오브젝트를 비활성화합니다.")]
        [SerializeField] private GameObject rootPanel;

        [Header("상자 슬롯 UI")]
        [Tooltip("정사각형 슬롯 6개를 순서대로 연결합니다.")]
        [SerializeField] private KHWContainerSlotUI[] slotUis;

        [Header("제목 텍스트")]
        [Tooltip("상자 UI 상단 제목 텍스트입니다.")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Header("닫기 버튼")]
        [Tooltip("파밍 상자 UI를 닫는 버튼입니다.")]
        [SerializeField] private Button closeButton;

        private KHWLootContainer currentContainer;

        private void Awake()
        {
            if (rootPanel != null)
            {
                rootPanel.SetActive(false);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }

            UnsubscribeCurrentContainer();
        }

        public void Open(KHWLootContainer container)
        {
            // [KHW 추가 기능]
            // TargetClientRpc를 받은 클라이언트에서만 호출됩니다.
            // 현재 상자의 NetworkList 변경 이벤트를 구독해서 슬롯 UI를 자동 갱신합니다.
            UnsubscribeCurrentContainer();

            currentContainer = container;

            if (currentContainer != null && currentContainer.Slots != null)
            {
                currentContainer.Slots.OnListChanged += OnContainerSlotsChanged;
            }

            if (rootPanel != null)
            {
                rootPanel.SetActive(true);
            }

            if (titleText != null)
            {
                titleText.text = "파밍 상자";
            }

            RefreshSlots();
        }

        public void Close()
        {
            UnsubscribeCurrentContainer();

            if (rootPanel != null)
            {
                rootPanel.SetActive(false);
            }
        }

        private void UnsubscribeCurrentContainer()
        {
            if (currentContainer != null && currentContainer.Slots != null)
            {
                currentContainer.Slots.OnListChanged -= OnContainerSlotsChanged;
            }

            currentContainer = null;
        }

        private void OnContainerSlotsChanged(NetworkListEvent<KHWContainerSlotNetData> changeEvent)
        {
            RefreshSlots();
        }

        public void RefreshSlots()
        {
            if (slotUis == null) return;

            for (int i = 0; i < slotUis.Length; i++)
            {
                if (slotUis[i] == null) continue;

                ItemDataSO item = null;
                int amount = 0;

                if (currentContainer != null)
                {
                    KHWContainerSlotNetData slotData;
                    bool found = currentContainer.TryGetSlot(i, out slotData);
                    if (found && !slotData.IsEmpty)
                    {
                        item = currentContainer.LookupItem(slotData.itemId.ToString());
                        amount = slotData.amount;
                    }
                }

                slotUis[i].SetSlot(this, currentContainer, i, item, amount);
            }
        }
    }
}

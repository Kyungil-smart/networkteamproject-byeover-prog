using System.Collections.Generic;
using DeadZone.Actors;
using DeadZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class ContainerGridView : MonoBehaviour
    {
        [Header("상자 그리드")]
        [SerializeField] private List<InventorySlotUI> slots = new();
        [SerializeField] private ItemTooltipUI itemTooltipUI;
        [SerializeField] private bool autoCollectSlots = true;

        private LootContainer currentContainer;
        private IItemDatabase itemDb;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Bind(LootContainer container)
        {
            if (currentContainer == container)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            currentContainer = container;
            Subscribe();
            Refresh();
        }

        public void Clear()
        {
            Unsubscribe();
            currentContainer = null;
            Refresh();
        }

        public bool IsContainerSlot(InventorySlotUI slot, out int slotIndex)
        {
            slotIndex = -1;

            if (slot == null)
                return false;

            LootContainerSlotUI marker = slot.GetComponent<LootContainerSlotUI>();
            if (marker == null || marker.GridView != this)
                return false;

            slotIndex = marker.SlotIndex;
            return true;
        }

        public void Refresh()
        {
            ResolveReferences();

            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                slot.PrepareDropSlot(itemTooltipUI, i);

                LootContainerSlotUI marker = slot.GetComponent<LootContainerSlotUI>();
                if (marker == null)
                    marker = slot.gameObject.AddComponent<LootContainerSlotUI>();

                marker.Bind(this, currentContainer, i);

                if (!TryGetSlotData(i, out ContainerSlotNetData slotData) || slotData.IsEmpty)
                {
                    slot.ClearItem();
                    continue;
                }

                ItemDataSO itemData = ResolveItem(slotData.itemId.ToString());
                if (itemData == null)
                {
                    slot.ClearItem();
                    continue;
                }

                slot.SetItem(itemData, slotData.amount);
            }
        }

        private void Subscribe()
        {
            if (currentContainer == null || currentContainer.Slots == null)
                return;

            currentContainer.Slots.OnListChanged += HandleSlotsChanged;
        }

        private void Unsubscribe()
        {
            if (currentContainer == null || currentContainer.Slots == null)
                return;

            currentContainer.Slots.OnListChanged -= HandleSlotsChanged;
        }

        private void HandleSlotsChanged(NetworkListEvent<ContainerSlotNetData> changeEvent)
        {
            Refresh();
        }

        private bool TryGetSlotData(int index, out ContainerSlotNetData slotData)
        {
            slotData = default;

            if (currentContainer == null || index < 0)
                return false;

            if (currentContainer.Slots != null && index < currentContainer.Slots.Count)
            {
                slotData = currentContainer.Slots[index];
                return true;
            }

            return currentContainer.TryGetLocalSlot(index, out slotData);
        }

        private ItemDataSO ResolveItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            if (itemDb == null)
                itemDb = ServiceLocator.Get<IItemDatabase>();

            return itemDb?.GetById(itemId);
        }

        private void ResolveReferences()
        {
            if (itemTooltipUI == null)
                itemTooltipUI = GetComponentInChildren<ItemTooltipUI>(true);

            if (!autoCollectSlots || slots.Count > 0)
                return;

            slots.AddRange(GetComponentsInChildren<InventorySlotUI>(true));
        }
    }
}

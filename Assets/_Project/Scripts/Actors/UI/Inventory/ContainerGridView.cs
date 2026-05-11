using System.Collections.Generic;
using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;
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
        private CorpseInventory currentCorpseInventory;
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
            currentCorpseInventory = null;
            Subscribe();
            Refresh();
        }

        public void Bind(CorpseInventory corpseInventory)
        {
            if (currentCorpseInventory == corpseInventory)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            currentContainer = null;
            currentCorpseInventory = corpseInventory;
            Subscribe();
            Refresh();
        }

        public void Clear()
        {
            Unsubscribe();
            currentContainer = null;
            currentCorpseInventory = null;
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

                if (currentCorpseInventory != null)
                    marker.Bind(this, currentCorpseInventory, i);
                else
                    marker.Bind(this, currentContainer, i);

                if (!TryGetSlotViewData(i, out string itemId, out int amount))
                {
                    slot.ClearItem();
                    continue;
                }

                ItemDataSO itemData = ResolveItem(itemId);
                if (itemData == null)
                {
                    slot.ClearItem();
                    continue;
                }

                slot.SetItem(itemData, amount);
            }
        }

        private void Subscribe()
        {
            if (currentContainer != null && currentContainer.Slots != null)
                currentContainer.Slots.OnListChanged += HandleSlotsChanged;

            if (currentCorpseInventory != null && currentCorpseInventory.Slots != null)
                currentCorpseInventory.Slots.OnListChanged += HandleCorpseSlotsChanged;
        }

        private void Unsubscribe()
        {
            if (currentContainer != null && currentContainer.Slots != null)
                currentContainer.Slots.OnListChanged -= HandleSlotsChanged;

            if (currentCorpseInventory != null && currentCorpseInventory.Slots != null)
                currentCorpseInventory.Slots.OnListChanged -= HandleCorpseSlotsChanged;
        }

        private void HandleSlotsChanged(NetworkListEvent<ContainerSlotNetData> changeEvent)
        {
            Refresh();
        }

        private void HandleCorpseSlotsChanged(NetworkListEvent<ItemSlotData> changeEvent)
        {
            Refresh();
        }

        private bool TryGetSlotViewData(int index, out string itemId, out int amount)
        {
            itemId = string.Empty;
            amount = 0;

            if (currentContainer == null || index < 0)
            {
                return TryGetCorpseSlotViewData(index, out itemId, out amount);
            }

            if (currentContainer.Slots != null && index < currentContainer.Slots.Count)
            {
                ContainerSlotNetData slotData = currentContainer.Slots[index];
                if (slotData.IsEmpty)
                    return false;

                itemId = slotData.itemId.ToString();
                amount = slotData.amount;
                return amount > 0;
            }

            if (!currentContainer.TryGetLocalSlot(index, out ContainerSlotNetData localSlotData) || localSlotData.IsEmpty)
                return false;

            itemId = localSlotData.itemId.ToString();
            amount = localSlotData.amount;
            return amount > 0;
        }

        private bool TryGetCorpseSlotViewData(int index, out string itemId, out int amount)
        {
            itemId = string.Empty;
            amount = 0;

            if (currentCorpseInventory == null || currentCorpseInventory.Slots == null || index < 0 || index >= currentCorpseInventory.Slots.Count)
                return false;

            ItemSlotData slotData = currentCorpseInventory.Slots[index];
            itemId = slotData.itemId.ToString();
            amount = slotData.stackCount;
            return !string.IsNullOrEmpty(itemId) && amount > 0;
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

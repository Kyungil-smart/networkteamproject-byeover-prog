using DeadZone.Actors;
using DeadZone.Core;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class LootContainerSlotUI : MonoBehaviour, IInventorySlotDropHandler
    {
        public ContainerGridView GridView { get; private set; }
        public LootContainer Container { get; private set; }
        public int SlotIndex { get; private set; } = -1;

        public void Bind(ContainerGridView gridView, LootContainer container, int slotIndex)
        {
            GridView = gridView;
            Container = container;
            SlotIndex = slotIndex;
        }

        public bool TryHandleDrop(InventorySlotUI source, InventorySlotUI target)
        {
            if (Container == null || GridView == null || source == null || target == null)
                return false;

            bool sourceIsContainer = GridView.IsContainerSlot(source, out int sourceIndex);
            bool targetIsContainer = GridView.IsContainerSlot(target, out int targetIndex);

            if (!sourceIsContainer && !targetIsContainer)
                return false;

            if (sourceIsContainer && targetIsContainer)
            {
                if (sourceIndex != targetIndex)
                    Container.RequestMoveSlot(sourceIndex, targetIndex);

                return true;
            }

            if (sourceIsContainer)
            {
                Container.RequestTakeSlotToPlayer(sourceIndex);
                return true;
            }

            ItemDataSO itemData = source.CurrentItemData;
            if (itemData == null || source.CurrentStackCount <= 0)
                return true;

            Container.RequestDepositFromPlayer(itemData.itemID, source.CurrentStackCount, targetIndex);
            return true;
        }
    }
}

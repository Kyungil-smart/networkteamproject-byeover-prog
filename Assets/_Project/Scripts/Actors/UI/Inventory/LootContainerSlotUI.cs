using DeadZone.Actors;
using DeadZone.Core;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class LootContainerSlotUI : MonoBehaviour, IInventorySlotDropHandler
    {
        public ContainerGridView GridView { get; private set; }
        public LootContainer Container { get; private set; }
        public CorpseInventory CorpseInventory { get; private set; }
        public int SlotIndex { get; private set; } = -1;

        public void Bind(ContainerGridView gridView, LootContainer container, int slotIndex)
        {
            GridView = gridView;
            Container = container;
            CorpseInventory = null;
            SlotIndex = slotIndex;
        }

        public void Bind(ContainerGridView gridView, CorpseInventory corpseInventory, int slotIndex)
        {
            GridView = gridView;
            Container = null;
            CorpseInventory = corpseInventory;
            SlotIndex = slotIndex;
        }

        public bool TryHandleDrop(InventorySlotUI source, InventorySlotUI target)
        {
            if ((Container == null && CorpseInventory == null) || GridView == null || source == null || target == null)
                return false;

            bool sourceIsContainer = GridView.IsContainerSlot(source, out int sourceIndex);
            bool targetIsContainer = GridView.IsContainerSlot(target, out int targetIndex);

            if (!sourceIsContainer && !targetIsContainer)
                return false;

            if (CorpseInventory != null)
            {
                if (sourceIsContainer && !targetIsContainer)
                {
                    CorpseInventory.RequestTakeSlotToPlayer(sourceIndex);
                    return true;
                }

                return true;
            }

            if (sourceIsContainer && targetIsContainer)
            {
                if (sourceIndex != targetIndex)
                    Container.RequestMoveSlot(sourceIndex, targetIndex);

                return true;
            }

            if (sourceIsContainer)
            {
                if (Container != null && TryGetEquipmentTargetSlot(target, out EquipmentTargetSlot equipmentTargetSlot))
                {
                    Container.RequestEquipSlotToPlayer(sourceIndex, equipmentTargetSlot);
                    return true;
                }

                Container.RequestTakeSlotToPlayer(sourceIndex);
                return true;
            }

            ItemDataSO itemData = source.CurrentItemData;
            if (itemData == null || source.CurrentStackCount <= 0)
                return true;

            Container.RequestDepositFromPlayer(itemData.itemID, source.CurrentStackCount, targetIndex);
            return true;
        }

        private static bool TryGetEquipmentTargetSlot(InventorySlotUI target, out EquipmentTargetSlot targetSlot)
        {
            targetSlot = EquipmentTargetSlot.None;

            if (target == null)
                return false;

            string slotId = target.GetEquipmentSaveSlotId();
            if (string.IsNullOrWhiteSpace(slotId))
                return false;

            string normalized = slotId.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();
            targetSlot = normalized switch
            {
                "equipmenthead" or "head" => EquipmentTargetSlot.Head,
                "equipmentbackpack" or "backpack" => EquipmentTargetSlot.Backpack,
                "equipmentarmor" or "torso" => EquipmentTargetSlot.Armor,
                "equipmentprimaryweapon" or "primary1" => EquipmentTargetSlot.Primary1,
                "primary2" => EquipmentTargetSlot.Primary2,
                "equipmentsecondaryweapon" or "secondary" => EquipmentTargetSlot.Secondary,
                "equipmentmeleeweapon" or "melee" => EquipmentTargetSlot.Melee,
                _ => EquipmentTargetSlot.None
            };

            return targetSlot != EquipmentTargetSlot.None;
        }
    }
}

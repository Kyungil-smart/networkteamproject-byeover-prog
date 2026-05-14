using System;
using System.Collections.Generic;

namespace DeadZone.Systems.Raid
{
    [Serializable]
    public class RaidLoadoutSaveData
    {
        public ulong clientId;
        public List<InventoryItemSaveData> inventoryItems = new();
        public List<QuickSlotSaveData> quickSlotItems = new();
        public List<EquipmentSaveData> equipmentItems = new();
        public string currentEquippedItemId;
    }

    [Serializable]
    public class InventoryItemSaveData
    {
        public string itemId;
        public string instanceId;
        public int gridX;
        public int gridY;
        public bool rotated;
        public int stackCount;
        public float currentDurability;
        public int currentAmmo;
    }

    [Serializable]
    public class EquipmentSaveData
    {
        public string slotId;
        public string itemId;
        public string instanceId;
        public string loadedAmmoId;
        public int currentAmmo;
        public float currentDurability;
    }

    [Serializable]
    public class QuickSlotSaveData
    {
        public string itemId;
        public string instanceId;
        public int slotIndex;
        public int stackCount;
        public float currentDurability;
        public int currentAmmo;
    }
}

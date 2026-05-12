using System;
using System.Collections.Generic;

namespace DeadZone.Systems.Save
{
    [Serializable]
    public class LobbySaveDTO
    {
        public bool hasCredits;
        public int credits;
        public List<ItemSaveDTO> inventoryItems = new();
        public List<ItemSaveDTO> stashItems = new();
        public List<EquipmentSaveDTO> equipmentItems = new();
        public List<FacilitySaveDTO> facilities = new();
    }

    [Serializable]
    public class ItemSaveDTO
    {
        public string itemId;
        public string instanceId;
        public string containerId;
        public int x;
        public int y;
        public bool rotated;
        public int stackCount;
        public float currentDurability;
        public int currentAmmo;
    }

    [Serializable]
    public class EquipmentSaveDTO
    {
        public string slotId;
        public string itemId;
        public string instanceId;
        public string loadedAmmoId;
        public int currentAmmo;
        public float durability;
    }

    [Serializable]
    public class FacilitySaveDTO
    {
        public string facilityId;
        public int level;
    }
}

using System;
using System.Collections.Generic;

namespace DeadZone.Systems.Housing
{
    [Serializable]
    public sealed class HideoutFacilitySaveData
    {
        public List<FacilityLevelSaveData> facilities = new();
    }

    [Serializable]
    public sealed class FacilityLevelSaveData
    {
        public string facilityId;
        public int level;
    }
}
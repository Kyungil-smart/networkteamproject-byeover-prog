using System;
using System.Collections.Generic;
using UnityEngine;


namespace DeadZone.Core
{
    [Serializable]
    public struct ItemRequirement
    {
        public ItemDataSO item;
        public int amount;
    }

    [Serializable]
    public class FacilityLevel
    {
        public int level = 1;
        public List<ItemRequirement> upgradeMaterials;
        [TextArea] public string effectDescription;
    }

    [CreateAssetMenu(menuName = "DeadZone/Housing/Facility Data", fileName = "Facility_New")]
    public class FacilityDataSO : ScriptableObject
    {
        public FacilityType type;
        public FacilityLevel[] levels;

        public FacilityLevel GetLevel(int lv)
        {
            if (levels == null || levels.Length == 0) return null;
            int idx = Mathf.Clamp(lv - 1, 0, levels.Length - 1);
            return levels[idx];
        }
    }
}

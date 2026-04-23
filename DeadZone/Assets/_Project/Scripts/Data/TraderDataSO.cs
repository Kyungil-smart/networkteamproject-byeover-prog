using System;
using System.Collections.Generic;
using UnityEngine;


namespace DeadZone.Core
{
    [Serializable]
    public struct TraderEntry
    {
        public ItemDataSO item;
        public int basePrice;
        public int requiredCommLevel;
    }

    [CreateAssetMenu(menuName = "DeadZone/Economy/Trader Data", fileName = "Trader_New")]
    public class TraderDataSO : ScriptableObject
    {
        public string traderName;
        public List<TraderEntry> stock;
        public float buyPriceMultiplier = 1.0f;
    }
}

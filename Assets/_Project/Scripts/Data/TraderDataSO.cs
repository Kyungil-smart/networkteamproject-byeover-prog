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

        [Tooltip("유저→트레이더 판매 시 basePrice에 곱하는 배율 (0.5 = 50%)")]
        [Range(0f, 1f)]
        public float sellMultiplier = 0.5f;
    }
}
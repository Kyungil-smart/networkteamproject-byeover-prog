using UnityEngine;

namespace DeadZone.Core
{
    /// <summary>
    /// 가방 데이터. 장착 시 인벤토리 슬롯과 최대 무게를 증가시킨다.
    /// Lv1: +5슬롯 +10kg / Lv2~4: 레벨당 +5슬롯 +5kg 추가
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBackpack",
        menuName = "DeadZone/Data/Backpack Data",
        order = 5)]
    public class BackpackDataSO : ItemDataSO
    {
        [Header("=== Backpack Stats ===")]

        [Tooltip("가방 레벨 (1~4)")]
        public int backpackLevel = 1;

        [Tooltip("추가 인벤토리 슬롯 수")]
        public int extraSlots;

        [Tooltip("추가 최대 무게 (kg)")]
        public float extraWeightCapacity;
    }
}
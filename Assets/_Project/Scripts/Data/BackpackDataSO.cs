using UnityEngine;


namespace DeadZone.Core
{
    /// <summary>
    /// 가방 ScriptableObject. 장착 시 GridInventory의 높이가 확장된다.
    ///
    /// 그리드 구조 (GameSystem v1.0 + 결정사항):
    ///   - 베이스: 4x5 = 20칸 (가방 미장착)
    ///   - 가방 장착 시: 4 x (5 + extraRows)
    ///
    /// 등급별 권장 수치:
    ///   Common (Backpack_Small):    extraRows=5,  carryWeightBonus=+5
    ///   Uncommon (Backpack_Medium): extraRows=10,  carryWeightBonus=+10
    ///   Rare (Backpack_Large):      extraRows=15, carryWeightBonus=+15
    ///   Epic (Backpack_Tactical):   extraRows=20, carryWeightBonus=+25
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Items/Backpack Data", fileName = "Backpack_New")]
    public class BackpackDataSO : ItemDataSO
    {
        [Header("Backpack Capacity")]
        [Tooltip("베이스 그리드(4x5) 아래에 추가되는 행 개수")]
        [Range(0, 30)]
        public byte extraRows = 5;

        [Header("Carry Weight")]
        [Tooltip("소지 무게 한계 증가량 (kg). PlayerStats.carryWeight에 합산됨")]
        public float carryWeightBonus = 5f;

        private void OnValidate()
        {
            // BackpackDataSO 자산은 항상 Backpack 카테고리여야 함
            if (category != ItemCategory.Backpack)
            {
                category = ItemCategory.Backpack;
            }
        }
    }
}
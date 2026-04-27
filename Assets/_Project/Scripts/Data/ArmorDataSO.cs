using UnityEngine;


namespace DeadZone.Core
{
    public enum ArmorClass : byte { C1 = 1, C2, C3, C4, C5, C6 }

    /// <summary>
    /// 상의 방어구 전용. v1.1부터 머리는 HelmetDataSO가 담당하므로
    /// protectedParts에 Head를 포함하지 않는다 (C6 포함).
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Items/Armor Data", fileName = "Armor_New")]
    public class ArmorDataSO : ItemDataSO
    {
        [Header("Armor")]
        public ArmorClass armorClass = ArmorClass.C1;
        public float maxDurability = 50f;
        [Range(-0.20f, 0)] public float moveSpeedPenalty = 0f;
        [Range(0, 1)] public float blockChance = 0.1f;

        public BodyPart[] protectedParts = { BodyPart.Torso };
    }
}

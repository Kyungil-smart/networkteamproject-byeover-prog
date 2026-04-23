using UnityEngine;


namespace DeadZone.Core
{
    public enum HelmetClass : byte { C1 = 1, C2, C3, C4 }

    /// <summary>
    /// v1.1 신설. 헬멧 전용 SO. ArmorDataSO와 분리.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Items/Helmet Data", fileName = "Helmet_New")]
    public class HelmetDataSO : ItemDataSO
    {
        [Header("Helmet")]
        public HelmetClass helmetClass = HelmetClass.C1;
        public float maxDurability = 30f;
        [Range(-0.20f, 0)] public float moveSpeedPenalty = 0f;
        [Range(0, 1)] public float blockChance = 0.25f;

        public BodyPart[] protectedParts = { BodyPart.Head };
    }
}

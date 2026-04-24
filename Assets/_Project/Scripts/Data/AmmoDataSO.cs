using UnityEngine;


namespace DeadZone.Core
{
    public enum AmmoGrade : byte { LP, BP, AP }

    [CreateAssetMenu(menuName = "DeadZone/Items/Ammo Data", fileName = "Ammo_New")]
    public class AmmoDataSO : ItemDataSO
    {
        [Header("Ammo Properties")]
        public AmmoType caliber;
        public AmmoGrade grade;

        [Range(1, 6)] public int penetration = 1;
        public float damageMultiplier = 1.0f;
        public float priceMultiplier = 1.0f;

        [Header("Ballistics")]
        public float velocityMultiplier = 1.0f;
        public float dragCoefficient = 0.002f;
    }
}

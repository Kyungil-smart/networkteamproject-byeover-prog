using UnityEngine;


namespace DeadZone.Core
{
    public enum WeaponCategory : byte { AR, SMG, Handgun, Sniper, Shotgun, Melee }
    public enum FireMode : byte { Semi, Full, Bolt, Pump }
    public enum AmmoType : byte
    {
        AR, SMG, Handgun, Sniper, Shotgun
    }

    [CreateAssetMenu(menuName = "DeadZone/Items/Weapon Data", fileName = "Weapon_New")]
    public class WeaponDataSO : ItemDataSO
    {
        [Header("Weapon Stats")]
        public WeaponCategory weaponCategory;
        public float damage = 30f;
        public float fireRate = 10f;
        public int magSize = 30;
        public Vector2 engageRange = new(0, 100);

        [Header("Ammo")]
        public AmmoType ammoType;

        [Header("Fire Modes")]
        public FireMode[] availableModes;

        [Header("Projectile")]
        public float muzzleVelocity = 900f;
        public GameObject projectilePrefab;
        public float muzzleFlashOffset = 0.2f;

        [Header("ADS")]
        public float adsTransitionTime = 0.2f;

        [Header("Recoil")]
        [Tooltip("연사 시 탄환이 빗나가는 최대 각도 (0 = 완전 정확)")]
        public Vector2 spreadAngle = new(0.5f, 5.0f); // 최소/최대 탄퍼짐
        [Tooltip("사격 중단 시 에임이 다시 모이는 속도")]
        public float spreadRecovery = 10f;

        [Header("Durability")]
        public float maxDurability = 100f;
    }
}

using UnityEngine;


namespace DeadZone.Core
{
    public enum WeaponCategory : byte { AR, SMG, Handgun, Sniper, Shotgun, Melee }
    public enum FireMode : byte { Semi, Full, Bolt, Pump }
    public enum AmmoType : byte
    {
        _9mm, _556, _545, _46, _50AE, _357, _762, _12ga
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
        public float adsFOV = 50f;
        public float adsTransitionTime = 0.2f;

        [Header("Recoil")]
        public Vector2 recoilPattern = new(0.3f, 1.5f);
        public float recoilRecovery = 5f;

        [Header("Durability")]
        public float maxDurability = 100f;
    }
}

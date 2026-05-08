using UnityEngine;

namespace DeadZone.Data
{
    public enum ItemCategory
    {
        Weapon,
        Ammo,
        Armor,
        Helmet,
        Med,
        Food,
        Valuable,
        Material,
        Key,
        QuestItem,
        Backpack,
        Throwable
    }

    public enum RarityTier
    {
        Common,
        Rare,
        VeryRare,
        Epic
    }

    public enum AmmoType
    {
        Caliber_556,
        Caliber_545,
        Caliber_9mm,
        Caliber_46,
        Caliber_50AE,
        Caliber_357,
        Caliber_762,
        Caliber_12ga
    }

    public enum AmmoGrade { LP, BP, AP }

    public enum WeaponCategory { AR, SMG, Handgun, Sniper, Shotgun }

    public enum FireMode { Full, Semi, Bolt, Pump }

    public enum FacilityType { Workbench, MedicalStation, CommStation, Gym, Stash, Kitchen, Bed }
}
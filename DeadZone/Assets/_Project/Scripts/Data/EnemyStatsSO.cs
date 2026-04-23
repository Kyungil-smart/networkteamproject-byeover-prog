using UnityEngine;


namespace DeadZone.Core
{
    public enum EnemyFaction : byte { Conscript, Scavenger, Cerberus }

    [CreateAssetMenu(menuName = "DeadZone/Enemies/Enemy Stats", fileName = "Enemy_New")]
    public class EnemyStatsSO : ScriptableObject
    {
        [Header("Identity")]
        public string enemyId;
        public EnemyTier tier = EnemyTier.T1;
        public EnemyFaction faction = EnemyFaction.Conscript;

        [Header("Stats")]
        public float maxHP = 80f;
        public ArmorDataSO defaultArmor;
        public float moveSpeed = 3.5f;

        [Header("Detection")]
        public float visionRange = 30f;
        public float fov = 110f;
        public float reactionTime = 1.5f;
        public float hearingRange = 20f;

        [Header("Combat")]
        public WeaponDataSO defaultWeapon;
        [Range(0, 1)] public float accuracy = 0.45f;
        public float fireInterval = 1.2f;
        public int burstSize = 2;
        public float burstRestDelay = 1.0f;
        public float maxEffectiveRange = 30f;
    }
}

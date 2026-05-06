using UnityEngine;

namespace DeadZone.Core
{
    public enum Faction { Scavenger, Conscript, Cerberus }
    
    [CreateAssetMenu(
        fileName = "NewEnemyStats",
        menuName = "DeadZone/Data/Enemy Stats",
        order = 10)]
    public class EnemyStatsSO : ScriptableObject
    {
        [Header("━━━ 식별 ━━━")]
        public string displayName = "T1 졸병";
        public EnemyTier tier = EnemyTier.T1;
        public Faction faction = Faction.Scavenger;
        public bool isBoss;

        [Header("━━━ 체력 & 방어 ━━━")]
        [Range(50f, 2000f)]
        public float maxHP = 80f;

        [Tooltip("상체 아머 SO (null = 아머 없음)")]
        public ArmorDataSO defaultArmor;

        [Header("━━━ 무기 & 탄약 ━━━")]
        public WeaponDataSO defaultWeapon;
        public AmmoDataSO defaultAmmo;

        [Tooltip("SO의 기본 관통력에 더해지는 보정 (음수 = 약화)")]
        [Range(-4, 4)]
        public int penetrationModifier;

        [Tooltip("무기 데미지에 곱해지는 배율 (0.5 = 50%)")]
        [Range(0.1f, 2.0f)]
        public float damageMultiplier = 0.5f;

        [Header("━━━ 사격 타이밍 ━━━")]
        [Tooltip("점사 간 발사 간격 (초). 작을수록 빠르게 쏨")]
        [Range(0.1f, 3.0f)]
        public float fireInterval = 1.0f;

        [Tooltip("한 점사에 쏘는 탄 수")]
        [Range(1, 10)]
        public int burstSize = 3;

        [Tooltip("점사 끝난 후 다음 점사까지 쉬는 시간 (초)")]
        [Range(0.1f, 3.0f)]
        public float burstRestDelay = 0.8f;

        [Header("━━━ 탄퍼짐 (Spread) ━━━")]
        [Tooltip("최소 탄퍼짐 각도 (정지, 근거리)")]
        [Range(0f, 10f)]
        public float spreadAngleMin = 1.5f;

        [Tooltip("최대 탄퍼짐 각도 (이동, 원거리)")]
        [Range(0f, 20f)]
        public float spreadAngleMax = 4.5f;

        [Tooltip("유효사거리 초과 시 탄퍼짐 추가 배율")]
        [Range(1.0f, 3.0f)]
        public float rangeSpreadMultiplier = 1.5f;

        [Tooltip("유효 사거리 (m). 이 거리 이하에서 정상 교전")]
        [Range(10f, 300f)]
        public float maxEffectiveRange = 35f;

        [Header("━━━ 감지 ━━━")]
        [Range(10f, 200f)]
        public float visionRange = 30f;

        [Range(30f, 360f)]
        public float fov = 110f;

        [Range(5f, 60f)]
        public float hearingRange = 20f;

        [Tooltip("감지 후 사격 시작까지 지연 (초)")]
        [Range(0.1f, 3.0f)]
        public float reactionTime = 1.0f;

        [Header("━━━ 이동 ━━━")]
        [Range(1f, 10f)]
        public float moveSpeed = 4.0f;

        [Header("━━━ 교전 거리 (커버 시스템) ━━━")]
        [Tooltip("이 거리 이하면 후퇴")]
        [Range(0f, 50f)]
        public float preferredRangeMin = 8f;

        [Tooltip("이 거리 이상이면 접근")]
        [Range(10f, 200f)]
        public float preferredRangeMax = 30f;

        [Header("━━━ 확장 능력 (T3+) ━━━")]
        public bool canCallReinforcements;
        public bool canThrowGrenades;

        [Range(5f, 60f)]
        public float grenadeCooldown = 25f;
    }
}
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 청각 감지를 처리합니다.
    /// 총성 위치를 EnemyAI에 전달해 해당 위치를 조사하도록 만듭니다.
    /// </summary>
    public class EnemyHearing : NetworkBehaviour
    {
        [Header("청각 감지")]
        [Tooltip("SO가 없을 때 사용할 청각 감지 거리입니다.")]
        [SerializeField] private float hearingRange = 20f;

        [Tooltip("소리 크기를 거리 계산에 반영할 때 사용할 최소 배율입니다.")]
        [SerializeField] private float minimumLoudnessMultiplier = 0.2f;

        private EnemyAI ai;

        private void Awake() { ai = GetComponent<EnemyAI>(); }

        /// <summary>
        /// 네트워크 스폰 시 EnemyStatsSO의 청각 값을 적용하고 총성 이벤트를 구독합니다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // SO에서 hearingRange 읽기
            var stats = GetComponent<EnemyStats>();
            if (stats != null && stats.StatsSO != null)
                hearingRange = stats.StatsSO.hearingRange;

            EventBus.Subscribe<WeaponFiredEvent>(OnSound);
        }

        /// <summary>
        /// 네트워크 디스폰 시 총성 이벤트 구독을 해제합니다.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnSound);
        }

        private void OnSound(WeaponFiredEvent e)
        {
            if (!IsServer) return;
            float dist = Vector3.Distance(transform.position, e.origin);
            float effectiveRange = hearingRange * Mathf.Max(e.loudness, minimumLoudnessMultiplier);
            if (dist > effectiveRange) return;

            ai?.ReportSuspiciousPosition(e.origin);
        }
    }
}

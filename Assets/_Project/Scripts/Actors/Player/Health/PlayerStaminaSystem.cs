using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 스태미나만 담당. 서버 권위 소비 + 회복.
    /// SOLID의 단일 책임 원칙: PlayerHealthSystem과 PlayerNutritionSystem에서 분리됨.
    /// </summary>
    public class PlayerStaminaSystem : NetworkBehaviour
    {
        [Header("====스테미나 설정====")]
        [Tooltip("플레이어가 가질 수 있는 최대 스태미나")]
        [SerializeField, Min(0f)] private float maxStamina = 100f;
        
        [Tooltip("초당 회복되는 스태미나 양")]
        [SerializeField, Min(0f)] private float regenPerSecond = 8f;
        
        [Tooltip("스태미나를 소비한 뒤 회복이 다시 시작되기까지의 대기 시간" +
                 "\n값이 클수록 달리기나 행동 후 스태미나 회복이 늦게 시작")]
        [SerializeField, Min(0f)] private float regenDelaySeconds = 1.5f;

        public readonly NetworkVariable<float> CurrentStamina = new(100f);
        public float MaxStamina => maxStamina;

        // 마지막 스태미나 소비 시각
        private float lastConsumeTime;

        public override void OnNetworkSpawn()
        {
            if (IsServer) CurrentStamina.Value = maxStamina;

            CurrentStamina.OnValueChanged += (oldV, newV) =>
                EventBus.Publish(new PlayerStaminaChangedEvent
                {
                    clientId = OwnerClientId,
                    oldValue = oldV,
                    newValue = newV,
                });
        }

        private void Update()
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return;
            
            if (CurrentStamina.Value >= maxStamina) return;
            if (Time.time - lastConsumeTime < regenDelaySeconds) return;

            CurrentStamina.Value = Mathf.Min(maxStamina, CurrentStamina.Value + regenPerSecond * Time.deltaTime);
        }

        public bool TryConsume(float amount)
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 서버 권위 가드를 임시 비활성화
            // 복구 조건: 서버 전용 호출 경로가 검증되면 활성화
            // if (IsSpawned && !IsServer) return false;
            if (CurrentStamina.Value < amount) return false;
            CurrentStamina.Value -= amount;
            lastConsumeTime = Time.time;
            return true;
        }

        public bool TryConsumeForLocalTest(float amount)
        {
            if (IsSpawned) return false;
            if (amount <= 0f) return true;
            if (CurrentStamina.Value < amount) return false;
            
            CurrentStamina.Value -= amount;
            lastConsumeTime = Time.time;
            return true;
        }
    }
}

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
        [Header("Stamina")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float regenPerSecond = 8f;
        [SerializeField] private float regenDelaySeconds = 1.5f;

        public NetworkVariable<float> CurrentStamina = new(100f);
        public float MaxStamina => maxStamina;

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
            if (!IsServer) return;
            if (CurrentStamina.Value >= maxStamina) return;
            if (Time.time - lastConsumeTime < regenDelaySeconds) return;

            CurrentStamina.Value = Mathf.Min(maxStamina, CurrentStamina.Value + regenPerSecond * Time.deltaTime);
        }

        public bool TryConsume(float amount)
        {
            if (!IsServer) return false;
            if (CurrentStamina.Value < amount) return false;
            CurrentStamina.Value -= amount;
            lastConsumeTime = Time.time;
            return true;
        }
    }
}

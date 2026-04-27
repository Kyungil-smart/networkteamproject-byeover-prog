using UnityEngine;
using Unity.Netcode;


namespace DeadZone.Actors
{
    /// <summary>
    /// v1.1 — v1.0 DashSystem을 대체한다. WASD 방향으로 구르기, 입력 없으면 뒤구르기.
    /// 선택적 무적 프레임 윈도우.
    /// </summary>
    public class RollSystem : NetworkBehaviour
    {
        [Header("Roll")]
        [SerializeField] private float rollDistance = 3.5f;
        [SerializeField] private float rollDuration = 0.5f;
        [SerializeField] private float rollCooldown = 1.0f;
        [SerializeField] private int staminaCost = 25;
        [SerializeField] private float invincibilityWindow = 0.15f;
        [SerializeField] private AnimationCurve rollCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private CharacterController cc;
        private PlayerStaminaSystem stamina;
        private FPSController fps;

        private bool isRolling;
        private float rollEndTime;
        private float invincibleUntil;
        private float cooldownUntil;
        private Vector3 rollDirection;
        private float rollStartTime;
        private float distanceTraveled;

        public bool IsRolling => isRolling;
        public bool IsInvincible => Time.time < invincibleUntil;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            stamina = GetComponent<PlayerStaminaSystem>();
            fps = GetComponent<FPSController>();
        }

        public void TryRoll()
        {
            if (!IsOwner) return;
            if (isRolling) return;
            if (Time.time < cooldownUntil) return;
            if (stamina != null && stamina.CurrentStamina.Value < staminaCost) return;

            Vector3 dir = transform.forward * -1f;

            isRolling = true;
            rollDirection = dir;
            rollStartTime = Time.time;
            rollEndTime = Time.time + rollDuration;
            invincibleUntil = Time.time + invincibilityWindow;
            cooldownUntil = rollEndTime + rollCooldown;
            distanceTraveled = 0f;

            ConsumeStaminaServerRpc(staminaCost);
        }

        [ServerRpc] private void ConsumeStaminaServerRpc(int amount) => stamina?.TryConsume(amount);

        private void Update()
        {
            if (!isRolling || cc == null) return;

            float t = Mathf.InverseLerp(rollStartTime, rollEndTime, Time.time);
            float curveValue = rollCurve.Evaluate(t);
            float targetDistance = rollDistance * curveValue;
            float deltaDistance = targetDistance - distanceTraveled;
            distanceTraveled = targetDistance;

            cc.Move(rollDirection * deltaDistance);

            if (Time.time >= rollEndTime) isRolling = false;
        }
    }
}

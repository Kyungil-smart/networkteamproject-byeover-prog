using UnityEngine;
using Unity.Netcode;


namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어 구르기 이동을 담당한다.
    /// WASD 입력이 있으면 입력 방향으로 구르고, 입력이 없으면 캐릭터 기준 뒤로 구른다.
    /// 스태미나 소모, 쿨다운, 중복 입력 방지를 처리한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkObject))]
    public class RollSystem : NetworkBehaviour
    {
        [Header("====구르기 이동 설정====")]
        [Tooltip("구르기로 이동하는 거리")]
        [SerializeField, Min(0f)] private float rollDistance = 3.5f;
        
        [Tooltip("구르기 이동이 진행되는 시간")]
        [SerializeField, Min(0.01f)] private float rollDuration = 0.5f;
        
        [Tooltip("구르기 이동 거리 보간 곡선" +
                 "\nX는 진행 시간 비율, Y는 이동 거리 비율")]
        [SerializeField] private AnimationCurve rollCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        [Header("====구르기 비용/제한====")]
        [Tooltip("구르기 1회에 소모되는 스테미나")]
        [SerializeField, Min(0f)] private float staminaCost = 25f;
        
        [Tooltip("구르기 종료 후 다시 구를 수 있기까지의 대기시간")]
        [SerializeField, Min(0f)] private float rollCooldown = 1.0f;
        
        [Header("====구르기 무적 설정====")]
        [Tooltip("구르기 중 데미지를 무시할지 여부")]
        [SerializeField] private bool useDamageImmuneWindow = true;
        
        [Tooltip("구르기 시작 후 데미지를 무시하는 시간")]
        [SerializeField, Min(0f)] private float invincibilityWindow = 0.15f;
        
        [Header("====로컬 테스트====")]
        [Tooltip("NetworkObject가 스폰되지 않은 로컬 테스트 씬에서도 구르기를 허용할지 여부" +
                 "\nNGO 멀티 테스트 에서는 false로 하세요!!")]
        [SerializeField] private bool allowLocalRollWithoutNetworkSpawn = true;
        
        private CharacterController cc;
        private PlayerStaminaSystem staminaSystem;
        private FPSController fps;
        
        private bool isRolling;
        private float rollStartTime;
        private float rollEndTime;
        private float cooldownUntil;
        private float distanceTraveled;
        private Vector3 rollDirection;
        
        private bool isDamageImmune;   // 무적시간 확인용
        private float damageImmuneUntil;
        
        public bool IsRolling => isRolling;
        public bool IsDamageImmune => isDamageImmune;
        public Vector3 CurrentRollDirection => rollDirection;
        public float StaminaCost => staminaCost;
        public float InvincibilityWindow => invincibilityWindow;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            staminaSystem = GetComponent<PlayerStaminaSystem>();
            fps = GetComponent<FPSController>();
        }

        private void Update()
        {
            UpdateDamageImmuneWindow();
            
            if (!isRolling || cc == null) return;

            UpdateRollMovement();
        }
        
        public void CancelRoll()
        {
            isRolling = false;
            distanceTraveled = 0f;
            isDamageImmune = false;
        }
        
        public void TryRoll()
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 테스트 중에는 Owner 가드를 임시 비활성화
            // 복구 조건: NetworkManager 스폰과 소유자 입력 라우팅이 검증되면 활성화
            // if (IsSpawned && !IsOwner) return;
            
            if (!CanProcessRoll) return;
            if (isRolling) return;
            if (Time.time < cooldownUntil) return;
            if (!HasEnoughStamina()) return;
            
            Vector3 direction = GetRollDirection();
            
            if (direction.sqrMagnitude < 0.001f) return;

            StartRoll(direction);
        }

        private bool CanProcessRoll => IsSpawned ? IsOwner : allowLocalRollWithoutNetworkSpawn;

        private bool HasEnoughStamina()
        {
            if (staminaSystem == null) return true;
            
            return staminaSystem.CurrentStamina.Value >= staminaCost;
        }

        private Vector3 GetRollDirection()
        {
            if (fps != null && fps.HasMoveInput)
            {
                Vector3 moveDirection = fps.CurrentMoveDirection;
                moveDirection.y = 0;
                
                if (moveDirection.sqrMagnitude > 0.001f) return moveDirection.normalized;
            }
            
            Vector3 backward = -transform.forward;
            backward.y = 0;

            if (backward.sqrMagnitude < 0.001f) return Vector3.back;
            
            return backward.normalized;
        }

        private void StartRoll(Vector3 direction)
        {
            if (!TryConsumeStamina()) return;

            isRolling = true;
            rollDirection = direction;
            rollStartTime = Time.time;
            rollEndTime = Time.time + rollDuration;
            cooldownUntil = rollEndTime + rollCooldown;
            distanceTraveled = 0f;
            
            StartDamageImmuneWindow();
        }

        private bool TryConsumeStamina()
        {
            if (staminaSystem == null || staminaCost <= 0f) return true;

            if (IsSpawned)
            {
                if (IsServer) return staminaSystem.TryConsume(staminaCost);
                
                ConsumeStaminaServerRpc(staminaCost);
                return true;
            }
            
            // TODO(NetworkTest): 일반 로컬 테스트에서는 ServerRpc를 호출할 수 없으므로 로컬 스태미나 차감 경로를 사용
            // 복구 조건: Host/Client 테스트에서 서버 스태미나 차감 경로가 검증되면 테스트 전용 분기로 유지할지 제거할지 결정
            //return staminaSystem.CurrentStamina.Value >= staminaCost;
            
            return staminaSystem.TryConsumeForLocalTest(staminaCost);
        }
        
        private void UpdateDamageImmuneWindow()
        {
            if (!isDamageImmune) return;

            if (Time.time >= damageImmuneUntil) isDamageImmune = false;
        }
        
        private void StartDamageImmuneWindow()
        {
            if (!useDamageImmuneWindow || invincibilityWindow <= 0f)
            {
                isDamageImmune = false;
                return;
            }

            isDamageImmune = true;
            damageImmuneUntil = Time.time + invincibilityWindow;
        }
        
        [ServerRpc] private void ConsumeStaminaServerRpc(float amount) => staminaSystem?.TryConsume(amount);

        private void UpdateRollMovement()
        {
            float normalizedTime = Mathf.InverseLerp(rollStartTime, rollEndTime, Time.time);
            float curveValue = rollCurve.Evaluate(normalizedTime);

            float targetDistance = rollDistance * curveValue;
            float deltaDistance = targetDistance - distanceTraveled;
            distanceTraveled = targetDistance;

            cc.Move(rollDirection * deltaDistance);

            if (Time.time >= rollEndTime) EndRoll();
        }

        private void EndRoll()
        {
            isRolling = false;
            distanceTraveled = 0f;
        }
    }
}

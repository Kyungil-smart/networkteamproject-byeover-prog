using Unity.Netcode;
using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어 이동 컨트롤러. Owner 전용 — 이동은 로컬에서 적용되고
    /// ClientNetworkTransform이 위치를 다른 클라에 동기화한다.
    /// 기어가기 모드(Knocked 상태)는 속도를 강제로 낮추고 달리기를 비활성화한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkObject))]
    public class FPSController : NetworkBehaviour
    {
        [Header("====이동속도 설정====")]
        [Tooltip("기본 이동 속도")]
        [SerializeField, Min(0f)] private float walkSpeed = 4f;
        
        [Tooltip("달리기 상태일 때 적용되는 이동 속도" +
                 "\n스태미나가 0보다 클 때만 걷기 속도 대신 사용")]
        [SerializeField, Min(0f)] private float sprintSpeed = 7f;
        
        [Tooltip("기어가기 상태일 때 적용되는 이동 속도" +
                 "\nKnocked 상태처럼 정상 이동이 제한된 상황에서 사용")]
        [SerializeField, Min(0f)] private float crawlSpeed = 1f;

        [Header("====이동 기준====")]
        [Tooltip("카메라 기준 이동을 사용할지 여부" +
                 "\n켜면 W/A/S/D가 카메라 화면 방향 기준으로 이동")]
        [SerializeField] private bool useCameraRelativeMovement = true;

        [Tooltip("이동 방향 계산에 사용할 기준 Transform" +
                 "\nMainCamera 또는 CameraHolder를 연결" +
                 "\n비어 있으면 월드 X/Z 기준 이동으로 동작")]
        [SerializeField] private Transform movementReference;

        [Header("====회전 설정====")]
        [Tooltip("마우스 방향으로 캐릭터를 회전할지 여부")]
        [SerializeField] private bool rotateToLookDirection = true;
        
        [Header("====중력 설정====")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private LayerMask groundMask = ~0;

        private CharacterController cc;
        private PlayerStaminaSystem stamina;
        private RollSystem rollSystem;

        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool isSprinting;
        private bool isCrawling;
        private float verticalVelocity;
        private bool isMoveLocked;

        public Vector2 LookInput => lookInput;
        public Vector2 MoveInput => moveInput;
        public bool HasMoveInput => moveInput.sqrMagnitude > 0.001f;
        public Vector3 CurrentMoveDirection => GetMoveDirection();
        public bool IsSprinting => isSprinting;
        public bool IsCrawling => isCrawling;
        public bool IsMoveLocked => isMoveLocked;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            stamina = GetComponent<PlayerStaminaSystem>();
            rollSystem = GetComponent<RollSystem>();
        }

        private void Update()
        {
            // TODO(NetworkAuthority): 로컬 단일 플레이 이동 테스트 중에는 Owner 가드를 임시 비활성화
            // 복구 조건: NetworkManager가 PlayerPrefab을 스폰하고 소유자 입력 라우팅이 검증되면 활성화
            // if (IsSpawned && !IsOwner) return;
            
            if (isMoveLocked)
            {
                ApplyGravityOnly();
                return;
            }
            
            if (rollSystem != null && rollSystem.IsRolling)
            {
                ApplyRollRotation();
                ApplyGravityOnly();
                return;
            }
            
            ApplyLookRotation();

            float speed;
            if (isCrawling)
            {
                speed = crawlSpeed;
            }
            else
            {
                speed = walkSpeed;
                if (isSprinting && stamina != null && stamina.CurrentStamina.Value > 0) speed = sprintSpeed;
            }

            Vector3 moveDir = GetMoveDirection();

            if (cc.isGrounded && verticalVelocity < 0) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = moveDir * speed;
            velocity.y = verticalVelocity;

            cc.Move(velocity * Time.deltaTime);
        }

        public void SetMoveLocked(bool locked)
        {
            isMoveLocked = locked;

            if (locked)
            {
                moveInput = Vector2.zero;
                isSprinting = false;
            }
        }
        
        private Vector3 GetMoveDirection()
        {
            Vector3 rawInput = new Vector3(moveInput.x, 0f, moveInput.y);
            
            if (rawInput.sqrMagnitude > 1f) rawInput.Normalize();
            if (!useCameraRelativeMovement || movementReference == null) return rawInput;
            
            Vector3 forward = movementReference.forward;
            forward.y = 0f;
            
            Vector3 right = movementReference.right;
            right.y = 0f;
            
            if (forward.sqrMagnitude < 0.001f || right.sqrMagnitude < 0.001f) return rawInput;
            
            forward.Normalize();
            right.Normalize();
            
            Vector3 moveDir = right * moveInput.x + forward * moveInput.y;
            
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            
            return moveDir;
        }

        private void ApplyLookRotation()
        {
            if (!rotateToLookDirection) return;
            if (lookInput.sqrMagnitude < 0.001f)  return;

            Vector3 lookDir = new Vector3(lookInput.x, 0f, lookInput.y);
            
            if (lookDir.sqrMagnitude < 0.001f) return;
            
            transform.rotation = Quaternion.LookRotation(lookDir);
        }

        private void ApplyRollRotation()
        {
            Vector3 rollDirection = rollSystem.CurrentRollDirection;
            rollDirection.y = 0f;
            
            if (rollDirection.sqrMagnitude < 0.001f) return;
            
            transform.rotation = Quaternion.LookRotation(rollDirection.normalized);
        }

        private void ApplyGravityOnly()
        {
            if (cc == null) return;

            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            
            verticalVelocity += gravity * Time.deltaTime;
            
            Vector3 velocity = Vector3.up * verticalVelocity;
            cc.Move(velocity * Time.deltaTime);
        }
        
        public void SetMove(Vector2 v) => moveInput = v;
        public void SetLook(Vector2 v) => lookInput = v;
        public void SetSprint(bool b) => isSprinting = b;
        public void SetCrawlMode(bool b) => isCrawling = b;
    }
}

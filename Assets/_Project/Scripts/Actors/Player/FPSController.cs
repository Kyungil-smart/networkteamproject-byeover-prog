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

        [Header("====중력 설정====")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private LayerMask groundMask = ~0;

        private CharacterController cc;
        private PlayerStaminaSystem stamina;

        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool isSprinting;
        private bool isCrawling;
        private float verticalVelocity;

        public Vector2 LookInput => lookInput;
        public bool IsSprinting => isSprinting;
        public bool IsCrawling => isCrawling;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            stamina = GetComponent<PlayerStaminaSystem>();
        }

        private void Update()
        {
            //if (!IsOwner) return; 임시 주석처리 TODO(Step5): 원복

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

            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            moveDir = transform.TransformDirection(moveDir);

            if (cc.isGrounded && verticalVelocity < 0) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = moveDir * speed;
            velocity.y = verticalVelocity;

            cc.Move(velocity * Time.deltaTime);
        }

        public void SetMove(Vector2 v) => moveInput = v;
        public void SetLook(Vector2 v) => lookInput = v;
        public void SetSprint(bool b) => isSprinting = b;
        public void SetCrawlMode(bool b) => isCrawling = b;
    }
}

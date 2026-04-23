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
        [Header("Speed")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float crawlSpeed = 1f;

        [Header("Physics")]
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
            if (!IsOwner) return;

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

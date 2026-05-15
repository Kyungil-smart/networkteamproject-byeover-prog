using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Audio;


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

        [Header("====발걸음 오디오====")]
        [Tooltip("켜면 플레이어가 땅 위에서 이동할 때 AudioManager 이벤트로 발걸음 소리를 재생합니다.")]
        [SerializeField] private bool playFootstepSound = true;

        [Tooltip("걷기 상태에서 발걸음 소리를 다시 재생하기까지의 시간입니다.")]
        [SerializeField, Min(0.05f)] private float walkFootstepInterval = 0.45f;

        [Tooltip("달리기 상태에서 발걸음 소리를 다시 재생하기까지의 시간입니다.")]
        [SerializeField, Min(0.05f)] private float sprintFootstepInterval = 0.32f;

        [Tooltip("기어가기 상태에서 발걸음 소리를 다시 재생하기까지의 시간입니다.")]
        [SerializeField, Min(0.05f)] private float crawlFootstepInterval = 0.75f;

        [Tooltip("이 속도보다 느리면 발걸음 소리를 재생하지 않습니다.")]
        [SerializeField, Min(0f)] private float footstepMinSpeed = 0.2f;

        [Tooltip("플레이어 발걸음 볼륨 배율입니다. AudioLibrary의 개별 볼륨과 AudioManager의 SFX 볼륨이 함께 적용됩니다.")]
        [SerializeField, Range(0f, 2f)] private float footstepVolumeMultiplier = 1f;

        private CharacterController cc;
        private PlayerStaminaSystem stamina;
        private RollSystem rollSystem;

        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool isSprinting;
        private bool isCrawling;
        private float verticalVelocity;
        private bool isMoveLocked;
        private float footstepTimer;

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
            if (IsSpawned && !IsOwner) return;

            if (GameplayInputBlocker.IsBlocked)
            {
                moveInput = Vector2.zero;
                lookInput = Vector2.zero;
                isSprinting = false;
                ApplyGravityOnly();
                return;
            }
            
            if (isMoveLocked)
            {
                ApplyLookRotation();
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
            TickFootstepAudio(moveDir, speed);
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

        public void SetMovementReference(Transform reference)
        {
            movementReference = reference;
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

        private void TickFootstepAudio(Vector3 moveDir, float moveSpeed)
        {
            if (!playFootstepSound || cc == null || !cc.isGrounded)
            {
                footstepTimer = 0f;
                return;
            }

            if (moveDir.sqrMagnitude < 0.001f || moveSpeed < footstepMinSpeed)
            {
                footstepTimer = 0f;
                return;
            }

            footstepTimer += Time.deltaTime;
            float interval = GetFootstepInterval();
            if (footstepTimer < interval)
                return;

            footstepTimer = 0f;

            if (IsSpawned)
            {
                RequestPlayerFootstepServerRpc(transform.position, footstepVolumeMultiplier);
            }
            else
            {
                PublishPlayerFootstepAudio(transform.position, footstepVolumeMultiplier);
            }
        }

        private float GetFootstepInterval()
        {
            if (isCrawling)
                return crawlFootstepInterval;

            if (isSprinting && stamina != null && stamina.CurrentStamina.Value > 0)
                return sprintFootstepInterval;

            return walkFootstepInterval;
        }

        [ServerRpc]
        private void RequestPlayerFootstepServerRpc(Vector3 position, float volumeMultiplier)
        {
            PlayPlayerFootstepClientRpc(position, volumeMultiplier);
        }

        [ClientRpc]
        private void PlayPlayerFootstepClientRpc(Vector3 position, float volumeMultiplier)
        {
            PublishPlayerFootstepAudio(position, volumeMultiplier);
        }

        private static void PublishPlayerFootstepAudio(Vector3 position, float volumeMultiplier)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = AudioCueId.PlayerFootstep,
                position = position,
                use3D = true,
                volumeMultiplier = volumeMultiplier
            });
        }
        
        public void SetMove(Vector2 v) => moveInput = GameplayInputBlocker.IsBlocked ? Vector2.zero : v;
        public void SetLook(Vector2 v) => lookInput = GameplayInputBlocker.IsBlocked ? Vector2.zero : v;
        public void SetSprint(bool b) => isSprinting = !GameplayInputBlocker.IsBlocked && b;
        public void SetCrawlMode(bool b) => isCrawling = b;
    }
}

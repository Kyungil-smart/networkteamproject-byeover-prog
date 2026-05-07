using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// Player의 이동/구르기 상태를 Animator 파라미터에 반영한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerAniController : NetworkBehaviour
    {
        [Header("==== 애니메이션 참조 ====")]
        [Tooltip("이동/구르기 파라미터를 갱신할 Animator입니다. 비어 있으면 같은 오브젝트의 Animator를 사용합니다.")]
        [SerializeField] private Animator animator;

        [Tooltip("실제 이동 속도를 읽을 CharacterController입니다. 비어 있으면 같은 오브젝트의 CharacterController를 사용합니다.")]
        [SerializeField] private CharacterController characterController;

        [Tooltip("구르기 상태를 IsRolling 파라미터로 전달할 RollSystem입니다. 비어 있으면 같은 오브젝트에서 자동으로 찾습니다.")]
        [SerializeField] private RollSystem rollSystem;

        [Header("==== 네트워크 갱신 기준 ====")]
        [Tooltip("NetworkObject가 아직 Spawn되지 않은 로컬 테스트 상황에서도 Animator 파라미터 갱신을 허용합니다. Spawn된 PlayerObject에서는 Owner만 직접 갱신합니다.")]
        [SerializeField] private bool allowLocalAnimationWithoutNetworkSpawn = true;

        [Header("==== 이동 판정 기준 ====")]
        [Tooltip("Run 애니메이션에 도달하는 기준 속도입니다. FPSController의 sprintSpeed와 맞추는 것이 좋습니다. 단위: Unity unit/sec")]
        [SerializeField, Min(0.01f)] private float runSpeed = 7f;

        [Tooltip("이 속도 이하면 정지 상태로 판단합니다. 단위: Unity unit/sec")]
        [SerializeField, Min(0f)] private float movingThreshold = 0.05f;

        [Tooltip("이 정규화 속도 이상이면 Sprint/Run 상태로 판단합니다. 예: runSpeed가 7이고 값이 0.75라면 수평 속도 5.25 이상부터 Run으로 봅니다.")]
        [SerializeField, Range(0f, 1f)] private float sprintThreshold = 0.75f;

        [Header("==== Blend Tree 기준 ====")]
        [Tooltip("Walk 애니메이션이 배치된 2D Blend Tree 위치 크기입니다. 현재 Blend Tree에서 Walk/Strafe 위치가 0.5라면 기본값은 0.5입니다.")]
        [SerializeField, Range(0f, 1f)] private float walkBlendMagnitude = 0.5f;

        [Tooltip("Run 애니메이션이 배치된 2D Blend Tree 위치 크기입니다. 현재 Blend Tree에서 Run 위치가 1.0이라면 기본값은 1.0입니다.")]
        [SerializeField, Range(0f, 1f)] private float runBlendMagnitude = 1f;

        [Header("==== 보간 설정 ====")]
        [Tooltip("MoveX, MoveY, Speed 파라미터 변화가 급격하지 않도록 부드럽게 보간하는 시간입니다. 단위: 초")]
        [SerializeField, Min(0f)] private float parameterDampTime = 0.1f;

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");
        private static readonly int IsRollingHash = Animator.StringToHash("IsRolling");

        private void Awake()
        {
            CacheComponents();
        }

        private void Reset()
        {
            CacheComponents();
        }

        private void OnValidate()
        {
            runSpeed = Mathf.Max(0.01f, runSpeed);
            movingThreshold = Mathf.Max(0f, movingThreshold);
        }

        private void Update()
        {
            if (!CanUpdateAnimatorParameters()) return;
            if (animator == null || characterController == null) return;

            UpdateLocomotionParameters();
            UpdateRollParameter();
        }

        private void CacheComponents()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (rollSystem == null)
            {
                rollSystem = GetComponent<RollSystem>();
            }
        }

        private bool CanUpdateAnimatorParameters()
        {
            // Spawn된 PlayerObject에서는 Owner만 이동/구르기 파라미터를 직접 갱신한다.
            if (IsSpawned)
            {
                return IsOwner;
            }

            // 네트워크 Spawn 없이 배치된 테스트 오브젝트는 옵션에 따라 기존처럼 갱신한다.
            return allowLocalAnimationWithoutNetworkSpawn;
        }

        private void UpdateLocomotionParameters()
        {
            Vector3 horizontalVelocity = characterController.velocity;
            horizontalVelocity.y = 0f;

            float horizontalSpeed = horizontalVelocity.magnitude;
            bool isMoving = horizontalSpeed > movingThreshold;

            Vector2 localMoveDirection = CalculateLocalMoveDirection(horizontalVelocity, horizontalSpeed, isMoving);
            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / runSpeed);
            bool isSprinting = isMoving && normalizedSpeed >= sprintThreshold;

            float blendMagnitude = 0f;

            if (isMoving)
            {
                blendMagnitude = isSprinting ? runBlendMagnitude : walkBlendMagnitude;
            }

            Vector2 blendMove = localMoveDirection * blendMagnitude;

            animator.SetFloat(MoveXHash, blendMove.x, parameterDampTime, Time.deltaTime);
            animator.SetFloat(MoveYHash, blendMove.y, parameterDampTime, Time.deltaTime);
            animator.SetFloat(SpeedHash, normalizedSpeed, parameterDampTime, Time.deltaTime);

            animator.SetBool(IsMovingHash, isMoving);
            animator.SetBool(IsSprintingHash, isSprinting);
        }

        private Vector2 CalculateLocalMoveDirection(Vector3 horizontalVelocity, float horizontalSpeed, bool isMoving)
        {
            if (!isMoving)
            {
                return Vector2.zero;
            }

            Vector3 worldMoveDirection = horizontalVelocity / horizontalSpeed;
            Vector3 localDirection = transform.InverseTransformDirection(worldMoveDirection);
            localDirection.y = 0f;

            Vector2 localMoveDirection = new Vector2(localDirection.x, localDirection.z);

            if (localMoveDirection.sqrMagnitude > 1f)
            {
                localMoveDirection.Normalize();
            }

            return localMoveDirection;
        }

        private void UpdateRollParameter()
        {
            if (rollSystem == null) return;

            animator.SetBool(IsRollingHash, rollSystem.IsRolling);
        }
    }
}
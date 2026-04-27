using System.Threading;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// CharacterController의 실제 수평 이동 속도를 Animator 파라미터로 전달한다.
    /// 이동 자체는 FPSController/CharacterController가 담당하고,
    /// 이 컴포넌트는 애니메이션 표현만 갱신한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerAniController : MonoBehaviour
    {
        [Header("====애니메이션 참조====")]
        [Tooltip("이동 애니메이션 파라미터를 갱신할 Animator" +
                 "\n비어 있으면 같은 오브젝트의 Animator를 사용")]
        [SerializeField] private Animator animator;

        [Tooltip("실제 이동 속도를 읽을 CharacterController" +
                 "\n비어 있으면 같은 오브젝트의 CharacterController를 사용")]
        [SerializeField] private CharacterController characterController;
        
        [Header("====속도 단계 기준====")]
        [Tooltip("Run 애니메이션에 도달하는 기준 속도" +
                 "\nFPSController의 sprintSpeed와 맞추는 것이 좋다")]
        [SerializeField, Min(0.01f)] private float runSpeed = 7f;
        
        [Tooltip("이 속도 이하면 정지 상태로 판단")]
        [SerializeField, Min(0f)] private float movingThreshold = 0.05f;
        
        [Tooltip("이 정규화 속도 이상이면 Run 단계로 판단" +
                 "\n예: runSpeed가 7이고 값이 0.75라면, 수평 속도 5.25 이상부터 Run으로 봅니다.")]
        [SerializeField, Range(0f, 1f)] private float sprintThreshold = 0.75f;
        
        [Tooltip("Walk 애니메이션이 배치된 2D Blend Tree 위치 크기" +
                 "\n현재 Blend Tree에서 Walk/Strafe 위치가 0.5이므로 기본값은 0.5")]
        [SerializeField, Range(0f, 1f)] private float walkBlendMagnitude = 0.5f;

        [Tooltip("Run 애니메이션이 배치된 2D Blend Tree 위치 크기" +
                 "\n현재 Blend Tree에서 Run 위치가 1.0이므로 기본값은 1.0")]
        [SerializeField, Range(0f, 1f)] private float runBlendMagnitude = 1f;
        
        [Header("====보간 설정====")]
        [Tooltip("MoveX, MoveY, Speed 파라미터 변화가 급격하지 않도록 부드럽게 보간하는 시간")]
        [SerializeField, Min(0f)] private float parameterDampTime = 0.1f;
        
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (characterController == null) characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (animator == null ||  characterController == null) return;
            
            Vector3 horizontalVelocity = characterController.velocity;
            horizontalVelocity.y = 0f;
            
            float horizontalSpeed = horizontalVelocity.magnitude;
            bool isMoving = horizontalSpeed > movingThreshold;
            
            Vector2 localMoveDirection = Vector2.zero;
            
            if (isMoving)
            {
                Vector3 worldMoveDirection = horizontalVelocity / horizontalSpeed;
                Vector3 localDirection = transform.InverseTransformDirection(worldMoveDirection);
                localDirection.y = 0f;

                localMoveDirection = new Vector2(localDirection.x, localDirection.z);

                if (localMoveDirection.sqrMagnitude > 1f)
                {
                    localMoveDirection.Normalize();
                }
            }
            
            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / runSpeed);
            bool isSprinting = normalizedSpeed >= sprintThreshold;

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
        
    }
}
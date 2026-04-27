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
        [Tooltip("Speed 파라미터를 갱신할 Animator" +
                 "\n비어 있으면 같은 오브젝트의 Animator를 사용")]
        [SerializeField] private Animator animator;

        [Tooltip("실제 이동 속도를 읽을 CharacterController" +
                 "\n비어 있으면 같은 오브젝트의 CharacterController를 사용")]
        [SerializeField] private CharacterController characterController;

        [Header("====속도 정규화 기준====")]
        [Tooltip("Run 애니메이션에 도달하는 기준 속도" +
                 "\nFPSController의 sprintSpeed와 맞추는 것이 좋다")]
        [SerializeField, Min(0.01f)] private float runSpeed = 7f;

        [Header("====보간 설정====")]
        [Tooltip("Speed 파라미터 변화가 급격하지 않도록 부드럽게 보간하는 시간")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.1f;
        
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

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
            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / runSpeed);
            
            animator.SetFloat(SpeedHash, normalizedSpeed, speedDampTime, Time.deltaTime);
            animator.SetBool(IsMovingHash, horizontalSpeed > 0.05f);
        }
        
    }
}
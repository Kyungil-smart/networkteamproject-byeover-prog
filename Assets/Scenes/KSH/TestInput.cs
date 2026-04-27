using DeadZone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

public class TestInput : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float targetHeight = 1.2f; // 캐릭터 허리/총구 높이
    [SerializeField] private LayerMask groundMask;      // 지형 레이어
    [SerializeField] private InputActionAsset inputActions;

    [Header("Runtime Info (ReadOnly)")]
    public Vector3 AimWorldPosition; // 역산된 최종 조준점
    public bool IsFiring;

    private InputAction moveA, lookA, fireA;
    private Camera mainCam;

    private void Awake()
    {
        mainCam = Camera.main;
        var playerMap = inputActions.FindActionMap("Player", true);
        
        moveA = playerMap.FindAction("Move");
        lookA = playerMap.FindAction("Look"); // 보통 마우스 델타 혹은 위치
        fireA = playerMap.FindAction("Fire");

        fireA.performed += _ => EventBus.Publish(new FireInputEvent());

        playerMap.Enable();
    }

    private void Update()
    {
        // 1. 조준점 역산 로직 (가장자리 왜곡 및 고저차 대응)
        UpdateAimPosition();

        // 2. 사격 상태 업데이트 (Hold 방식 예시)
        if (fireA != null)
        {
            IsFiring = fireA.IsPressed();
        }
    }

    private void UpdateAimPosition()
    {
        // 마우스 현재 스크린 좌표 가져오기
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = mainCam.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundMask))
        {
            Vector3 hitPoint = hit.point;
            Vector3 reverseRayDir = -ray.direction.normalized;

            // 수식: d = h / cos(theta)
            // 지면 수직 벡터와 레이 역방향 벡터의 내적으로 cos 값 산출
            float cosTheta = Vector3.Dot(Vector3.up, reverseRayDir);

            if (cosTheta > 0.1f)
            {
                float distanceToMoveBack = targetHeight / cosTheta;
                AimWorldPosition = hitPoint + (reverseRayDir * distanceToMoveBack);
            }
            else
            {
                AimWorldPosition = hitPoint + (Vector3.up * targetHeight);
            }
        }
    }

    // 디버그용 시각화
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(AimWorldPosition, 0.2f);
        Gizmos.DrawLine(AimWorldPosition, AimWorldPosition + Vector3.down * targetHeight);
    }
}
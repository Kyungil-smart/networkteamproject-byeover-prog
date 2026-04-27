using UnityEngine;
using DeadZone.Core;
using UnityEngine.InputSystem;

public class TestShootRay : MonoBehaviour
{
    [SerializeField] private LayerMask groundMask; 
    [SerializeField] private float targetHeight = 1.2f;
    [SerializeField] Transform muzzlePoint;
    [SerializeField] GameObject bulletPrefab;
    
    
    
    private void OnEnable()
    {
        // 이벤트 구독
        EventBus.Subscribe<FireInputEvent>(OnFireInputReceived);
    }

    private void OnDisable()
    {
        // 메모리 누수 방지를 위한 구독 해제 (필수)
        EventBus.Unsubscribe<FireInputEvent>(OnFireInputReceived);
    }

    // 이벤트 수신 시 실행될 메서드
    private void OnFireInputReceived(FireInputEvent evt)
    {
        Shoot();
    }

    private void Shoot()
    {
        // 총구 높이를 기준으로 조준점 계산
        Vector3 aimPos = GetAimedPositionFromMuzzle(muzzlePoint, groundMask);
    
        if (aimPos != Vector3.zero)
        {
            Vector3 fireDir = (aimPos - muzzlePoint.position).normalized;
        
            // 탄환 생성 및 발사 로직...
            GameObject bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
            bullet.GetComponent<Projectile>().SetDirection(fireDir);
        }
    }

    public Vector3 GetAimedPosition(float height)
    {
        // 2. Input.mousePosition 대신 Mouse.current 사용
        if (Mouse.current == null) return Vector3.zero; 
    
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);
    
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundMask))
        {
            Vector3 rayDir = ray.direction.normalized;
            float cosTheta = Vector3.Dot(Vector3.up, -rayDir);
        
            if (cosTheta > 0.0001f)
            {
                float distanceToMoveBack = height / cosTheta;
                return hit.point - (rayDir * distanceToMoveBack);
            }
            return hit.point + (Vector3.up * height);
        }
        return Vector3.zero;
    }
    
    public Vector3 GetAimedPositionFromMuzzle(Transform muzzle, LayerMask groundLayer)
    {
        if (muzzle == null || Mouse.current == null) return Vector3.zero;

        // 1. 마우스 위치에서 레이 발사 (New Input System 방식)
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        // 2. 바닥(지형) 레이캐스트
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundLayer))
        {
            Vector3 hitPoint = hit.point;
            Vector3 reverseRayDir = -ray.direction.normalized;

            // 3. 목표 높이를 '현재 총구의 Y값'으로 설정
            // 만약 지형의 높이가 총구보다 높다면(벽 등), 최소 높이를 보정할 수 있습니다.
            float currentMuzzleHeight = muzzle.position.y;
        
            // 지면(hit.point.y)으로부터 총구 높이까지의 수직 거리(h)
            float h = currentMuzzleHeight - hitPoint.y;

            // 4. 수식 적용: d = h / cos(theta)
            float cosTheta = Vector3.Dot(Vector3.up, reverseRayDir);

            if (cosTheta > 0.1f)
            {
                float distanceToMoveBack = h / cosTheta;
                return hitPoint + (reverseRayDir * distanceToMoveBack);
            }

            // 예외 상황: 매우 가파른 각도일 경우 단순히 위로 올림
            return new Vector3(hitPoint.x, currentMuzzleHeight, hitPoint.z);
        }

        return Vector3.zero;
    }
}

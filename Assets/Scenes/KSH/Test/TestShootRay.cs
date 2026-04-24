using UnityEngine;

public class TestShootRay : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public Vector3 GetAimedPosition(float targetHeight)
    {
        // 1. 마우스 위치에서 레이 생성
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        // 2. 레이가 지면(예: Floor 레이어)에 부딪히는지 확인
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundMask))
        {
            Vector3 hitPoint = hit.point;
            Vector3 rayDir = ray.direction.normalized;
    
            // 3. 레이 방향의 반대 방향으로 역산
            // 지면의 법선(Vector3.up)과 레이 사이의 각도를 이용해 
            // 수직 높이 h를 만족하는 역산 거리 d를 구함
            float cosTheta = Vector3.Dot(Vector3.up, -rayDir);
            
            if (cosTheta > 0.0001f) // 각도가 너무 완만하지 않을 때만 계산
            {
                float distanceToMoveBack = targetHeight / cosTheta;
                return hitPoint - (rayDir * distanceToMoveBack);
            }
            
            return hitPoint; // 예외 상황 시 기본 히트 포인트 반환
        }
    
        return Vector3.zero;
    }
}

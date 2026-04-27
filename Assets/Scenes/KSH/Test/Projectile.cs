using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private float speed = 50f;
    [SerializeField] private float lifeTime = 3f;

    public void SetDirection(Vector3 direction)
    {
        // 탄환이 날아가는 방향을 바라보게 설정
        transform.forward = direction;
        // 일정 시간 후 자동 파괴 (또는 풀링 반환)
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 충돌 로직 (VFX 생성 등) 처리 후 파괴
        Destroy(gameObject);
    }
}
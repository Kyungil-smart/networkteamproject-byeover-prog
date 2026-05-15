using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 네트워크 판정과 분리된 로컬 탄도 시각 효과입니다.
    /// 서버 데미지 판정, 충돌 판정, NetworkObject Spawn에는 관여하지 않습니다.
    /// </summary>
    public class BulletTrailVisual : MonoBehaviour
    {
        private Vector3 direction;
        private float speed;
        private float maxDistance;
        private float maxLifetime;
        private float travelledDistance;
        private float elapsedTime;
        private bool initialized;

        private TrailRenderer trailRenderer;

        /// <summary>
        /// 서버가 확정한 발사 위치와 방향을 기준으로 각 클라이언트에서 로컬 visual을 재생합니다.
        /// </summary>
        public void Initialize(
            Vector3 origin,
            Vector3 moveDirection,
            float moveSpeed,
            float range,
            float lifetime)
        {
            direction = moveDirection.sqrMagnitude > 0.0001f
                ? moveDirection.normalized
                : transform.forward;

            speed = Mathf.Max(0.01f, moveSpeed);
            maxDistance = Mathf.Max(0.01f, range);
            maxLifetime = Mathf.Max(0.01f, lifetime);
            travelledDistance = 0f;
            elapsedTime = 0f;
            initialized = true;

            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction);

            trailRenderer = GetComponent<TrailRenderer>();
            if (trailRenderer != null)
            {
                trailRenderer.Clear();
                trailRenderer.emitting = true;
            }
        }

        private void Update()
        {
            if (!initialized)
                return;

            float deltaTime = Time.deltaTime;
            float moveDistance = speed * deltaTime;

            transform.position += direction * moveDistance;
            travelledDistance += moveDistance;
            elapsedTime += deltaTime;

            if (travelledDistance >= maxDistance || elapsedTime >= maxLifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    [DisallowMultipleComponent]
    public sealed class VFXManager : MonoBehaviour
    {
        [Header("====사격 FX====")]
        [Tooltip("총구가 벽에 막혔을 때 충돌 지점에 생성할 FX 프리팹입니다.")]
        [SerializeField] private GameObject blockedShotImpactPrefab;

        [Tooltip("벽 피격 FX를 자동 제거하기까지의 시간입니다.")]
        [SerializeField, Min(0.1f)] private float blockedShotImpactLifetime = 2f;

        private void OnEnable()
        {
            EventBus.Subscribe<BlockedShotImpactEvent>(OnBlockedShotImpact);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BlockedShotImpactEvent>(OnBlockedShotImpact);
        }

        private void OnBlockedShotImpact(BlockedShotImpactEvent e)
        {
            SpawnImpactVfx(blockedShotImpactPrefab, e.hitPoint, e.hitNormal, blockedShotImpactLifetime);
        }

        private void SpawnImpactVfx(GameObject prefab, Vector3 position, Vector3 normal, float lifetime)
        {
            // 생성할 FX 프리팹이 없으면 처리할 수 없으므로 종료합니다.
            if (prefab == null)
                return;

            // 충돌 표면 방향이 유효하면 FX의 정면을 표면 법선 방향으로 맞춥니다.
            Quaternion rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            // 계산된 위치와 회전으로 실제 FX 인스턴스를 생성합니다.
            GameObject instance = Instantiate(prefab, position, rotation);

            // 지정된 시간이 지나면 씬에 남은 FX 오브젝트를 제거합니다.
            if (lifetime > 0f)
                Destroy(instance, lifetime);
        }
    }
}

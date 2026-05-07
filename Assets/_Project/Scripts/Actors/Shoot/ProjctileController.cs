using Unity.Netcode;
using UnityEngine;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 서버 권위로 동작하는 투사체 컨트롤러입니다.
    /// 레이캐스트 CCD를 통해 고속 이동 시 터널링을 방지합니다.
    /// 네트워크 스폰 안 된 상태(오프라인 테스트)에서도 동작합니다.
    /// </summary>
    public class ProjectileController : NetworkBehaviour
    {
        [Header("Collision Settings")]
        [Tooltip("레이가 충돌을 감지할 모든 레이어를 포함합니다.")]
        [SerializeField] private LayerMask collisionMask;

        [Tooltip("데미지 판정을 수행할 레이어입니다. (예: Hitbox)")]
        [SerializeField] private LayerMask hitboxMask;

        [Tooltip("충돌 시 즉시 소멸할 환경 레이어입니다. (예: Ground, Env)")]
        [SerializeField] private LayerMask environmentMask;

        private ProjectileData data;
        private Vector3 direction;
        private float speed;
        private float distanceTravelled;
        private bool isInitialized;

        /// <summary>
        /// 생성 직후 서버에서 데이터를 주입하여 투사체를 활성화합니다.
        /// </summary>
        public void Initialize(ProjectileData pData, Vector3 dir, 
            float velocity)
        {
            // 네트워크 스폰됐으면 서버만, 아니면 로컬 실행
            if (IsSpawned && !IsServer) return;

            data = pData;
            direction = dir.normalized;
            speed = velocity;
            distanceTravelled = 0f;
            isInitialized = true;

            // 탄환의 진행 방향을 바라보도록 정렬
            transform.rotation = Quaternion.LookRotation(direction);
        }

        private void Update()
        {
            // 네트워크 스폰됐으면 서버만, 아니면 로컬 실행
            if (IsSpawned && !IsServer) return;
            if (!isInitialized) return;

            float moveDistance = speed * Time.deltaTime;
            Vector3 currentPos = transform.position;

            // 이번 프레임 이동 궤적 내에 장애물이 있는지 미리 검사 (CCD)
            if (Physics.Raycast(currentPos, direction, out RaycastHit hit, 
                moveDistance, collisionMask))
            {
                HandleCollision(hit);
            }
            else
            {
                // 장애물이 없으면 위치 이동 및 사거리 누적
                transform.position = currentPos + (direction * moveDistance);
                distanceTravelled += moveDistance;

                if (distanceTravelled >= data.Range)
                {
                    DespawnProjectile();
                }
            }
        }

        /// <summary>
        /// 레이캐스트에 검출된 객체의 레이어에 따라 로직을 분기합니다.
        /// </summary>
        private void HandleCollision(RaycastHit hit)
        {
            int layerBit = 1 << hit.collider.gameObject.layer;

            // 1. 데미지 판정 대상(Hitbox)인 경우
            if ((hitboxMask.value & layerBit) != 0)
            {
                ProcessDamage(hit);
            }
            // 2. 환경 지물(Environment/Ground)인 경우 즉시 소멸
            else if ((environmentMask.value & layerBit) != 0)
            {
                DespawnProjectile();
            }
        }

        private void ProcessDamage(RaycastHit hit)
        {
            if (hit.collider.TryGetComponent<HitZone>(out var hitZone))
            {
                var victim = hitZone.GetOwner<IDamageable>();
                if (victim != null)
                {
                    ServiceLocator.Get<DamageSystem>()?.ApplyDamage(
                        victim, 
                        hit.point, 
                        data
                    );
                }
            }

            DespawnProjectile();
        }

        /// <summary>
        /// 투사체를 제거한다. 네트워크 스폰 여부에 따라 분기 처리.
        /// </summary>
        private void DespawnProjectile()
        {
            // 네트워크 스폰된 상태 → Despawn
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
            // 오프라인 → 일반 Destroy
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
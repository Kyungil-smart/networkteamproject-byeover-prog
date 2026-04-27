using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// Owner가 로컬에서 먼저 발사하여 즉각 피드백을 주고, ServerRpc로 서버가 검증 후 데미지를 적용한다.
    /// Hitscan vs Projectile 결정은 무기팀의 몫 — 이 스텁은 Hitscan을 사용한다.
    /// </summary>
    public class ShootingSystem : NetworkBehaviour
    {
        [Header("Refs")]
        [Tooltip("총구를 할당해주세요")]
        [SerializeField] private Transform muzzleTransform;
        [Tooltip("피격 가능 대상의 레이어 입니다")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("바닥의 레이어 입니다")]
        [SerializeField] private LayerMask groundMask;
        [SerializeField] private float maxRange = 600f;

        private EquipmentSlots equipment;
        private float nextFireAllowed;
       
        private void Awake()
        {
            equipment = GetComponent<EquipmentSlots>();
        }

        // 레거시 
        public void TryFire()
        {
            if (!IsOwner) return;
            if (Time.time < nextFireAllowed) return;

            var weapon = equipment != null ? equipment.GetCurrentWeapon() : null;
            if (weapon == null) return;
            nextFireAllowed = Time.time + (1f / Mathf.Max(0.1f, weapon.fireRate));

            Vector3 origin = muzzleTransform != null ? muzzleTransform.position : transform.position;
            Vector3 dir = muzzleTransform != null ? muzzleTransform.forward : transform.forward;

            FireServerRpc(origin, dir, weapon.itemID);
        }
        // 탑뷰 기반 발사 시도 함수
        // 요청이 들어올 때 
        public void TryFire(Vector2 mousePos)
        {
            if (!IsOwner) return;
            if (Time.time < nextFireAllowed) return;

            var weapon = equipment?.GetCurrentWeapon();
            if (weapon == null) return;
            // todo 근접무기 예외처리 구현 필요 인풋 쪽에서 하는것이 직관적이지만 구조 변경 필요

            // 1. 전달받은 좌표로 즉시 타겟 지점 계산
            Vector3 targetPos = DetermineTargetPosition(mousePos);
            if (targetPos == Vector3.zero) return;

            // 2. 쿨타임 갱신 및 todo 서버 요청
            nextFireAllowed = Time.time + (1f / Mathf.Max(0.1f, weapon.fireRate));
            // FireServerRpc(targetPos, weapon.itemID);
        }
        
        // 레이캐스트 후 "목표" 트랜스폼을 반환하는 함수
        private Vector3 DetermineTargetPosition(Vector2 screenPos)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);

            // 우선순위 1: 히트박스 레이어 검출, 레이의 maxDistance= 200f는 임의값이고 의도 없음
            if (Physics.Raycast(ray, out var hit, 200f, hitMask))
            {
                // 충돌한 물체의 중심 위치를 가져옴
                float targetCenterY = hit.collider.transform.position.y + 1.0f; // todo 1.0f은 높이 오프셋, 테스트를 통해 조절 필요 
    
                Vector3 targetPos = hit.point;
                // 맞은 지점에서 y값 재설정
                targetPos.y = targetCenterY;
                return targetPos;
            }

            // 우선순위 2: 지면 레이어 검출 및 역산
            if (Physics.Raycast(ray, out hit, 200f, groundMask))
            {
                // 역방향 벡터
                Vector3 revDir = -ray.direction.normalized;
                // 지면과 총구 높이 기준으로 의도한 높이 추론
                float h = muzzleTransform.position.y - hit.point.y;
                // 수직축과 카메라 시선 사이의 각도
                float cos = Vector3.Dot(Vector3.up, revDir);

                // 삼각함수를 통해 계산한 최종 위치 반환
                return cos > 0.1f ? 
                    hit.point + (revDir * (h / cos)) : 
                    new Vector3(hit.point.x, muzzleTransform.position.y, hit.point.z);
            }

            return Vector3.zero;
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 origin, Vector3 dir, FixedString64Bytes weaponId, ServerRpcParams rpc = default)
        {
            ulong shooterId = rpc.Receive.SenderClientId;

            var weapon = equipment != null ? equipment.Lookup(weaponId.ToString()) as WeaponDataSO : null;
            if (weapon == null) return;

            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = shooterId,
                weaponId = weaponId,
                origin = origin,
                loudness = 1f,
            });

            if (Physics.Raycast(origin, dir, out RaycastHit hitInfo, maxRange, hitMask))
            {
                var hitZone = hitInfo.collider.GetComponent<HitZone>();
                if (hitZone == null) return;

                var hit = new HitInfo
                {
                    victim = hitZone.GetComponentInParent<NetworkObject>()?.gameObject,
                    zone = hitZone.ZoneType,
                    hitPoint = hitInfo.point,
                    hitNormal = hitInfo.normal,
                    distance = hitInfo.distance,
                };

                var defaultAmmo = ScriptableObject.CreateInstance<AmmoDataSO>();
                defaultAmmo.penetration = 3;
                defaultAmmo.damageMultiplier = 1f;

                ServiceLocator.Get<DamageSystem>()?.ApplyDamage(hit, defaultAmmo, weapon, shooterId);
            }
        }
    }
}

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
        private GameObject projectilePrefab; // 무기에서 추출 필요
        
        
        // 조준 분석 결과를 담는 내부 구조체
        private struct AimResult
        {
            public Vector3 targetPoint;
            public ulong targetId;
            public bool isHeadAim;
        }
       
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

            // FireServerRpc(origin, dir, weapon.itemID);
        }
        // 탑뷰 기반 발사 시도 함수
        // 요청이 들어올 때 
        public void TryFire(Vector2 mousePos)
        {
            if (!IsOwner) return;
            if (Time.time < nextFireAllowed) return;

            var weapon = equipment?.GetCurrentWeapon();
            if (weapon == null) return;

            // 1. 조준 의도 분석 (누구를, 어디를 노렸는가)
            AimResult result = AnalyzeAim(mousePos);
            if (result.targetPoint == Vector3.zero) return;

            // 2. 쿨타임 계산 및 서버 요청
            nextFireAllowed = Time.time + (1f / Mathf.Max(0.1f, weapon.fireRate));
            
            FireServerRpc(
                result.targetPoint, 
                result.targetId, 
                result.isHeadAim, 
                weapon.itemID
            );
        }
        
        private AimResult AnalyzeAim(Vector2 screenPos)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);

            // 1. 우선순위에 따른 타겟팅 시도
            if (TryHitboxAim(ray, out AimResult hitboxResult))
                return hitboxResult;

            if (TryGroundAim(ray, out AimResult groundResult))
                return groundResult;

            return new AimResult { targetPoint = Vector3.zero };
        }

        // 히트박스(적) 조준 분석
        private bool TryHitboxAim(Ray ray, out AimResult result)
        {
            result = new AimResult();
            if (Physics.Raycast(ray, out var hit, 200f, hitMask))
            {
                result.targetPoint = hit.point;
        
                // HitZone으로부터 대상 식별 및 의도 추출
                if (hit.collider.TryGetComponent<HitZone>(out var zone))
                {
                    var netObj = zone.GetOwner<NetworkObject>();
                    result.targetId = netObj != null ? 
                        netObj.NetworkObjectId : 0;
                    result.isHeadAim = (zone.ZoneType == BodyPart.Head);
                }
                return true;
            }
            return false;
        }

        // 지면(지형) 조준 및 수평 궤적 역산
        private bool TryGroundAim(Ray ray, out AimResult result)
        {
            result = new AimResult();
            if (Physics.Raycast(ray, out var hit, 200f, groundMask))
            {
                // 총구 높이를 유지하기 위한 수평 좌표 역산
                result.targetPoint = CalculateHorizontalPoint(ray, hit.point);
                result.targetId = 0;
                result.isHeadAim = false;
                return true;
            }
            return false;
        }

        // 삼각함수 기반 수평 좌표 계산 유틸리티
        private Vector3 CalculateHorizontalPoint(Ray ray, Vector3 hitPoint)
        {
            Vector3 revDir = -ray.direction.normalized;
            float h = muzzleTransform.position.y - hitPoint.y;
            float cos = Vector3.Dot(Vector3.up, revDir);

            // 카메라 각도가 너무 낮을 경우를 대비한 방어 로직
            if (cos <= 0.1f)
            {
                return new Vector3(hitPoint.x, muzzleTransform.position.y, hitPoint.z);
            }

            // d = h / cos(theta) 수식을 적용하여 총구 높이의 지점 산출
            return hitPoint + (revDir * (h / cos));
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 target, ulong tId, bool head, 
            FixedString64Bytes weaponId, ServerRpcParams rpc = default)
        {
            var weapon = equipment?.Lookup(weaponId.ToString()) as WeaponDataSO;
            if (weapon == null) return;

            Vector3 spawnPos = muzzleTransform.position;
            Vector3 fireDir = (target - spawnPos).normalized;

            // 탄환 생성 및 스폰
            GameObject bullet = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            var netObj = bullet.GetComponent<NetworkObject>();
            netObj.Spawn(true);

            // 투사체 초기화 (구조체 데이터 전달)
            if (bullet.TryGetComponent<ProjectileController>(out var pc))
            {
                var pData = new ProjectileData
                {
                    ShooterId = rpc.Receive.SenderClientId,
                    BaseDamage = (int)weapon.damage,
                    Penetration = 1, // 필요 시 무기/탄약 데이터 연동
                    TargetNetId = tId,
                    WasHeadAim = head,
                    Range = weapon.engageRange.y
                };

                pc.Initialize(pData, fireDir, weapon.muzzleVelocity);
            }

            // 발사 이벤트 전파 (사운드, 이펙트 등)
            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = rpc.Receive.SenderClientId,
                weaponId = weaponId,
                origin = spawnPos
            });
        }
    }
}

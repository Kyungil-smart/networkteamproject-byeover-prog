using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Audio;

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

        [Header("Shotgun")]
        [Tooltip("샷건 1회 사격 시 동시에 생성할 투사체 수")]
        [SerializeField, Min(1)] private int shotgunProjectileCount = 12;
        [Tooltip("샷건 투사체가 퍼질 전체 각도")]
        [SerializeField, Range(0f, 180f)] private float shotgunSpreadAngle = 24f;
        [Tooltip("샷건 투사체마다 더해지는 무작위 각도 오차")]
        [SerializeField, Range(0f, 15f)] private float shotgunPelletAngleJitter = 2f;
        [SerializeField, Min(0.1f)] private float shotgunEffectiveRange = 6f;
        [SerializeField, Min(0.1f)] private float shotgunMaxRange = 12f;
        [SerializeField, Range(0.05f, 1f)] private float shotgunMinDamageMultiplier = 0.15f;
        [SerializeField, Range(0.1f, 1f)] private float shotgunTotalDamageMultiplier = 0.55f;
        
        [Header("Projectile Visual")]
        [Tooltip("클라이언트에서 로컬로 재생하는 탄도 시각 효과의 최대 유지 시간입니다. 서버 판정 projectile 수명과는 별개입니다.")]
        [SerializeField, Min(0.01f)] private float projectileVisualMaxLifetime = 0.35f;

        [Header("Weapon Visual")]
        [SerializeField] private bool autoEquipWeaponVisual = true;
        [SerializeField] private Transform weaponHolder;
        [SerializeField] private string weaponHolderName = "WeaponHolder";
        [SerializeField] private string weaponMuzzlePointName = "MuzzlePoint";
        [SerializeField] private Vector3 weaponVisualPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 weaponVisualRotationOffset = Vector3.zero;
        [SerializeField] private Vector3 weaponVisualScale = Vector3.one;

        private EquipmentSlots equipment;
        private PlayerAnimatorDriver animatorDriver;
        private ReloadSystem reloadSystem;
        private Camera aimCamera;
        private float nextFireAllowed;
        private float currentSpreadAngle;
        private float lastServerFireTime;
        private Transform fallbackMuzzleTransform;
        private GameObject spawnedWeaponVisual;
        private string spawnedWeaponId;
        
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
            animatorDriver = GetComponent<PlayerAnimatorDriver>();
            reloadSystem = GetComponent<ReloadSystem>();
            fallbackMuzzleTransform = muzzleTransform;
        }

        public void SetAimCamera(Camera camera) => aimCamera = camera;

        public void SetMuzzleTransform(Transform muzzle)
        {
            muzzleTransform = muzzle != null ? muzzle : fallbackMuzzleTransform;
        }

        public void ResetMuzzleTransform()
        {
            muzzleTransform = fallbackMuzzleTransform;
        }

        // 레거시 
        public void TryFire()
        {
            if (!IsOwner) return;
            if (IsReloading()) return;
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
            if (IsReloading()) return;
            if (Time.time < nextFireAllowed) return;

            var weapon = equipment?.GetCurrentWeapon();
            if (weapon == null) return;

            // 1. 레이 캐스트로 조준 의도 분석 (누구를, 어디를 노렸는가)
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

        /// <summary>
        /// 버튼을 누르고 있는 동안 Full 모드를 지원하는 현재 무기만 연사 발사를 시도한다.
        /// 실제 발사 간격은 TryFire 내부의 nextFireAllowed 검사로 제한된다.
        /// </summary>
        public void TryFullAutoFire(Vector2 mousePos)
        {
            if (IsReloading()) return;
            if (!CurrentWeaponSupportsFull()) return;

            TryFire(mousePos);
        }

        private bool IsReloading()
        {
            if (reloadSystem == null)
                reloadSystem = GetComponent<ReloadSystem>();

            return reloadSystem != null && reloadSystem.IsReloading;
        }

        private bool CurrentWeaponSupportsFull()
        {
            var weapon = equipment?.GetCurrentWeapon();
            if (weapon?.availableModes == null) return false;

            for (int i = 0; i < weapon.availableModes.Length; i++)
            {
                if (weapon.availableModes[i] == FireMode.Full)
                {
                    return true;
                }
            }

            return false;
        }
        
        private AimResult AnalyzeAim(Vector2 screenPos)
        {
            if (aimCamera == null) return new AimResult { targetPoint = Vector3.zero };
            
            Ray ray = aimCamera.ScreenPointToRay(screenPos);

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
            if (Physics.Raycast(ray, out var hit, maxRange, hitMask))
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
            if (Physics.Raycast(ray, out var hit, maxRange, groundMask))
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
            if (IsReloading()) return;

            var weapon = equipment?.Lookup(weaponId.ToString()) as WeaponDataSO;
            var state = equipment?.CurrentWeaponState ?? default;
            var ammo = equipment?.CurrentAmmoData;

            if (weapon == null || ammo == null || state.currentAmmo <= 0) return;
            
            // 1. 탄환 소모
            equipment.ConsumeCurrentWeaponAmmo();

            // 2. 탄속 계산: 무기 기본 탄속 * 탄약 배율
            // V_final = V_muzzle * M_velocity
            float finalVelocity = weapon.muzzleVelocity * ammo.velocityMultiplier;

            // 3. 투사체 데이터 생성
            ProjectileData pData = CreateProjectileData(rpc.Receive.SenderClientId, tId, head, weapon, ammo);
    
            // 4. 투사체 생성 시 계산된 탄속 전달
            if (weapon.weaponCategory == WeaponCategory.Shotgun)
            {
                SpawnShotgunProjectiles(pData, target, weapon, finalVelocity);
            }
            else
            {
                SpawnProjectile(pData, target, weapon, finalVelocity);
            }

            // 서버에서 발사가 확정된 경우에만 모든 클라이언트에서 발사 애니메이션을 재생한다.
            PlayWeaponFireAnimationClientRpc();

            // 5. 이벤트 발행
            PublishFireEvent(rpc.Receive.SenderClientId, weaponId, weapon);

            AudioCueId fireCueId = GetFireCueId(weapon.weaponCategory);
            if (fireCueId != AudioCueId.None)
                PlayFireAudioClientRpc(fireCueId, muzzleTransform.position);
        }
        
        /// <summary>
        /// 무기와 탄약 데이터를 조합하여 최종 투사체 구조체를 생성합니다.
        /// </summary>
        private ProjectileData CreateProjectileData(ulong shooter, ulong target, 
            bool isHead, WeaponDataSO w, AmmoDataSO a)
        {
            return new ProjectileData
            {
                ShooterId = shooter,
                // 최종 데미지 = 무기 기본 데미지 * 탄약 배율
                BaseDamage = Mathf.RoundToInt(w.damage * a.damageMultiplier),
                // 탄약의 관통력 적용
                Penetration = a.penetration,
                TargetNetId = target,
                WasHeadAim = isHead,
                Range = w.engageRange.y,
                DamageFalloffStart = w.engageRange.y,
                MinDamageMultiplier = 1f
            };
        }
        
        /// <summary>
        /// 실제 투사체 오브젝트를 월드에 생성하고 네트워크에 등록합니다.
        /// </summary>
        private void SpawnProjectile(ProjectileData pData, Vector3 target, 
            WeaponDataSO w, float velocity)
        {
            Vector3 spawnPos = muzzleTransform.position;
            Vector3 fireDir = ApplySpread((target - spawnPos).normalized, w);

            SpawnProjectileWithDirection(pData, w, fireDir, velocity);
        }

        /// <summary>
        /// 샷건 사격 시 여러 개의 투사체를 지정된 각도 범위 안에서 같은 프레임에 생성한다.
        /// 각 투사체는 전체 산포각 안에 균등하게 배치하고, 작은 랜덤 오차를 더해 매 사격마다 자연스러운 편차를 만든다.
        /// </summary>
        private void SpawnShotgunProjectiles(ProjectileData pData, Vector3 target,
            WeaponDataSO w, float velocity)
        {
            Vector3 spawnPos = muzzleTransform.position;
            Vector3 baseDir = (target - spawnPos).normalized;
            int projectileCount = Mathf.Max(1, shotgunProjectileCount);
            float halfAngle = shotgunSpreadAngle * 0.5f;
            ProjectileData pelletData = pData;
            pelletData.Range = Mathf.Min(pData.Range, shotgunMaxRange);
            pelletData.DamageFalloffStart = Mathf.Min(shotgunEffectiveRange, pelletData.Range);
            pelletData.MinDamageMultiplier = shotgunMinDamageMultiplier;
            pelletData.BaseDamage = Mathf.Max(
                1,
                Mathf.RoundToInt((pData.BaseDamage * shotgunTotalDamageMultiplier) / projectileCount));

            for (int i = 0; i < projectileCount; i++)
            {
                float normalizedIndex = projectileCount == 1
                    ? 0.5f
                    : i / (float)(projectileCount - 1);
                float baseYaw = Mathf.Lerp(-halfAngle, halfAngle, normalizedIndex);
                float jitter = Random.Range(-shotgunPelletAngleJitter, shotgunPelletAngleJitter);
                float yaw = Mathf.Clamp(baseYaw + jitter, -halfAngle, halfAngle);
                Vector3 pelletDir = Quaternion.AngleAxis(yaw, Vector3.up) * baseDir;

                SpawnProjectileWithDirection(pelletData, w, pelletDir.normalized, velocity);
            }
        }

        /// <summary>
        /// 계산된 발사 방향을 기준으로 서버 판정 투사체를 생성하고, 클라이언트에는 로컬 visual 재생을 요청한다.
        /// </summary>
        private void SpawnProjectileWithDirection(ProjectileData pData,
            WeaponDataSO w, Vector3 fireDir, float velocity)
        {
            if (w == null || w.projectilePrefab == null)
                return;

            Vector3 spawnPos = muzzleTransform.position;
            Vector3 normalizedFireDir = fireDir.sqrMagnitude > 0.0001f
                ? fireDir.normalized
                : transform.forward;

            // 서버 권위 투사체: 충돌, 데미지, 사거리 판정은 기존 ProjectileController 흐름을 유지한다.
            GameObject bullet = Instantiate(w.projectilePrefab, spawnPos,
                Quaternion.LookRotation(normalizedFireDir));

            var netObj = bullet.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn(true);
            }

            // ProjectileController에 최종 계산된 velocity 주입
            if (bullet.TryGetComponent<ProjectileController>(out var pc))
            {
                pc.Initialize(pData, normalizedFireDir, velocity);
            }

            // 원격 클라이언트용 시각 효과: 빠른 탄도 Trail은 Transform 동기화 대신 각 클라이언트에서 로컬 재생한다.
            PlayProjectileVisualClientRpc(
                w.itemID,
                spawnPos,
                normalizedFireDir,
                velocity,
                pData.Range,
                projectileVisualMaxLifetime);
        }

        private Vector3 ApplySpread(Vector3 baseDir, WeaponDataSO weapon)
        {
            if (weapon == null) return baseDir;

            // x는 최소(기본), y는 최대 탄퍼짐으로 정의
            float minSpread = weapon.spreadAngle.x;
            float maxSpread = weapon.spreadAngle.y;

            // 1. 회복 로직: 마지막 사격 이후 흐른 시간만큼 회복
            float elapsed = lastServerFireTime > 0f ? 
                Time.time - lastServerFireTime : 0f;
    
            // minSpread 미만으로 내려가지 않도록 차단
            currentSpreadAngle = Mathf.Max(minSpread, 
                currentSpreadAngle - (weapon.spreadRecovery * elapsed));

            // 2. 현재 각도로 탄퍼짐 계산 (첫 탄은 minSpread 상태)
            float yawSpread = Random.Range(-currentSpreadAngle, currentSpreadAngle);
            Vector3 spreadDir = Quaternion.AngleAxis(yawSpread, Vector3.up) * baseDir;

            // 3. 다음 탄을 위해 탄퍼짐 누적 최소치의 절반만큼씩 누적
            float spreadIncrement = minSpread * 0.5f; 
            currentSpreadAngle = Mathf.Min(maxSpread, currentSpreadAngle + spreadIncrement);

            lastServerFireTime = Time.time;
            return spreadDir.normalized;
        }

        /// <summary>
        /// 서버가 확정한 탄도 정보를 받아 각 클라이언트에서 로컬 bullet trail을 재생한다.
        /// Host는 서버 투사체를 직접 보므로 중복 visual 생성을 생략한다.
        /// </summary>
        [ClientRpc]
        private void PlayProjectileVisualClientRpc(
            FixedString64Bytes weaponId,
            Vector3 spawnPos,
            Vector3 fireDir,
            float velocity,
            float range,
            float maxLifetime)
        {
            if (IsServer)
                return;

            GameObject visualPrefab = ResolveProjectileVisualPrefab(weaponId);
            if (visualPrefab == null)
                return;

            Vector3 normalizedFireDir = fireDir.sqrMagnitude > 0.0001f
                ? fireDir.normalized
                : transform.forward;

            GameObject visual = Instantiate(
                visualPrefab,
                spawnPos,
                Quaternion.LookRotation(normalizedFireDir));

            PrepareLocalProjectileVisual(visual);

            BulletTrailVisual trailVisual = visual.GetComponent<BulletTrailVisual>();
            if (trailVisual == null)
                trailVisual = visual.AddComponent<BulletTrailVisual>();

            trailVisual.Initialize(
                spawnPos,
                normalizedFireDir,
                velocity,
                range,
                maxLifetime);
        }

        /// <summary>
        /// 서버에서 확정된 발사 액션을 모든 클라이언트의 해당 Player Animator에 반영한다.
        /// Host도 발사 애니메이션을 봐야 하므로 서버 클라이언트에서 생략하지 않는다.
        /// </summary>
        [ClientRpc]
        private void PlayWeaponFireAnimationClientRpc()
        {
            if (animatorDriver == null)
                animatorDriver = GetComponent<PlayerAnimatorDriver>();

            animatorDriver?.TriggerFireAnimation();
        }

        private GameObject ResolveProjectileVisualPrefab(FixedString64Bytes weaponId)
        {
            if (equipment == null)
                equipment = GetComponent<EquipmentSlots>();

            WeaponDataSO weapon = equipment?.Lookup(weaponId.ToString()) as WeaponDataSO;
            return weapon != null ? weapon.projectilePrefab : null;
        }

        /// <summary>
        /// 네트워크 판정용 prefab을 로컬 visual로 재사용할 때 판정/네트워크 컴포넌트가 동작하지 않도록 비활성화한다.
        /// </summary>
        private static void PrepareLocalProjectileVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
                return;

            if (visualRoot.TryGetComponent<ProjectileController>(out var projectileController))
                projectileController.enabled = false;

            if (visualRoot.TryGetComponent<NetworkObject>(out var networkObject))
                networkObject.enabled = false;

            Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }
        
        private void PublishFireEvent(ulong clientId, FixedString64Bytes wId, WeaponDataSO weapon)
        {
            EventBus.Publish(new WeaponFiredEvent
            {
                shooterClientId = clientId,
                weaponId = wId,
                weaponData = weapon,
                weaponCategory = weapon != null ? weapon.weaponCategory : default,
                maxAmmo = weapon != null ? weapon.magSize : 0,
                maxDurability = weapon != null ? weapon.maxDurability : 0f,
                origin = muzzleTransform.position,
                loudness = 1f
            });
        }

        [ClientRpc]
        private void PlayFireAudioClientRpc(AudioCueId cueId, Vector3 position)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = cueId,
                position = position,
                use3D = true,
                volumeMultiplier = 1f
            });
        }

        private static AudioCueId GetFireCueId(WeaponCategory weaponCategory)
        {
            return weaponCategory switch
            {
                WeaponCategory.AR => AudioCueId.ARFire,
                WeaponCategory.SMG => AudioCueId.SMGFire,
                WeaponCategory.Handgun => AudioCueId.HGFire,
                WeaponCategory.Sniper => AudioCueId.SRDragFire,
                _ => AudioCueId.None,
            };
        }

        private void HandleCurrentEquippedChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquippedWeaponVisual();
        }

        private void RefreshEquippedWeaponVisual()
        {
            if (!autoEquipWeaponVisual)
                return;

            if (equipment == null)
                equipment = GetComponent<EquipmentSlots>();

            if (weaponHolder == null)
                weaponHolder = FindDeepChild(transform, weaponHolderName);

            string weaponId = equipment != null ? equipment.CurrentEquipped.Value.ToString() : string.Empty;
            if (spawnedWeaponVisual != null && spawnedWeaponId == weaponId)
                return;

            ClearWeaponVisual();

            if (string.IsNullOrWhiteSpace(weaponId))
            {
                muzzleTransform = fallbackMuzzleTransform;
                return;
            }

            WeaponDataSO weapon = equipment?.Lookup(weaponId) as WeaponDataSO;
            if (weapon == null || weapon.worldPrefab == null || weaponHolder == null)
            {
                muzzleTransform = fallbackMuzzleTransform;
                return;
            }

            spawnedWeaponVisual = Instantiate(weapon.worldPrefab, weaponHolder);
            spawnedWeaponId = weaponId;
            spawnedWeaponVisual.transform.localPosition = weaponVisualPositionOffset;
            spawnedWeaponVisual.transform.localRotation = Quaternion.Euler(weaponVisualRotationOffset);
            spawnedWeaponVisual.transform.localScale = weaponVisualScale;
            SetVisualCollidersEnabled(spawnedWeaponVisual, false);

            Transform weaponMuzzle = FindDeepChild(spawnedWeaponVisual.transform, weaponMuzzlePointName);
            muzzleTransform = weaponMuzzle != null ? weaponMuzzle : fallbackMuzzleTransform;
        }

        private void ClearWeaponVisual()
        {
            muzzleTransform = fallbackMuzzleTransform;
            spawnedWeaponId = string.Empty;

            if (spawnedWeaponVisual == null)
                return;

            Destroy(spawnedWeaponVisual);
            spawnedWeaponVisual = null;
        }

        private static void SetVisualCollidersEnabled(GameObject visualRoot, bool enabled)
        {
            if (visualRoot == null)
                return;

            Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = enabled;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;

            if (root.name == childName)
                return root;

            foreach (Transform child in root)
            {
                Transform found = FindDeepChild(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}

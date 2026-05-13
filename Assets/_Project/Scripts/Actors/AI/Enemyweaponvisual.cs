using DeadZone.Core;
using UnityEngine;

namespace DeadZone.Actors
{
    /// <summary>
    /// 적의 WeaponDataSO에 연결된 무기 월드 프리팹을 장착하고 총구 위치를 EnemyShooter에 연결합니다.
    /// </summary>
    public class EnemyWeaponVisual : MonoBehaviour
    {
        [Header("자동 장착")]
        [Tooltip("체크하면 부모 EnemyStats의 기본 무기 SO에서 월드 프리팹을 찾아 자동 장착합니다.")]
        [SerializeField] private bool autoEquipFromSO = true;

        [Tooltip("무기 장착 후 찾은 MuzzlePoint를 부모 EnemyShooter에 자동 연결합니다.")]
        [SerializeField] private bool connectMuzzleToShooter = true;

        [Header("수동 장착")]
        [Tooltip("자동 장착을 끌 때 직접 장착할 무기 프리팹입니다.")]
        [SerializeField] private GameObject weaponPrefabOverride;

        [Tooltip("무기 프리팹 내부에서 총구로 사용할 자식 오브젝트 이름입니다.")]
        [SerializeField] private string muzzlePointName = "MuzzlePoint";

        [Header("위치 보정")]
        [Tooltip("무기 모델의 로컬 위치 오프셋입니다.")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;

        [Tooltip("무기 모델의 로컬 회전 오프셋입니다.")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Tooltip("무기 모델의 로컬 스케일입니다.")]
        [SerializeField] private Vector3 scale = Vector3.one;

        private GameObject spawnedWeapon;
        private Renderer[] spawnedWeaponRenderers;
        private bool weaponRenderersRegistered;

        /// <summary>
        /// 현재 장착된 무기의 총구 위치입니다.
        /// </summary>
        public Transform MuzzlePoint { get; private set; }

        private void OnEnable()
        {
            RegisterSpawnedWeaponRenderers();
        }

        private void OnDisable()
        {
            UnregisterSpawnedWeaponRenderers();
        }

        private void Start()
        {
            if (autoEquipFromSO)
            {
                AutoEquipFromStats();
                return;
            }

            if (weaponPrefabOverride != null)
            {
                EquipWeapon(weaponPrefabOverride);
            }
        }

        /// <summary>
        /// 지정한 무기 프리팹을 현재 오브젝트 하위에 장착합니다.
        /// </summary>
        /// <param name="weaponPrefab">장착할 무기 프리팹입니다.</param>
        public void EquipWeapon(GameObject weaponPrefab)
        {
            ClearWeapon();

            if (weaponPrefab == null)
            {
                return;
            }

            spawnedWeapon = Instantiate(weaponPrefab, transform);
            spawnedWeapon.transform.localPosition = positionOffset;
            spawnedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
            spawnedWeapon.transform.localScale = scale;
            RegisterSpawnedWeaponRenderers();

            MuzzlePoint = FindDeepChild(spawnedWeapon.transform, muzzlePointName);
            if (MuzzlePoint == null)
            {
                Debug.LogWarning($"[EnemyWeaponVisual] {weaponPrefab.name}에서 {muzzlePointName} 오브젝트를 찾지 못했습니다.", this);
                return;
            }

            if (connectMuzzleToShooter)
            {
                ConnectMuzzleToShooter();
            }
        }

        private void AutoEquipFromStats()
        {
            EnemyStats stats = GetComponentInParent<EnemyStats>();
            if (stats == null || stats.StatsSO == null || stats.StatsSO.defaultWeapon == null)
            {
                return;
            }

            WeaponDataSO weaponSO = stats.StatsSO.defaultWeapon;
            if (weaponSO.worldPrefab == null)
            {
                Debug.LogWarning($"[EnemyWeaponVisual] {weaponSO.name}의 World Prefab이 비어 있습니다.", this);
                return;
            }

            EquipWeapon(weaponSO.worldPrefab);
        }

        private void ConnectMuzzleToShooter()
        {
            EnemyShooter shooter = GetComponentInParent<EnemyShooter>();
            if (shooter == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] 부모 오브젝트에서 EnemyShooter를 찾지 못했습니다.", this);
                return;
            }

            shooter.SetMuzzle(MuzzlePoint);
        }

        private void ClearWeapon()
        {
            UnregisterSpawnedWeaponRenderers();
            MuzzlePoint = null;

            if (spawnedWeapon == null)
            {
                return;
            }

            Destroy(spawnedWeapon);
            spawnedWeapon = null;
            spawnedWeaponRenderers = null;
        }

        /// <summary>
        /// 장착된 적 무기의 Renderer들을 VisionMaskManager에 등록한다.
        /// 적 본체는 CharacterVisual 쪽에서 등록하고, 무기는 런타임 생성물이므로 EnemyWeaponVisual이 별도로 등록한다.
        /// </summary>
        private void RegisterSpawnedWeaponRenderers()
        {
            if (weaponRenderersRegistered || spawnedWeapon == null)
                return;

            // 비주얼 교체시 렌더러 재등록(기존 렌더 해제 및 활성 랜더 등록) 과정이 필요합니다.
            spawnedWeaponRenderers = spawnedWeapon.GetComponentsInChildren<Renderer>(false);
            if (spawnedWeaponRenderers == null || spawnedWeaponRenderers.Length == 0)
                return;

            weaponRenderersRegistered = true;
            EventBus.Publish(new VisionMaskRenderersRegisteredEvent
            {
                renderers = spawnedWeaponRenderers
            });
        }

        /// <summary>
        /// 장착 무기가 제거되거나 EnemyWeaponVisual이 비활성화될 때 VisionMaskManager 등록을 해제한다.
        /// 해제 이벤트는 Renderer 상태를 직접 되돌리지 않고, 매니저의 대상 목록에서만 제거하도록 요청한다.
        /// </summary>
        private void UnregisterSpawnedWeaponRenderers()
        {
            if (!weaponRenderersRegistered)
                return;

            weaponRenderersRegistered = false;
            EventBus.Publish(new VisionMaskRenderersUnregisteredEvent
            {
                renderers = spawnedWeaponRenderers
            });
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            if (root.name == childName)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindDeepChild(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

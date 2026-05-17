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

        [Header("무기 Mount")]
        [Tooltip("무기 생성 기준이 되는 기본 부모 Transform입니다. 비어 있으면 이 컴포넌트가 붙은 Transform을 사용합니다.")]
        [SerializeField] private Transform weaponHolder;

        [Tooltip("Handgun 계열 무기를 생성할 위치입니다. 비어 있으면 WeaponHolder로 fallback되며, 손 위치가 어긋날 수 있습니다.")]
        [SerializeField] private Transform handgunMount;

        [Tooltip("AR / SMG / Sniper / Shotgun 계열 무기를 생성할 위치입니다. 비어 있으면 WeaponHolder로 fallback되며, 장총 위치가 어긋날 수 있습니다.")]
        [SerializeField] private Transform rifleLikeMount;

        [Tooltip("무기 내부 MuzzlePoint를 찾지 못했을 때 EnemyShooter에 연결할 예비 발사 위치입니다. 비어 있으면 기존 EnemyShooter muzzle 또는 현재 Transform을 fallback으로 사용합니다.")]
        [SerializeField] private Transform fallbackMuzzle;

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
        private bool[] spawnedWeaponRendererDefaultEnabled;
        private bool weaponRenderersRegistered;
        private bool desiredWeaponVisible;
        private bool? lastAppliedWeaponVisible;

        /// <summary>
        /// 현재 장착된 무기의 총구 위치입니다.
        /// </summary>
        public Transform MuzzlePoint { get; private set; }

        /// <summary>
        /// AI 상태에서 요구하는 현재 무기 표시 상태입니다.
        /// 실제 발사 기준인 MuzzlePoint는 숨김 상태에서도 유지됩니다.
        /// </summary>
        public bool IsWeaponVisible => desiredWeaponVisible;

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
            EquipWeapon(weaponPrefab, null);
        }

        /// <summary>
        /// WeaponDataSO 기준으로 무기 카테고리에 맞는 Mount를 선택해 장착합니다.
        /// </summary>
        /// <param name="weaponData">장착할 무기 데이터입니다.</param>
        public void EquipWeapon(WeaponDataSO weaponData)
        {
            if (weaponData == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] 장착할 WeaponDataSO가 비어 있습니다.", this);
                ClearWeapon();
                ConnectMuzzleToShooter(ResolveMuzzle(null, GetFallbackMount(), ResolveShooter()));
                return;
            }

            if (weaponData.worldPrefab == null)
            {
                Debug.LogWarning($"[EnemyWeaponVisual] {weaponData.name}의 World Prefab이 비어 있습니다.", this);
                ClearWeapon();
                ConnectMuzzleToShooter(ResolveMuzzle(null, GetFallbackMount(), ResolveShooter()));
                return;
            }

            EquipWeapon(weaponData.worldPrefab, weaponData);
        }

        private void EquipWeapon(GameObject weaponPrefab, WeaponDataSO weaponData)
        {
            ClearWeapon();

            if (weaponPrefab == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] 장착할 무기 프리팹이 비어 있습니다.", this);
                ConnectMuzzleToShooter(ResolveMuzzle(null, GetFallbackMount(), ResolveShooter()));
                return;
            }

            Transform selectedMount = ResolveWeaponMount(weaponData);
            EnemyShooter shooter = ResolveShooter();

            spawnedWeapon = Instantiate(weaponPrefab, selectedMount);
            spawnedWeapon.transform.localPosition = positionOffset;
            spawnedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
            spawnedWeapon.transform.localScale = scale;
            RegisterSpawnedWeaponRenderers();

            MuzzlePoint = ResolveMuzzle(spawnedWeapon.transform, selectedMount, shooter);

            if (connectMuzzleToShooter)
            {
                ConnectMuzzleToShooter(MuzzlePoint, shooter);
            }

            ApplyWeaponRendererVisibility();
        }

        /// <summary>
        /// AI 상태에 따라 장착 무기 표시만 전환합니다.
        /// 무기 GameObject와 MuzzlePoint를 유지해야 숨김 상태에서도 EnemyShooter의 발사 기준이 끊기지 않습니다.
        /// </summary>
        /// <param name="visible">전투 흐름에서는 true, 순찰/복귀 같은 비전투 상태에서는 false입니다.</param>
        public void SetWeaponVisible(bool visible)
        {
            if (desiredWeaponVisible == visible &&
                lastAppliedWeaponVisible.HasValue &&
                lastAppliedWeaponVisible.Value == visible)
            {
                return;
            }

            desiredWeaponVisible = visible;
            ApplyWeaponRendererVisibility();
        }

        private void AutoEquipFromStats()
        {
            EnemyStats stats = GetComponentInParent<EnemyStats>();
            if (stats == null || stats.StatsSO == null || stats.StatsSO.defaultWeapon == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] EnemyStatsSO.defaultWeapon을 찾지 못해 무기 자동 장착을 건너뜁니다.", this);
                ConnectMuzzleToShooter(ResolveMuzzle(null, GetFallbackMount(), ResolveShooter()));
                return;
            }

            WeaponDataSO weaponSO = stats.StatsSO.defaultWeapon;
            EquipWeapon(weaponSO);
        }

        private Transform ResolveWeaponMount(WeaponDataSO weaponData)
        {
            if (weaponData == null)
            {
                return GetFallbackMount();
            }

            switch (weaponData.weaponCategory)
            {
                case WeaponCategory.Handgun:
                    if (handgunMount != null)
                    {
                        return handgunMount;
                    }

                    Debug.LogWarning($"[EnemyWeaponVisual] {weaponData.name}은 Handgun 계열이지만 HandgunMount가 비어 있어 WeaponHolder를 사용합니다.", this);
                    return GetFallbackMount();

                case WeaponCategory.AR:
                case WeaponCategory.SMG:
                case WeaponCategory.Sniper:
                case WeaponCategory.Shotgun:
                    if (rifleLikeMount != null)
                    {
                        return rifleLikeMount;
                    }

                    Debug.LogWarning($"[EnemyWeaponVisual] {weaponData.name}은 RifleLike 계열이지만 RifleLikeMount가 비어 있어 WeaponHolder를 사용합니다.", this);
                    return GetFallbackMount();

                case WeaponCategory.Melee:
                    Debug.LogWarning($"[EnemyWeaponVisual] {weaponData.name}은 Melee 계열입니다. Enemy 총기 Mount 분류 대상이 아니므로 WeaponHolder를 사용합니다.", this);
                    return GetFallbackMount();

                default:
                    Debug.LogWarning($"[EnemyWeaponVisual] {weaponData.name}의 무기 카테고리({weaponData.weaponCategory})를 분류할 수 없어 WeaponHolder를 사용합니다.", this);
                    return GetFallbackMount();
            }
        }

        private Transform ResolveMuzzle(Transform spawnedWeaponRoot, Transform selectedMount, EnemyShooter shooter)
        {
            Transform weaponMuzzle = FindDeepChild(spawnedWeaponRoot, muzzlePointName);
            if (weaponMuzzle != null)
            {
                return weaponMuzzle;
            }

            if (spawnedWeaponRoot != null)
            {
                Debug.LogWarning($"[EnemyWeaponVisual] {spawnedWeaponRoot.name}에서 {muzzlePointName} 오브젝트를 찾지 못해 fallback Muzzle을 사용합니다.", this);
            }

            if (fallbackMuzzle != null)
            {
                return fallbackMuzzle;
            }

            if (shooter != null && shooter.CurrentMuzzle != null)
            {
                return shooter.CurrentMuzzle;
            }

            if (selectedMount != null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] fallbackMuzzle과 기존 EnemyShooter muzzle이 없어 선택된 Mount를 임시 발사 기준으로 사용합니다.", this);
                return selectedMount;
            }

            Debug.LogWarning("[EnemyWeaponVisual] 사용할 MuzzlePoint를 찾지 못했습니다. EnemyShooter에는 새 muzzle을 연결하지 않습니다.", this);
            return null;
        }

        private void ConnectMuzzleToShooter(Transform muzzlePoint)
        {
            ConnectMuzzleToShooter(muzzlePoint, ResolveShooter());
        }

        private void ConnectMuzzleToShooter(Transform muzzlePoint, EnemyShooter shooter)
        {
            if (!connectMuzzleToShooter)
            {
                return;
            }

            if (muzzlePoint == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] 연결할 MuzzlePoint가 없어 EnemyShooter muzzle 연결을 건너뜁니다.", this);
                return;
            }

            if (shooter == null)
            {
                Debug.LogWarning("[EnemyWeaponVisual] 현재 오브젝트, 부모, 자식에서 EnemyShooter를 찾지 못했습니다.", this);
                return;
            }

            shooter.SetMuzzle(muzzlePoint);
        }

        private EnemyShooter ResolveShooter()
        {
            EnemyShooter shooter = GetComponentInParent<EnemyShooter>();
            if (shooter != null)
            {
                return shooter;
            }

            return GetComponentInChildren<EnemyShooter>(true);
        }

        private Transform GetFallbackMount()
        {
            if (weaponHolder != null)
            {
                return weaponHolder;
            }

            return transform;
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
            spawnedWeaponRendererDefaultEnabled = null;
            lastAppliedWeaponVisible = null;
        }

        private void CacheSpawnedWeaponRenderers()
        {
            if (spawnedWeapon == null)
            {
                spawnedWeaponRenderers = null;
                spawnedWeaponRendererDefaultEnabled = null;
                lastAppliedWeaponVisible = null;
                return;
            }

            spawnedWeaponRenderers = spawnedWeapon.GetComponentsInChildren<Renderer>(true);
            if (spawnedWeaponRenderers == null || spawnedWeaponRenderers.Length == 0)
            {
                spawnedWeaponRendererDefaultEnabled = null;
                lastAppliedWeaponVisible = null;
                return;
            }

            spawnedWeaponRendererDefaultEnabled = new bool[spawnedWeaponRenderers.Length];
            for (int i = 0; i < spawnedWeaponRenderers.Length; i++)
            {
                spawnedWeaponRendererDefaultEnabled[i] = spawnedWeaponRenderers[i] != null &&
                                                        spawnedWeaponRenderers[i].enabled;
            }

            lastAppliedWeaponVisible = null;
        }

        private void ApplyWeaponRendererVisibility()
        {
            if (spawnedWeapon == null)
            {
                lastAppliedWeaponVisible = null;
                return;
            }

            if (spawnedWeaponRenderers == null)
            {
                CacheSpawnedWeaponRenderers();
            }

            if (spawnedWeaponRenderers == null || spawnedWeaponRenderers.Length == 0)
            {
                lastAppliedWeaponVisible = desiredWeaponVisible;
                return;
            }

            if (lastAppliedWeaponVisible.HasValue &&
                lastAppliedWeaponVisible.Value == desiredWeaponVisible)
            {
                return;
            }

            for (int i = 0; i < spawnedWeaponRenderers.Length; i++)
            {
                Renderer weaponRenderer = spawnedWeaponRenderers[i];
                if (weaponRenderer == null)
                {
                    continue;
                }

                bool defaultEnabled = spawnedWeaponRendererDefaultEnabled == null ||
                                      i >= spawnedWeaponRendererDefaultEnabled.Length ||
                                      spawnedWeaponRendererDefaultEnabled[i];
                weaponRenderer.enabled = desiredWeaponVisible && defaultEnabled;
            }

            lastAppliedWeaponVisible = desiredWeaponVisible;
        }

        /// <summary>
        /// 장착된 적 무기의 Renderer들을 VisionMaskManager에 등록한다.
        /// 적 본체는 CharacterVisual 쪽에서 등록하고, 무기는 런타임 생성물이므로 EnemyWeaponVisual이 별도로 등록한다.
        /// </summary>
        private void RegisterSpawnedWeaponRenderers()
        {
            if (weaponRenderersRegistered || spawnedWeapon == null)
                return;

            if (spawnedWeaponRenderers == null)
            {
                CacheSpawnedWeaponRenderers();
            }

            // 비주얼 교체시 렌더러 재등록(기존 렌더 해제 및 활성 랜더 등록) 과정이 필요합니다.
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

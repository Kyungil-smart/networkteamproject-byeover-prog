using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// EquipmentSlots에 동기화된 현재 장착 무기를 로컬 시각 모델로 표시하고,
    /// 장착 무기 내부 MuzzlePoint를 ShootingSystem의 발사 기준점으로 연결한다.
    /// </summary>
    public class PlayerEquippedWeaponView : MonoBehaviour
    {
        [Header("장착 상태 참조")]
        [Tooltip("현재 장착 무기 ID와 탄창 상태를 읽는 컴포넌트입니다. 비워두면 같은 Player에서 자동 탐색합니다.")]
        [SerializeField] private EquipmentSlots equipmentSlots;

        [Tooltip("장착 무기의 MuzzlePoint를 전달할 발사 시스템입니다. 비워두면 같은 Player에서 자동 탐색합니다.")]
        [SerializeField] private ShootingSystem shootingSystem;

        [Header("무기 표시 위치")]
        [Tooltip("장착 무기 worldPrefab이 생성될 부모 Transform입니다. Player/WeaponHolder를 연결합니다.")]
        [SerializeField] private Transform weaponHolder;

        [Tooltip("장착 무기에서 MuzzlePoint를 찾지 못했거나 무기를 해제했을 때 사용할 기본 총구 Transform입니다.")]
        [SerializeField] private Transform fallbackMuzzle;

        [Header("무기 프리팹 탐색")]
        [Tooltip("장착 무기 프리팹 내부에서 총구로 사용할 자식 Transform 이름입니다.")]
        [SerializeField] private string muzzlePointName = "MuzzlePoint";

        [Tooltip("생성한 무기 모델의 localPosition/localRotation/localScale을 WeaponHolder 기준 기본값으로 맞춥니다.")]
        [SerializeField] private bool resetLocalTransform = true;

        [Header("동작 옵션")]
        [Tooltip("필수 참조가 비어 있으면 같은 Player 하위에서 자동으로 찾습니다.")]
        [SerializeField] private bool autoBindReferences = true;

        [Tooltip("장착 무기 표시와 MuzzlePoint 연결 과정을 Console에 출력합니다.")]
        [SerializeField] private bool showDebugLogs;

        private GameObject currentWeaponInstance;
        private string displayedWeaponId;
        private bool subscribedToEquipment;

        private void Awake()
        {
            AutoBindReferences();
        }

        private void OnEnable()
        {
            AutoBindReferences();
            SubscribeEquipmentEvents();
            RefreshEquippedWeaponView();
        }

        private void OnDisable()
        {
            UnsubscribeEquipmentEvents();
            ClearCurrentWeaponView();
        }

        private void Update()
        {
            if (autoBindReferences && !HasRequiredReferences())
            {
                AutoBindReferences();
                SubscribeEquipmentEvents();
            }

            if (equipmentSlots == null)
                return;

            string currentWeaponId = equipmentSlots.CurrentEquipped.Value.ToString();
            if (currentWeaponId != displayedWeaponId ||
                (!string.IsNullOrWhiteSpace(currentWeaponId) && currentWeaponInstance == null))
            {
                RefreshEquippedWeaponView();
            }
        }

        public void RefreshEquippedWeaponView()
        {
            AutoBindReferences();

            if (equipmentSlots == null)
            {
                ClearCurrentWeaponView();
                return;
            }

            string weaponId = equipmentSlots.CurrentEquipped.Value.ToString();
            if (string.IsNullOrWhiteSpace(weaponId))
            {
                ClearCurrentWeaponView();
                return;
            }

            WeaponDataSO weaponData = equipmentSlots.CurrentWeaponData;
            if (weaponData == null)
            {
                ClearCurrentWeaponView();
                displayedWeaponId = weaponId;
                LogDebug($"장착 무기 데이터를 찾지 못했습니다. weaponId={weaponId}");
                return;
            }

            if (displayedWeaponId == weaponId && currentWeaponInstance != null)
                return;

            CreateWeaponView(weaponData, weaponId);
        }

        private void CreateWeaponView(WeaponDataSO weaponData, string weaponId)
        {
            ClearCurrentWeaponView();
            displayedWeaponId = weaponId;

            if (weaponHolder == null)
            {
                LogDebug("WeaponHolder가 연결되어 있지 않아 무기 모델을 표시할 수 없습니다.");
                ApplyFallbackMuzzle();
                return;
            }

            if (weaponData.worldPrefab == null)
            {
                LogDebug($"worldPrefab이 비어 있어 무기 모델을 표시하지 않습니다. weaponId={weaponId}");
                ApplyFallbackMuzzle();
                return;
            }

            currentWeaponInstance = Instantiate(weaponData.worldPrefab, weaponHolder, false);
            currentWeaponInstance.name = $"{weaponData.worldPrefab.name}_EquippedView";

            if (resetLocalTransform)
            {
                Transform weaponTransform = currentWeaponInstance.transform;
                weaponTransform.localPosition = Vector3.zero;
                weaponTransform.localRotation = Quaternion.identity;
                weaponTransform.localScale = Vector3.one;
            }

            if (currentWeaponInstance.TryGetComponent<NetworkObject>(out _) && showDebugLogs)
            {
                Debug.LogWarning(
                    $"[PlayerEquippedWeaponView] {weaponData.worldPrefab.name}에 NetworkObject가 있습니다. " +
                    "장착 무기 모델은 로컬 시각 요소로만 생성하며 Spawn하지 않습니다.",
                    this);
            }

            Transform muzzle = FindChildRecursive(currentWeaponInstance.transform, muzzlePointName);
            if (muzzle != null)
            {
                shootingSystem?.SetMuzzleTransform(muzzle);
                LogDebug($"장착 무기 표시 완료: {weaponId}, muzzle={muzzle.name}");
                return;
            }

            LogDebug($"장착 무기에서 {muzzlePointName}을 찾지 못해 fallback muzzle을 사용합니다. weaponId={weaponId}");
            ApplyFallbackMuzzle();
        }

        private void ClearCurrentWeaponView()
        {
            if (currentWeaponInstance != null)
            {
                if (Application.isPlaying)
                    Destroy(currentWeaponInstance);
                else
                    DestroyImmediate(currentWeaponInstance);
            }

            currentWeaponInstance = null;
            displayedWeaponId = string.Empty;
            ApplyFallbackMuzzle();
        }

        private void ApplyFallbackMuzzle()
        {
            if (shootingSystem == null)
                return;

            if (fallbackMuzzle != null)
                shootingSystem.SetMuzzleTransform(fallbackMuzzle);
            else
                shootingSystem.ResetMuzzleTransform();
        }

        private void AutoBindReferences()
        {
            if (!autoBindReferences)
                return;

            if (equipmentSlots == null)
                equipmentSlots = GetComponent<EquipmentSlots>();

            if (shootingSystem == null)
                shootingSystem = GetComponent<ShootingSystem>();

            if (weaponHolder == null)
                weaponHolder = transform.Find("WeaponHolder");

            if (fallbackMuzzle == null && weaponHolder != null)
                fallbackMuzzle = weaponHolder.Find("MuzzlePoint");
        }

        private bool HasRequiredReferences()
        {
            return equipmentSlots != null &&
                   shootingSystem != null &&
                   weaponHolder != null;
        }

        private void SubscribeEquipmentEvents()
        {
            if (equipmentSlots == null || subscribedToEquipment)
                return;
            
            equipmentSlots.CurrentEquipped.OnValueChanged += OnCurrentEquippedChanged;
            equipmentSlots.Primary1Id.OnValueChanged += OnWeaponSlotIdChanged;
            equipmentSlots.Primary2Id.OnValueChanged += OnWeaponSlotIdChanged;
            equipmentSlots.SecondaryId.OnValueChanged += OnWeaponSlotIdChanged;
            equipmentSlots.MeleeId.OnValueChanged += OnWeaponSlotIdChanged;
            subscribedToEquipment = true;
        }

        private void UnsubscribeEquipmentEvents()
        {
            if (equipmentSlots == null || !subscribedToEquipment)
                return;

            equipmentSlots.CurrentEquipped.OnValueChanged -= OnCurrentEquippedChanged;
            equipmentSlots.Primary1Id.OnValueChanged -= OnWeaponSlotIdChanged;
            equipmentSlots.Primary2Id.OnValueChanged -= OnWeaponSlotIdChanged;
            equipmentSlots.SecondaryId.OnValueChanged -= OnWeaponSlotIdChanged;
            equipmentSlots.MeleeId.OnValueChanged -= OnWeaponSlotIdChanged;
            subscribedToEquipment = false;
        }

        private void OnCurrentEquippedChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquippedWeaponView();
        }

        private void OnWeaponSlotIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquippedWeaponView();
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            if (root.name == targetName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void LogDebug(string message)
        {
            if (!showDebugLogs)
                return;

            Debug.Log($"[PlayerEquippedWeaponView] {message}", this);
        }
    }
}

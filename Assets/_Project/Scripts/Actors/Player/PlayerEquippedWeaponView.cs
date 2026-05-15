using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// EquipmentSlots에 동기화된 현재 장착 무기를 로컬 시각 모델로 표시하고,
    /// 장착 무기 안의 MuzzlePoint를 ShootingSystem의 발사 기준으로 연결한다.
    /// Roll / Knocked / Dead 상태에서는 장착 데이터는 유지한 채 무기 모델 Root만 숨긴다.
    /// </summary>
    public class PlayerEquippedWeaponView : MonoBehaviour
    {
        [Header("장착 상태 참조")]
        [Tooltip("현재 장착 무기 ID와 슬롯 상태를 읽는 컴포넌트입니다. 비워두면 같은 Player에서 자동 검색합니다.")]
        [SerializeField] private EquipmentSlots equipmentSlots;

        [Tooltip("장착 무기의 MuzzlePoint를 전달할 발사 시스템입니다. 비워두면 같은 Player에서 자동 검색합니다.")]
        [SerializeField] private ShootingSystem shootingSystem;

        [Tooltip("Knocked / Dead 상태를 읽는 체력 상태 컴포넌트입니다. 비워두면 같은 Player에서 자동 검색합니다.")]
        [SerializeField] private PlayerHealthSystem playerHealthSystem;

        [Header("무기 표시 위치")]
        [Tooltip("장착 무기 worldPrefab을 생성할 기본 부모 Transform입니다. 기존 Player/WeaponHolder를 연결합니다.")]
        [SerializeField] private Transform weaponHolder;

        [Tooltip("AR, SMG, Sniper, Shotgun 계열 무기가 생성될 부모 Transform입니다. 비어 있으면 WeaponHolder를 사용합니다.")]
        [SerializeField] private Transform rifleLikeMount;

        [Tooltip("Handgun 계열 무기가 생성될 부모 Transform입니다. 비어 있으면 WeaponHolder를 사용합니다.")]
        [SerializeField] private Transform handgunMount;

        [Tooltip("장착 무기에서 MuzzlePoint를 찾지 못했거나 무기가 없을 때 사용할 기본 총구 Transform입니다.")]
        [SerializeField] private Transform fallbackMuzzle;

        [Header("상태 기반 비주얼 숨김")]
        [Tooltip("Roll 상태를 읽을 Animator입니다. 비워두면 같은 Player에서 자동 검색합니다.")]
        [SerializeField] private Animator animator;

        [Tooltip("Roll 상태를 나타내는 Animator Bool 파라미터 이름입니다. 원격 플레이어는 NetworkAnimator로 동기화된 값을 기준으로 판단합니다.")]
        [SerializeField] private string rollingParameterName = "IsRolling";

        [Header("무기 프리팹 검색")]
        [Tooltip("장착 무기 프리팹 내부에서 총구로 사용할 자식 Transform 이름입니다.")]
        [SerializeField] private string muzzlePointName = "MuzzlePoint";

        [Tooltip("생성된 무기 모델의 localPosition/localRotation/localScale을 선택한 Mount 기준 기본값으로 맞춥니다.")]
        [SerializeField] private bool resetLocalTransform = true;

        [Header("동작 옵션")]
        [Tooltip("필수 참조가 비어 있으면 같은 Player 하위에서 자동으로 찾습니다.")]
        [SerializeField] private bool autoBindReferences = true;

        [Tooltip("장착 무기 표시와 MuzzlePoint 연결 과정을 Console에 출력합니다.")]
        [SerializeField] private bool showDebugLogs;

        private GameObject currentWeaponInstance;
        private string displayedWeaponId;
        private bool subscribedToEquipment;
        private bool subscribedToPlayerState;

        private int rollingHash;
        private bool hasRollingParameter;
        private Animator cachedAnimatorForRollingParameter;
        private bool? lastAppliedWeaponVisualVisible;

        private void Awake()
        {
            AutoBindReferences();
            CacheAnimatorBindings();
        }

        private void OnEnable()
        {
            AutoBindReferences();
            CacheAnimatorBindings();

            SubscribeEquipmentEvents();
            SubscribePlayerStateEvents();

            RefreshEquippedWeaponView();
        }

        private void OnDisable()
        {
            UnsubscribeEquipmentEvents();
            UnsubscribePlayerStateEvents();

            // 기존 코드에도 있던 동작이다. 컴포넌트가 비활성화되면 로컬 시각 모델도 정리한다.
            ClearCurrentWeaponView();
        }

        private void OnDestroy()
        {
            UnsubscribeEquipmentEvents();
            UnsubscribePlayerStateEvents();
        }

        private void Update()
        {
            if (equipmentSlots == null)
                return;

            string currentWeaponId = equipmentSlots.CurrentEquipped.Value.ToString();
            if (currentWeaponId != displayedWeaponId ||
                (!string.IsNullOrWhiteSpace(currentWeaponId) && currentWeaponInstance == null))
            {
                RefreshEquippedWeaponView();
                return;
            }

            // Roll은 짧은 시간 동안 바뀌는 시각 상태이므로 매 프레임 표시 여부만 갱신한다.
            RefreshWeaponVisualVisibility();
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
            {
                RefreshWeaponVisualVisibility();
                return;
            }

            CreateWeaponView(weaponData, weaponId);
        }

        private void CreateWeaponView(WeaponDataSO weaponData, string weaponId)
        {
            ClearCurrentWeaponView();
            displayedWeaponId = weaponId;

            Transform selectedMount = ResolveWeaponMount(weaponData);
            if (selectedMount == null)
            {
                LogDebug("장착 무기 Mount가 연결되어 있지 않아 무기 모델을 표시하지 않습니다.");
                ApplyFallbackMuzzle();
                return;
            }

            if (weaponData.worldPrefab == null)
            {
                LogDebug($"worldPrefab이 비어 있어 무기 모델을 표시하지 않습니다. weaponId={weaponId}");
                ApplyFallbackMuzzle();
                return;
            }

            currentWeaponInstance = Instantiate(weaponData.worldPrefab, selectedMount, false);
            currentWeaponInstance.name = $"{weaponData.worldPrefab.name}_EquippedView";
            lastAppliedWeaponVisualVisible = null;

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
                LogDebug($"장착 무기 표시 완료: {weaponId}, mount={selectedMount.name}, muzzle={muzzle.name}");
            }
            else
            {
                LogDebug($"장착 무기에서 {muzzlePointName}을 찾지 못해 fallback muzzle을 사용합니다. weaponId={weaponId}");
                ApplyFallbackMuzzle();
            }

            RefreshWeaponVisualVisibility();
        }

        private Transform ResolveWeaponMount(WeaponDataSO weaponData)
        {
            if (weaponData == null)
                return weaponHolder;

            switch (weaponData.weaponCategory)
            {
                case WeaponCategory.AR:
                case WeaponCategory.SMG:
                case WeaponCategory.Sniper:
                case WeaponCategory.Shotgun:
                    return rifleLikeMount != null ? rifleLikeMount : weaponHolder;

                case WeaponCategory.Handgun:
                    return handgunMount != null ? handgunMount : weaponHolder;

                default:
                    return weaponHolder;
            }
        }

        private void RefreshWeaponVisualVisibility()
        {
            if (currentWeaponInstance == null)
            {
                lastAppliedWeaponVisualVisible = null;
                return;
            }

            bool visible = ShouldShowWeaponVisual();

            if (lastAppliedWeaponVisualVisible.HasValue &&
                lastAppliedWeaponVisualVisible.Value == visible &&
                currentWeaponInstance.activeSelf == visible)
            {
                return;
            }

            currentWeaponInstance.SetActive(visible);
            lastAppliedWeaponVisualVisible = visible;
        }

        private bool ShouldShowWeaponVisual()
        {
            if (IsKnockedOrDead())
                return false;

            if (IsRollingForVisual())
                return false;

            return true;
        }

        private bool IsKnockedOrDead()
        {
            if (playerHealthSystem == null)
                return false;

            PlayerState state = playerHealthSystem.State.Value;
            return state == PlayerState.Knocked || state == PlayerState.Dead;
        }

        private bool IsRollingForVisual()
        {
            if (animator == null || !hasRollingParameter)
                return false;

            return animator.GetBool(rollingHash);
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
            lastAppliedWeaponVisualVisible = null;
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

            if (playerHealthSystem == null)
                playerHealthSystem = GetComponent<PlayerHealthSystem>();

            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (weaponHolder == null)
                weaponHolder = transform.Find("WeaponHolder");

            if (weaponHolder != null)
            {
                if (rifleLikeMount == null)
                    rifleLikeMount = weaponHolder.Find("RifleLikeMount");

                if (handgunMount == null)
                    handgunMount = weaponHolder.Find("HandgunMount");

                if (fallbackMuzzle == null)
                    fallbackMuzzle = weaponHolder.Find("MuzzlePoint");
            }
        }

        private void CacheAnimatorBindings()
        {
            if (cachedAnimatorForRollingParameter == animator)
                return;

            cachedAnimatorForRollingParameter = animator;
            hasRollingParameter = false;
            rollingHash = 0;

            if (animator == null || string.IsNullOrWhiteSpace(rollingParameterName))
                return;

            rollingHash = Animator.StringToHash(rollingParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != rollingParameterName)
                    continue;

                hasRollingParameter = parameter.type == AnimatorControllerParameterType.Bool;

                if (!hasRollingParameter)
                {
                    LogDebug($"Animator 파라미터 '{rollingParameterName}' 타입이 Bool이 아니어서 Roll 기준 무기 숨김을 적용하지 않습니다.");
                }

                return;
            }

            LogDebug($"Animator에서 Bool 파라미터 '{rollingParameterName}'를 찾지 못해 Roll 기준 무기 숨김을 적용하지 않습니다.");
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

        private void SubscribePlayerStateEvents()
        {
            if (playerHealthSystem == null || subscribedToPlayerState)
                return;

            playerHealthSystem.State.OnValueChanged += OnPlayerStateChanged;
            subscribedToPlayerState = true;
        }

        private void UnsubscribePlayerStateEvents()
        {
            if (playerHealthSystem == null || !subscribedToPlayerState)
                return;

            playerHealthSystem.State.OnValueChanged -= OnPlayerStateChanged;
            subscribedToPlayerState = false;
        }

        private void OnCurrentEquippedChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquippedWeaponView();
        }

        private void OnWeaponSlotIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquippedWeaponView();
        }

        private void OnPlayerStateChanged(PlayerState previousState, PlayerState newState)
        {
            RefreshWeaponVisualVisibility();
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

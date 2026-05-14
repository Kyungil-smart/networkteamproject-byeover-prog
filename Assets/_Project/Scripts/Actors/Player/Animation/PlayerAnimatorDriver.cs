using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// EquipmentSlots에 동기화된 장착 상태를 Player Animator 파라미터로 변환한다.
    /// 장착 데이터 자체는 변경하지 않고, 각 클라이언트에서 표시용 Animator 상태만 갱신한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerAnimatorDriver : NetworkBehaviour
    {
        [Header("애니메이션 참조")]
        [Tooltip("WeaponType 파라미터와 상체 전투 레이어 Weight를 갱신할 Animator입니다. " +
                 "비워두면 같은 Player에서 자동 탐색합니다.")]
        [SerializeField] private Animator animator;

        [Tooltip("현재 장착 무기 ID와 슬롯 ID를 읽는 장비 슬롯 컴포넌트입니다. " +
                 "비워두면 같은 Player에서 자동 탐색합니다.")]
        [SerializeField] private EquipmentSlots equipmentSlots;

        [Tooltip("기절/사망 상태를 읽는 플레이어 체력 상태 컴포넌트입니다. " +
                 "비워두면 같은 Player에서 자동 탐색합니다.")]
        [SerializeField] private PlayerHealthSystem playerHealthSystem;

        [Header("Animator 파라미터")]
        [Tooltip("Player Animator Controller에 있는 Int 파라미터 이름입니다. " +
                 "PlayerWeaponAnimationType 값과 대응됩니다.")]
        [SerializeField] private string weaponTypeParameterName = "WeaponType";

        [Tooltip("구르기 상태를 읽을 Bool 파라미터 이름입니다. " +
                 "이 값이 true이면 상체 전투 레이어 Weight를 0으로 낮춥니다.")]
        [SerializeField] private string rollingParameterName = "IsRolling";

        [Tooltip("발사 애니메이션을 재생할 Trigger 파라미터 이름입니다.")]
        [SerializeField] private string fireTriggerParameterName = "Fire";

        [Tooltip("피격 반응 애니메이션을 재생할 Trigger 파라미터 이름입니다.")]
        [SerializeField] private string hitTriggerParameterName = "Hit";

        [Tooltip("재장전 애니메이션 상태를 표시할 Bool 파라미터 이름입니다.")]
        [SerializeField] private string reloadingParameterName = "IsReloading";

        [Tooltip("무기 장착 시 Weight를 올릴 상체 전투 레이어 이름입니다. " +
                 "레이어가 없으면 Weight 갱신만 건너뜁니다.")]
        [SerializeField] private string combatLayerName = "Combat Upper Body";

        [Tooltip("활성화하면 장착 무기 타입에 따라 상체 전투 레이어 Weight를 0 또는 1로 보간합니다.")]
        [SerializeField] private bool useCombatLayerWeight = true;

        [Tooltip("상체 전투 레이어 Weight가 목표값으로 이동하는 속도입니다. 0 이하이면 즉시 반영합니다.")]
        [SerializeField, Min(0f)] private float layerBlendSpeed = 12f;

        [Header("동작 옵션")]
        [Tooltip("필수 참조가 비어 있으면 같은 Player 오브젝트에서 자동으로 찾습니다.")]
        [SerializeField] private bool autoBindReferences = true;

        [Tooltip("장착 무기 애니메이션 타입 계산과 레이어 연결 상태를 Console에 출력합니다.")]
        [SerializeField] private bool showDebugLogs = false;

        private PlayerWeaponAnimationType currentWeaponType = PlayerWeaponAnimationType.Unarmed;
        private float targetCombatLayerWeight;
        private float currentCombatLayerWeight;
        private bool isReloadingAnimation;

        private int weaponTypeHash;
        private int rollingHash;
        private int fireTriggerHash;
        private int hitTriggerHash;
        private int reloadingHash;
        private int combatLayerIndex = -1;

        private bool hasWeaponTypeParameter;
        private bool hasRollingParameter;
        private bool hasFireTriggerParameter;
        private bool hasHitTriggerParameter;
        private bool hasReloadingParameter;
        private bool subscribedToEquipment;

        private bool loggedMissingAnimator;
        private bool loggedMissingEquipmentSlots;
        private bool loggedMissingPlayerHealthSystem;
        private bool loggedMissingWeaponTypeParameter;
        private bool loggedMissingRollingParameter;
        private bool loggedMissingFireTriggerParameter;
        private bool loggedMissingHitTriggerParameter;
        private bool loggedMissingReloadingParameter;
        private bool loggedWeaponTypeMismatch;
        private bool loggedRollingParameterMismatch;
        private bool loggedFireTriggerParameterMismatch;
        private bool loggedHitTriggerParameterMismatch;
        private bool loggedReloadingParameterMismatch;
        private bool loggedMissingCombatLayer;

        private void Awake()
        {
            AutoBindReferences();
            CacheAnimatorBindings();
        }

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            layerBlendSpeed = Mathf.Max(0f, layerBlendSpeed);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            AutoBindReferences();
            CacheAnimatorBindings();
            SubscribeEquipmentEvents();

            RefreshWeaponAnimationState(immediateLayerWeight: true);
            UpdateReloadingParameter();
        }

        public override void OnNetworkDespawn()
        {
            SetReloadingAnimation(false);
            UnsubscribeEquipmentEvents();
            base.OnNetworkDespawn();
        }

        private void OnEnable()
        {
            if (!IsSpawned)
                return;

            AutoBindReferences();
            CacheAnimatorBindings();
            SubscribeEquipmentEvents();
            RefreshWeaponAnimationState(immediateLayerWeight: true);
            UpdateReloadingParameter();
        }

        private void OnDisable()
        {
            UnsubscribeEquipmentEvents();
        }

        private void Update()
        {
            UpdateCombatLayerWeight();
            UpdateReloadingParameter();
        }

        /// <summary>
        /// 서버에서 확정된 발사 액션을 현재 로컬 Animator에 반영한다.
        /// Roll, Knocked, Dead, Reload 중에는 상체 전투 자세가 충돌하므로 발사 Trigger를 실행하지 않는다.
        /// </summary>
        public void TriggerFireAnimation()
        {
            if (animator == null)
                return;

            if (!hasFireTriggerParameter)
                return;

            if (!CanPlayFireAnimation())
                return;

            animator.ResetTrigger(fireTriggerHash);
            animator.SetTrigger(fireTriggerHash);
        }

        /// <summary>
        /// 서버에서 확정된 실제 피해를 현재 로컬 Animator에 반영한다.
        /// 재장전/발사 중에는 피격 반응이 끼어들 수 있지만, Roll/Knocked/Dead 상태에서는 기존 특수 상태를 우선한다.
        /// </summary>
        public void TriggerHitReaction()
        {
            if (animator == null)
                return;

            if (!hasHitTriggerParameter)
                return;

            if (!CanPlayHitReaction())
                return;

            animator.ResetTrigger(hitTriggerHash);
            animator.SetTrigger(hitTriggerHash);
        }

        /// <summary>
        /// 서버에서 확정된 재장전 표시 상태를 현재 로컬 Animator에 반영한다.
        /// 실제 탄약 처리와 장전 성공 여부는 ReloadSystem/GridInventory가 담당하고,
        /// 이 함수는 표시용 IsReloading Bool만 관리한다.
        /// </summary>
        public void SetReloadingAnimation(bool isReloading)
        {
            bool nextReloading = isReloading && IsSupportedWeaponActionType();
            isReloadingAnimation = nextReloading;

            if (nextReloading && hasFireTriggerParameter && animator != null)
                animator.ResetTrigger(fireTriggerHash);

            UpdateReloadingParameter();
        }

        private void AutoBindReferences()
        {
            if (!autoBindReferences)
                return;

            if (animator == null)
                animator = GetComponent<Animator>();

            if (equipmentSlots == null)
                equipmentSlots = GetComponent<EquipmentSlots>();

            if (playerHealthSystem == null)
                playerHealthSystem = GetComponent<PlayerHealthSystem>();
        }

        private void CacheAnimatorBindings()
        {
            hasWeaponTypeParameter = false;
            hasRollingParameter = false;
            hasFireTriggerParameter = false;
            hasHitTriggerParameter = false;
            hasReloadingParameter = false;
            weaponTypeHash = 0;
            rollingHash = 0;
            fireTriggerHash = 0;
            hitTriggerHash = 0;
            reloadingHash = 0;
            combatLayerIndex = -1;

            if (animator == null)
            {
                WarnOnce(ref loggedMissingAnimator,
                    "[PlayerAnimatorDriver] Animator 참조가 없어 WeaponType을 갱신할 수 없습니다.");
                return;
            }

            CacheWeaponTypeParameter();
            CacheRollingParameter();
            CacheFireTriggerParameter();
            CacheHitTriggerParameter();
            CacheReloadingParameter();
            CacheCombatLayer();

            if (playerHealthSystem == null)
            {
                WarnOnce(ref loggedMissingPlayerHealthSystem,
                    "[PlayerAnimatorDriver] PlayerHealthSystem 참조가 없어 기절/사망 중 상체 레이어 억제를 적용할 수 없습니다.");
            }
        }

        private void CacheWeaponTypeParameter()
        {
            if (string.IsNullOrWhiteSpace(weaponTypeParameterName))
            {
                WarnOnce(ref loggedMissingWeaponTypeParameter,
                    "[PlayerAnimatorDriver] WeaponType 파라미터 이름이 비어 있습니다.");
                return;
            }

            weaponTypeHash = Animator.StringToHash(weaponTypeParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != weaponTypeParameterName)
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Int)
                {
                    WarnOnce(
                        ref loggedWeaponTypeMismatch,
                        $"[PlayerAnimatorDriver] Animator 파라미터 '{weaponTypeParameterName}'" +
                        $" 타입이 Int가 아닙니다. 현재 타입: {parameter.type}");

                    return;
                }

                hasWeaponTypeParameter = true;
                return;
            }

            WarnOnce(
                ref loggedMissingWeaponTypeParameter,
                $"[PlayerAnimatorDriver] Animator에서 Int 파라미터 '{weaponTypeParameterName}'를 찾지 못했습니다.");
        }

        private void CacheRollingParameter()
        {
            if (string.IsNullOrWhiteSpace(rollingParameterName))
            {
                WarnOnce(ref loggedMissingRollingParameter,
                    "[PlayerAnimatorDriver] 구르기 파라미터 이름이 비어 있습니다.");
                return;
            }

            rollingHash = Animator.StringToHash(rollingParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != rollingParameterName)
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Bool)
                {
                    WarnOnce(
                        ref loggedRollingParameterMismatch,
                        $"[PlayerAnimatorDriver] Animator 파라미터 '{rollingParameterName}'" +
                        $" 타입이 Bool이 아닙니다. 현재 타입: {parameter.type}");

                    return;
                }

                hasRollingParameter = true;
                return;
            }

            WarnOnce(
                ref loggedMissingRollingParameter,
                $"[PlayerAnimatorDriver] Animator에서 Bool 파라미터 '{rollingParameterName}'를 찾지 못했습니다.");
        }

        private void CacheFireTriggerParameter()
        {
            if (string.IsNullOrWhiteSpace(fireTriggerParameterName))
            {
                WarnOnce(ref loggedMissingFireTriggerParameter,
                    "[PlayerAnimatorDriver] Fire Trigger 파라미터 이름이 비어 있습니다.");
                return;
            }

            fireTriggerHash = Animator.StringToHash(fireTriggerParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != fireTriggerParameterName)
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Trigger)
                {
                    WarnOnce(
                        ref loggedFireTriggerParameterMismatch,
                        $"[PlayerAnimatorDriver] Animator 파라미터 '{fireTriggerParameterName}'" +
                        $" 타입이 Trigger가 아닙니다. 현재 타입: {parameter.type}");

                    return;
                }

                hasFireTriggerParameter = true;
                return;
            }

            WarnOnce(
                ref loggedMissingFireTriggerParameter,
                $"[PlayerAnimatorDriver] Animator에서 Trigger 파라미터 '{fireTriggerParameterName}'를 찾지 못했습니다.");
        }

        private void CacheHitTriggerParameter()
        {
            if (string.IsNullOrWhiteSpace(hitTriggerParameterName))
            {
                WarnOnce(ref loggedMissingHitTriggerParameter,
                    "[PlayerAnimatorDriver] Hit Trigger 파라미터 이름이 비어 있습니다.");
                return;
            }

            hitTriggerHash = Animator.StringToHash(hitTriggerParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != hitTriggerParameterName)
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Trigger)
                {
                    WarnOnce(
                        ref loggedHitTriggerParameterMismatch,
                        $"[PlayerAnimatorDriver] Animator 파라미터 '{hitTriggerParameterName}'" +
                        $" 타입이 Trigger가 아닙니다. 현재 타입: {parameter.type}");

                    return;
                }

                hasHitTriggerParameter = true;
                return;
            }

            WarnOnce(
                ref loggedMissingHitTriggerParameter,
                $"[PlayerAnimatorDriver] Animator에서 Trigger 파라미터 '{hitTriggerParameterName}'를 찾지 못했습니다.");
        }

        private void CacheReloadingParameter()
        {
            if (string.IsNullOrWhiteSpace(reloadingParameterName))
            {
                WarnOnce(ref loggedMissingReloadingParameter,
                    "[PlayerAnimatorDriver] IsReloading 파라미터 이름이 비어 있습니다.");
                return;
            }

            reloadingHash = Animator.StringToHash(reloadingParameterName);

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.name != reloadingParameterName)
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Bool)
                {
                    WarnOnce(
                        ref loggedReloadingParameterMismatch,
                        $"[PlayerAnimatorDriver] Animator 파라미터 '{reloadingParameterName}'" +
                        $" 타입이 Bool이 아닙니다. 현재 타입: {parameter.type}");

                    return;
                }

                hasReloadingParameter = true;
                return;
            }

            WarnOnce(
                ref loggedMissingReloadingParameter,
                $"[PlayerAnimatorDriver] Animator에서 Bool 파라미터 '{reloadingParameterName}'를 찾지 못했습니다.");
        }

        private void CacheCombatLayer()
        {
            if (!useCombatLayerWeight)
                return;

            if (string.IsNullOrWhiteSpace(combatLayerName))
                return;

            combatLayerIndex = animator.GetLayerIndex(combatLayerName);

            if (combatLayerIndex < 0)
            {
                if (!loggedMissingCombatLayer)
                {
                    LogDebug($"Animator Layer '{combatLayerName}'를 찾지 못했습니다. Layer Weight 갱신은 건너뜁니다.");
                    loggedMissingCombatLayer = true;
                }

                return;
            }

            currentCombatLayerWeight = animator.GetLayerWeight(combatLayerIndex);
            loggedMissingCombatLayer = false;
        }

        private void SubscribeEquipmentEvents()
        {
            if (equipmentSlots == null)
            {
                WarnOnce(ref loggedMissingEquipmentSlots,
                    "[PlayerAnimatorDriver] EquipmentSlots 참조가 없어 장착 상태를 읽을 수 없습니다.");
                return;
            }

            if (subscribedToEquipment)
                return;

            // 장착 상태의 원천은 EquipmentSlots의 NetworkVariable이다.
            // Owner만 처리하면 원격 플레이어의 자세가 갱신되지 않으므로 모든 클라이언트에서 로컬 Animator를 맞춘다.
            equipmentSlots.CurrentEquipped.OnValueChanged += OnEquipmentSlotValueChanged;
            equipmentSlots.Primary1Id.OnValueChanged += OnEquipmentSlotValueChanged;
            equipmentSlots.Primary2Id.OnValueChanged += OnEquipmentSlotValueChanged;
            equipmentSlots.SecondaryId.OnValueChanged += OnEquipmentSlotValueChanged;
            equipmentSlots.MeleeId.OnValueChanged += OnEquipmentSlotValueChanged;

            subscribedToEquipment = true;
        }

        private void UnsubscribeEquipmentEvents()
        {
            if (equipmentSlots == null || !subscribedToEquipment)
                return;

            equipmentSlots.CurrentEquipped.OnValueChanged -= OnEquipmentSlotValueChanged;
            equipmentSlots.Primary1Id.OnValueChanged -= OnEquipmentSlotValueChanged;
            equipmentSlots.Primary2Id.OnValueChanged -= OnEquipmentSlotValueChanged;
            equipmentSlots.SecondaryId.OnValueChanged -= OnEquipmentSlotValueChanged;
            equipmentSlots.MeleeId.OnValueChanged -= OnEquipmentSlotValueChanged;

            subscribedToEquipment = false;
        }

        private void OnEquipmentSlotValueChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshWeaponAnimationState(immediateLayerWeight: false);
        }

        private void RefreshWeaponAnimationState(bool immediateLayerWeight)
        {
            PlayerWeaponAnimationType nextWeaponType = ResolveCurrentWeaponAnimationType();
            ApplyWeaponAnimationType(nextWeaponType, immediateLayerWeight);
        }

        private PlayerWeaponAnimationType ResolveCurrentWeaponAnimationType()
        {
            if (equipmentSlots == null)
                return PlayerWeaponAnimationType.Unarmed;

            FixedString64Bytes currentEquipped = equipmentSlots.CurrentEquipped.Value;
            if (currentEquipped.Length == 0)
                return PlayerWeaponAnimationType.Unarmed;

            // CurrentEquipped는 네트워크로 동기화된 현재 선택 값이지만,
            // 실제 슬롯 ID와 맞지 않으면 오래된 장착 값으로 보고 비무장 처리한다.
            if (!IsCurrentEquippedInAnyWeaponSlot(currentEquipped))
            {
                LogDebug($"CurrentEquipped가 실제 슬롯에 없습니다. weaponId={currentEquipped}");
                return PlayerWeaponAnimationType.Unarmed;
            }

            WeaponDataSO weaponData = equipmentSlots.Lookup<WeaponDataSO>(currentEquipped.ToString());
            if (weaponData == null)
            {
                LogDebug($"WeaponDataSO를 찾지 못했습니다. weaponId={currentEquipped}");
                return PlayerWeaponAnimationType.Unarmed;
            }

            return ResolveWeaponAnimationType(weaponData);
        }

        private bool IsCurrentEquippedInAnyWeaponSlot(FixedString64Bytes currentEquipped)
        {
            if (currentEquipped.Length == 0)
                return false;

            return IsSameOccupiedSlot(equipmentSlots.Primary1Id.Value, currentEquipped) ||
                   IsSameOccupiedSlot(equipmentSlots.Primary2Id.Value, currentEquipped) ||
                   IsSameOccupiedSlot(equipmentSlots.SecondaryId.Value, currentEquipped) ||
                   IsSameOccupiedSlot(equipmentSlots.MeleeId.Value, currentEquipped);
        }

        private static bool IsSameOccupiedSlot(FixedString64Bytes slotId, FixedString64Bytes currentEquipped)
        {
            return slotId.Length > 0 && slotId == currentEquipped;
        }

        private static PlayerWeaponAnimationType ResolveWeaponAnimationType(WeaponDataSO weaponData)
        {
            if (weaponData == null)
                return PlayerWeaponAnimationType.Unarmed;

            switch (weaponData.weaponCategory)
            {
                case WeaponCategory.AR:
                case WeaponCategory.SMG:
                case WeaponCategory.Sniper:
                case WeaponCategory.Shotgun:
                    return PlayerWeaponAnimationType.RifleLike;

                case WeaponCategory.Handgun:
                    return PlayerWeaponAnimationType.Handgun;

                case WeaponCategory.Melee:
                    return PlayerWeaponAnimationType.Melee;

                default:
                    return PlayerWeaponAnimationType.Unarmed;
            }
        }

        private void ApplyWeaponAnimationType(PlayerWeaponAnimationType nextWeaponType, bool immediateLayerWeight)
        {
            bool changed = currentWeaponType != nextWeaponType;
            currentWeaponType = nextWeaponType;

            targetCombatLayerWeight = nextWeaponType == PlayerWeaponAnimationType.Unarmed ? 0f : 1f;

            SetWeaponTypeParameter(nextWeaponType);

            if (!IsSupportedWeaponActionType())
                isReloadingAnimation = false;

            UpdateReloadingParameter();

            if (immediateLayerWeight)
            {
                ApplyCombatLayerWeight(GetEffectiveCombatLayerWeight());
            }

            if (changed)
            {
                LogDebug($"WeaponType 변경: {nextWeaponType}, layerWeightTarget={targetCombatLayerWeight}");
            }
        }

        private void SetWeaponTypeParameter(PlayerWeaponAnimationType weaponType)
        {
            if (animator == null)
                return;

            if (!hasWeaponTypeParameter)
                return;

            animator.SetInteger(weaponTypeHash, (int)weaponType);
        }

        private void UpdateCombatLayerWeight()
        {
            if (!useCombatLayerWeight)
                return;

            if (animator == null || combatLayerIndex < 0)
                return;

            float effectiveTargetWeight = GetEffectiveCombatLayerWeight();
            float nextWeight;

            if (layerBlendSpeed <= 0f)
            {
                nextWeight = effectiveTargetWeight;
            }
            else
            {
                nextWeight = Mathf.MoveTowards(
                    currentCombatLayerWeight,
                    effectiveTargetWeight,
                    layerBlendSpeed * Time.deltaTime);
            }

            if (Mathf.Approximately(currentCombatLayerWeight, nextWeight))
                return;

            ApplyCombatLayerWeight(nextWeight);
        }

        private void UpdateReloadingParameter()
        {
            if (animator == null || !hasReloadingParameter)
                return;

            bool shouldShowReloading = isReloadingAnimation && CanPlayWeaponAction();

            if (animator.GetBool(reloadingHash) == shouldShowReloading)
                return;

            animator.SetBool(reloadingHash, shouldShowReloading);
        }

        private float GetEffectiveCombatLayerWeight()
        {
            if (ShouldSuppressCombatLayer())
                return 0f;

            return targetCombatLayerWeight;
        }

        private bool ShouldSuppressCombatLayer()
        {
            return IsRolling() || IsKnockedOrDead();
        }

        private bool CanPlayFireAnimation()
        {
            if (isReloadingAnimation)
                return false;

            return CanPlayWeaponAction();
        }

        private bool CanPlayHitReaction()
        {
            return CanPlayWeaponAction();
        }

        private bool CanPlayWeaponAction()
        {
            if (!IsSupportedWeaponActionType())
                return false;

            return !ShouldSuppressCombatLayer();
        }

        private bool IsSupportedWeaponActionType()
        {
            return currentWeaponType == PlayerWeaponAnimationType.RifleLike ||
                   currentWeaponType == PlayerWeaponAnimationType.Handgun;
        }

        private bool IsRolling()
        {
            if (animator == null)
                return false;

            if (!hasRollingParameter)
                return false;

            return animator.GetBool(rollingHash);
        }

        private bool IsKnockedOrDead()
        {
            if (playerHealthSystem == null)
                return false;

            return playerHealthSystem.IsKnocked || playerHealthSystem.IsDead;
        }

        private void ApplyCombatLayerWeight(float weight)
        {
            currentCombatLayerWeight = Mathf.Clamp01(weight);

            if (!useCombatLayerWeight)
                return;

            if (animator == null || combatLayerIndex < 0)
                return;

            animator.SetLayerWeight(combatLayerIndex, currentCombatLayerWeight);
        }

        private void WarnOnce(ref bool flag, string message)
        {
            if (flag)
                return;

            flag = true;
            Debug.LogWarning(message, this);
        }

        private void LogDebug(string message)
        {
            if (!showDebugLogs)
                return;

            Debug.Log($"[PlayerAnimatorDriver] {message}", this);
        }
    }
}

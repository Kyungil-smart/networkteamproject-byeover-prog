using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

using DeadZone.Core;
using DeadZone.Actors.UI;
using DeadZone.InputActions;

namespace DeadZone.Actors
{
    /// <summary>
    /// DeadZoneInputActions 입력을 현재 플레이어 상태에 맞는 InputContext로 라우팅한다.
    /// 입력 시스템 의존성은 이 클래스에만 집중시켜 이동, 전투, 상호작용 컴포넌트가 Input System을 직접 알지 않게 한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInputController : MonoBehaviour, DeadZoneInputActions.IPlayerActions
    {
        [Header("====입력 처리 기준====")]
        [Tooltip("NetworkObject가 아직 Spawn되지 않은 상태에서도 로컬 입력을 처리할지 여부입니다.")]
        [SerializeField] private bool allowLocalInputWithoutNetworkSpawn = true;

        [Header("====마우스 조준 기준====")]
        [Tooltip("마우스 화면 좌표를 월드 방향으로 변환할 때 사용할 카메라입니다.")]
        [SerializeField] private Camera inputCamera;

        [Tooltip("마우스 조준 Raycast가 감지할 지면 레이어입니다.")]
        [SerializeField] private LayerMask aimMask;

        [Tooltip("마우스 조준 Raycast 최대 거리입니다.")]
        [SerializeField, Min(1f)] private float aimRayDistance = 200f;

        private DeadZoneInputActions inputActions;
        private bool inputEnabled;

        private NetworkObject netObj;
        private PlayerHealthSystem health;
        private ShootingSystem shooting;
        private InteractionSystem interaction;

        private IPlayerInputContext currentContext;
        private IPlayerInputContext aliveCtx;
        private IPlayerInputContext knockedCtx;
        private IPlayerInputContext spectatorCtx;

        private Vector2 moveInput;
        private Vector2 lookScreenPosition;
        private Vector2 lookDirection = Vector2.up;
        private bool fireHeld;
        private bool firePressedThisFrame;
        private float lastInteractInputTime = -999f;
        private const float InteractInputGuardSeconds = 0.15f;

        private void Awake()
        {
            netObj = GetComponent<NetworkObject>();
            health = GetComponent<PlayerHealthSystem>();
            shooting = GetComponent<ShootingSystem>();
            interaction = GetComponentInChildren<InteractionSystem>();

            aliveCtx = new AliveInputContext(
                GetComponent<FPSController>(),
                shooting,
                GetComponent<ADSSystem>(),
                GetComponent<ReloadSystem>(),
                GetComponent<RollSystem>(),
                interaction,
                GetComponent<WeaponSwitching>());

            knockedCtx = new KnockedInputContext(GetComponent<FPSController>());
            spectatorCtx = new SpectatorInputContext(GetComponent<SpectatorController>());

            currentContext = aliveCtx;

            if (inputCamera != null)
            {
                SetInputCamera(inputCamera);
            }
        }

        private void Start()
        {
            if (health == null)
                return;

            health.State.OnValueChanged += OnPlayerStateChanged;
            ApplyContextForState(health.State.Value);
        }

        private void OnEnable()
        {
            inputActions = new DeadZoneInputActions();
            inputActions.Player.SetCallbacks(this);

            RefreshInputEnabledState();
        }

        private void OnDisable()
        {
            if (inputActions != null)
            {
                inputActions.Player.Disable();
                inputActions.Player.SetCallbacks(null);
                inputActions.Dispose();
                inputActions = null;
            }

            inputEnabled = false;
            ResetInputState();
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.State.OnValueChanged -= OnPlayerStateChanged;
            }
        }

        private void Update()
        {
            RefreshInputEnabledState();

            if (!CanProcessInput || !inputEnabled || currentContext == null)
                return;

            ReadContinuousInput();
            UpdateLookDirectionFromMousePosition();

            currentContext.Tick(moveInput, lookDirection, lookScreenPosition);
            currentContext.OnFireInput(firePressedThisFrame, fireHeld, lookScreenPosition);

            firePressedThisFrame = false;
        }

        /// <summary>
        /// Owner Player가 실제로 사용하는 카메라를 입력, 사격, 상호작용 시스템에 전달한다.
        /// </summary>
        public void SetInputCamera(Camera camera)
        {
            inputCamera = camera;

            if (interaction == null)
            {
                interaction = GetComponentInChildren<InteractionSystem>();
            }

            shooting?.SetAimCamera(camera);
            interaction?.SetInteractionCamera(camera);
        }

        private bool CanProcessInput
        {
            get
            {
                if (netObj == null)
                {
                    return allowLocalInputWithoutNetworkSpawn;
                }

                if (!netObj.IsSpawned)
                {
                    return allowLocalInputWithoutNetworkSpawn;
                }

                return netObj.IsOwner;
            }
        }

        private void RefreshInputEnabledState()
        {
            if (inputActions == null)
                return;

            bool shouldEnable = CanProcessInput;

            if (shouldEnable && !inputEnabled)
            {
                inputActions.Player.Enable();
                inputEnabled = true;
            }
            else if (!shouldEnable && inputEnabled)
            {
                inputActions.Player.Disable();
                inputEnabled = false;
                ResetInputState();
            }
        }

        private void ResetInputState()
        {
            moveInput = Vector2.zero;
            lookScreenPosition = Vector2.zero;
            lookDirection = Vector2.up;
            fireHeld = false;
            firePressedThisFrame = false;
        }

        private void ReadContinuousInput()
        {
            moveInput = inputActions.Player.Move.ReadValue<Vector2>();
            lookScreenPosition = inputActions.Player.Look.ReadValue<Vector2>();
        }

        private void OnPlayerStateChanged(PlayerState oldState, PlayerState newState)
        {
            ApplyContextForState(newState);
        }

        private void ApplyContextForState(PlayerState state)
        {
            currentContext = state switch
            {
                PlayerState.Alive => aliveCtx,
                PlayerState.Knocked => knockedCtx,
                PlayerState.Dead => spectatorCtx,
                _ => aliveCtx,
            };
        }

        private void UpdateLookDirectionFromMousePosition()
        {
            if (inputCamera == null)
                return;

            Ray ray = inputCamera.ScreenPointToRay(lookScreenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, aimRayDistance, aimMask, QueryTriggerInteraction.Ignore))
                return;

            Vector3 direction = hit.point - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
                return;

            direction.Normalize();
            lookDirection = new Vector2(direction.x, direction.z);
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            // Move 입력은 Update에서 매 프레임 ReadValue로 처리한다.
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            // Look 입력은 Update에서 매 프레임 Mouse Position을 읽어 처리한다.
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            if (!CanProcessInput)
                return;

            if (context.performed)
            {
                fireHeld = true;
                firePressedThisFrame = true;
            }
            else if (context.canceled)
            {
                fireHeld = false;
            }
        }

        public void OnAim(InputAction.CallbackContext context)
        {
            if (!CanProcessInput)
                return;

            if (context.performed)
            {
                currentContext?.OnAim(true);
            }
            else if (context.canceled)
            {
                currentContext?.OnAim(false);
            }
        }

        public void OnReload(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnReload();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || (!context.started && !context.performed))
                return;

            if (Time.unscaledTime - lastInteractInputTime < InteractInputGuardSeconds)
                return;

            lastInteractInputTime = Time.unscaledTime;
            currentContext?.OnInteract();
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnRoll();
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            if (!CanProcessInput)
                return;

            if (context.performed)
            {
                currentContext?.OnSprint(true);
            }
            else if (context.canceled)
            {
                currentContext?.OnSprint(false);
            }
        }

        public void OnWeapon_Secondary(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Secondary);
        }

        public void OnWeapon_Primary1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Primary1);
        }

        public void OnWeapon_Primary2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Primary2);
        }

        public void OnWeapon_Melee(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Melee);
        }

        public void OnQuickslot_1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(0);
        }

        public void OnQuickslot_2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(1);
        }

        public void OnQuickslot_3(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(2);
        }

        public void OnQuickslot_4(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(3);
        }

        public void OnQuickslot_5(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(4);
        }

        public void OnQuickslot_6(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;

            TryUseQuickslot(5);
        }

        private static void TryUseQuickslot(int slotIndex)
        {
            InventorySlotUI slot = ResolveQuickSlot(slotIndex);
            if (slot == null || !slot.HasItem)
                return;

            slot.TryUseCurrentItem();
        }

        private static InventorySlotUI ResolveQuickSlot(int slotIndex)
        {
            InventorySlotUI fallback = null;
            InventorySlotUI[] slots = FindObjectsByType<InventorySlotUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null ||
                    slot.SlotKind != InventorySlotKind.QuickSlot ||
                    slot.SlotIndex != slotIndex)
                {
                    continue;
                }

                if (slot.gameObject.activeInHierarchy)
                    return slot;

                fallback ??= slot;
            }

            return fallback;
        }

        public void OnInventory(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;
        }

        public void OnQuest(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;
        }

        public void OnMap(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;
        }

        public void OnHelp(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;
        }

        public void OnPause(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed)
                return;
        }
    }
}

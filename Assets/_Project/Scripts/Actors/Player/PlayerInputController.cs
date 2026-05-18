using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
        private ReviveSystem revive;

        private IPlayerInputContext currentContext;
        private IPlayerInputContext aliveCtx;
        private IPlayerInputContext knockedCtx;
        private IPlayerInputContext spectatorCtx;

        private Vector2 moveInput;
        private Vector2 lookScreenPosition;
        private Vector2 lookDirection = Vector2.up;
        private bool fireHeld;
        private bool firePressedThisFrame;
        private bool reviveHoldStarted;
        private float lastInteractInputTime = -999f;
        private const float InteractInputGuardSeconds = 0.15f;
        private readonly int[] quickslotUseFrames = new int[GridInventory.QUICK_SLOT_COUNT];

        private void Awake()
        {
            netObj = GetComponent<NetworkObject>();
            health = GetComponent<PlayerHealthSystem>();
            shooting = GetComponent<ShootingSystem>();
            interaction = GetComponentInChildren<InteractionSystem>();
            revive = GetComponent<ReviveSystem>();

            for (int i = 0; i < quickslotUseFrames.Length; i++)
                quickslotUseFrames[i] = -1;

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
            StopReviveHoldIfNeeded();

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

            if (GameplayInputBlocker.IsBlocked)
            {
                ClearGameplayInputState();
                return;
            }

            ReadContinuousInput();
            UpdateLookDirectionFromMousePosition();

            currentContext.Tick(moveInput, lookDirection, lookScreenPosition);
            currentContext.OnFireInput(firePressedThisFrame, fireHeld, lookScreenPosition);
            ProcessSpectatorKeyboardInput();
            ProcessQuickslotKeyboardFallback();

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

        private void ClearGameplayInputState()
        {
            StopReviveHoldIfNeeded();
            ResetInputState();
            currentContext?.OnAim(false);
            currentContext?.OnSprint(false);
        }

        private void ReadContinuousInput()
        {
            moveInput = inputActions.Player.Move.ReadValue<Vector2>();
            lookScreenPosition = inputActions.Player.Look.ReadValue<Vector2>();
        }

        private void ProcessSpectatorKeyboardInput()
        {
            if (health == null || !health.IsDead)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.qKey.wasPressedThisFrame)
                currentContext?.OnSpectatorPrev();

            if (keyboard.eKey.wasPressedThisFrame)
                currentContext?.OnSpectatorNext();
        }

        private void ProcessQuickslotKeyboardFallback()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (WasPressedThisFrame(keyboard.digit4Key) || WasPressedThisFrame(keyboard.numpad4Key))
                TryUseQuickslotFromInput(0);

            if (WasPressedThisFrame(keyboard.digit5Key) || WasPressedThisFrame(keyboard.numpad5Key))
                TryUseQuickslotFromInput(1);

            if (WasPressedThisFrame(keyboard.digit6Key) || WasPressedThisFrame(keyboard.numpad6Key))
                TryUseQuickslotFromInput(2);

            if (WasPressedThisFrame(keyboard.digit7Key) || WasPressedThisFrame(keyboard.numpad7Key))
                TryUseQuickslotFromInput(3);

            if (WasPressedThisFrame(keyboard.digit8Key) || WasPressedThisFrame(keyboard.numpad8Key))
                TryUseQuickslotFromInput(4);

            if (WasPressedThisFrame(keyboard.digit9Key) || WasPressedThisFrame(keyboard.numpad9Key))
                TryUseQuickslotFromInput(5);
        }

        private static bool WasPressedThisFrame(KeyControl key)
        {
            return key != null && key.wasPressedThisFrame;
        }

        private void OnPlayerStateChanged(PlayerState oldState, PlayerState newState)
        {
            if (newState != PlayerState.Alive)
                StopReviveHoldIfNeeded();

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
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked)
            {
                fireHeld = false;
                firePressedThisFrame = false;
                return;
            }

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

            if (GameplayInputBlocker.IsBlocked)
            {
                currentContext?.OnAim(false);
                return;
            }

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
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnReload();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked)
            {
                if (context.canceled)
                    StopReviveHoldIfNeeded();

                return;
            }

            if (context.canceled)
            {
                StopReviveHoldIfNeeded();
                return;
            }

            if (!context.started && !context.performed)
                return;

            if (context.started && TryStartReviveHold())
                return;

            if (reviveHoldStarted)
                return;

            if (Time.unscaledTime - lastInteractInputTime < InteractInputGuardSeconds)
                return;

            lastInteractInputTime = Time.unscaledTime;
            currentContext?.OnInteract();
        }

        private bool TryStartReviveHold()
        {
            if (health == null || !health.IsAlive)
                return false;

            if (revive == null || !revive.HasReviveCandidate())
                return false;

            reviveHoldStarted = true;
            revive.StartHold();
            return true;
        }

        private void StopReviveHoldIfNeeded()
        {
            if (!reviveHoldStarted)
                return;

            reviveHoldStarted = false;
            revive?.StopHold();
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnRoll();
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            if (!CanProcessInput)
                return;

            if (GameplayInputBlocker.IsBlocked)
            {
                currentContext?.OnSprint(false);
                return;
            }

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
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Secondary);
        }

        public void OnWeapon_Primary1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Primary1);
        }

        public void OnWeapon_Primary2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Primary2);
        }

        public void OnWeapon_Melee(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            currentContext?.OnEquipSlot(WeaponSlot.Melee);
        }

        public void OnQuickslot_1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(0);
        }

        public void OnQuickslot_2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(1);
        }

        public void OnQuickslot_3(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(2);
        }

        public void OnQuickslot_4(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(3);
        }

        public void OnQuickslot_5(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(4);
        }

        public void OnQuickslot_6(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || GameplayInputBlocker.IsBlocked || !context.performed)
                return;

            TryUseQuickslotFromInput(5);
        }

        private void TryUseQuickslotFromInput(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= quickslotUseFrames.Length)
                return;

            if (quickslotUseFrames[slotIndex] == Time.frameCount)
                return;

            quickslotUseFrames[slotIndex] = Time.frameCount;
            TryUseQuickslot(slotIndex);
        }

        private static void TryUseQuickslot(int slotIndex)
        {
            GridInventory inventory = ResolveOwnerGridInventory();
            if (inventory != null)
            {
                byte quickSlotIndex = (byte)Mathf.Clamp(slotIndex, 0, GridInventory.QUICK_SLOT_COUNT - 1);
                inventory.RequestUseQuickSlot(quickSlotIndex);
                return;
            }

            InventorySlotUI slot = ResolveQuickSlot(slotIndex);
            if (slot == null || !slot.HasItem)
                return;

            slot.TryUseCurrentItem();
        }

        private static GridInventory ResolveOwnerGridInventory()
        {
            GridInventory[] inventories = FindObjectsByType<GridInventory>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                GridInventory inventory = inventories[i];
                if (inventory != null && inventory.IsOwner)
                    return inventory;
            }

            return null;
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

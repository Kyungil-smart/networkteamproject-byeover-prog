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
    /// 입력 시스템 의존성은 이 클래스에만 집중시켜 이동/전투/상호작용 컴포넌트가 Input System을 직접 알지 않게 한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInputController : MonoBehaviour, DeadZoneInputActions.IPlayerActions
    {
        [Header("====입력 처리 기준====")]
        [Tooltip("네트워크 스폰 전 로컬 테스트 씬에서 입력을 허용할지 여부" +
                 "\nNGO 멀티 테스트 에서는 false로")]
        [SerializeField] private bool allowLocalInputWithoutNetworkSpawn = true;
        
        [Header("====마우스 조준 기준====")]
        [Tooltip("마우스 화면 좌표를 월드 좌표로 변환할 때 사용할 카메라" +
                 "\n비어 있으면 MainCamera를 자동으로 사용")]
        [SerializeField] private Camera inputCamera;
        
        [Tooltip("마우스 Raycast가 맞출 지면 레이어")]
        [SerializeField] private LayerMask aimMask;
        
        [Tooltip("마우스 Raycast 최대 거리")]
        [SerializeField, Min(1f)] private float aimRayDistance = 200f;

        private DeadZoneInputActions inputActions;
        private bool inputEnabled;
        
        private NetworkObject netObj;
        private PlayerHealthSystem health;
        private ShootingSystem shooting;

        private IPlayerInputContext currentContext;
        private IPlayerInputContext aliveCtx;
        private IPlayerInputContext knockedCtx;
        private IPlayerInputContext spectatorCtx;

        private Vector2 moveInput;
        private Vector2 lookScreenPosition;
        private Vector2 lookDirection = Vector2.up;
        private bool fireHeld;
        private bool firePressedThisFrame;
        
        private void Awake()
        {
            netObj = GetComponent<NetworkObject>();
            health = GetComponent<PlayerHealthSystem>();
            shooting = GetComponent<ShootingSystem>();

            aliveCtx = new AliveInputContext(
                GetComponent<FPSController>(),
                shooting,
                GetComponent<ADSSystem>(),
                GetComponent<ReloadSystem>(),
                GetComponent<RollSystem>(),
                GetComponentInChildren<InteractionSystem>(),
                GetComponent<WeaponSwitching>());

            knockedCtx = new KnockedInputContext(GetComponent<FPSController>());
            spectatorCtx = new SpectatorInputContext(GetComponent<SpectatorController>());

            currentContext = aliveCtx;
            
            if (inputCamera == null) inputCamera = Camera.main;
            if (inputCamera != null) shooting?.SetAimCamera(inputCamera);
        }

        private void Start()
        {
            if (health == null) return;
            
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
            if (health != null) health.State.OnValueChanged -= OnPlayerStateChanged;
        }
        
        private void Update()
        {
            RefreshInputEnabledState();
            
            if (!CanProcessInput || !inputEnabled || currentContext == null) return;
            
            ReadContinuousInput();
            UpdateLookDirectionFromMousePosition();

            currentContext.Tick(moveInput, lookDirection, lookScreenPosition);
            currentContext.OnFireInput(firePressedThisFrame, fireHeld, lookScreenPosition);

            // 클릭 시작 입력은 한 프레임짜리 신호이므로 같은 프레임의 사격 처리 후 바로 해제한다.
            firePressedThisFrame = false;
        }

        public void SetInputCamera(Camera cam)
        {
            inputCamera = cam;
            shooting?.SetAimCamera(cam);
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
            if (inputActions == null) return;
            
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
        
        private void ApplyContextForState(PlayerState s)
        {
            currentContext = s switch
            {
                PlayerState.Alive => aliveCtx,
                PlayerState.Knocked => knockedCtx,
                PlayerState.Dead => spectatorCtx,
                _ => aliveCtx,
            };
        }
        
        private void UpdateLookDirectionFromMousePosition()
        {
            if (!TryResolveInputCamera()) return;
            
            Ray ray = inputCamera.ScreenPointToRay(lookScreenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, aimRayDistance, aimMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Vector3 direction = hit.point - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f) return;

            direction.Normalize();
            lookDirection = new Vector2(direction.x, direction.z);
        }
        
        private bool TryResolveInputCamera()
        {
            if (inputCamera != null) return true;

            inputCamera = Camera.main;
            if (inputCamera != null) shooting?.SetAimCamera(inputCamera);

            return inputCamera != null;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            // Move는 Update에서 매 프레임 ReadValue로 처리한다.
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            // Look은 Update에서 매 프레임 Mouse Position을 읽어 처리한다.
        }
        
        public void OnFire(InputAction.CallbackContext context)
        {
            if (!CanProcessInput) return;

            if (context.performed)
            {
                // 클릭이 시작된 프레임과 버튼 유지 상태를 분리해 단발/연사를 같은 경로에서 처리한다.
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
            if (!CanProcessInput) return;

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
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnReload();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnInteract();
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnRoll();
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            if (!CanProcessInput) return;

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
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnEquipSlot(WeaponSlot.Secondary);
        }

        public void OnWeapon_Primary1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnEquipSlot(WeaponSlot.Primary1);
        }

        public void OnWeapon_Primary2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnEquipSlot(WeaponSlot.Primary2);
        }

        public void OnWeapon_Melee(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
            currentContext?.OnEquipSlot(WeaponSlot.Melee);
        }

        public void OnQuickslot_1(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuickslot_2(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuickslot_3(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuickslot_4(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuickslot_5(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuickslot_6(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnInventory(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnQuest(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnMap(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnHelp(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }

        public void OnPause(InputAction.CallbackContext context)
        {
            if (!CanProcessInput || !context.performed) return;
        }
    }
}
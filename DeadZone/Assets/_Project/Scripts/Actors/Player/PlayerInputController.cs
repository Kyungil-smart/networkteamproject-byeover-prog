using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// Unity Input System 액션을 활성 IPlayerInputContext로 라우팅한다.
    /// 플레이어 상태(Alive/Knocked/Dead)가 바뀌면 컨텍스트가 자동으로 교체된다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInputController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        private InputActionMap playerMap;
        private NetworkObject netObj;
        private PlayerHealthSystem health;
        private IPlayerInputContext currentContext;

        private IPlayerInputContext aliveCtx;
        private IPlayerInputContext knockedCtx;
        private IPlayerInputContext spectatorCtx;

        private InputAction moveA, lookA, fireA, aimA, reloadA, interactA, rollA, sprintA;
        private InputAction wSecondary, wPrimary1, wPrimary2, wMelee;
        private InputAction[] quickslots;
        private InputAction inventoryA, questA, mapA, helpA, pauseA;
        private InputAction spectatorNextA, spectatorPrevA, spectatorModeA;

        private void Awake()
        {
            netObj = GetComponent<NetworkObject>();
            health = GetComponent<PlayerHealthSystem>();

            aliveCtx = new AliveInputContext(
                GetComponent<FPSController>(),
                GetComponent<ShootingSystem>(),
                GetComponent<ADSSystem>(),
                GetComponent<ReloadSystem>(),
                GetComponent<RollSystem>(),
                GetComponentInChildren<InteractionSystem>(),
                GetComponent<WeaponSwitching>());

            knockedCtx = new KnockedInputContext(GetComponent<FPSController>());

            spectatorCtx = new SpectatorInputContext(GetComponent<SpectatorController>());

            currentContext = aliveCtx;
        }

        private void Start()
        {
            if (health != null)
            {
                health.State.OnValueChanged += OnPlayerStateChanged;
                ApplyContextForState(health.State.Value);
            }
        }

        private void OnDestroy()
        {
            if (health != null) health.State.OnValueChanged -= OnPlayerStateChanged;
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

        private void OnEnable()
        {
            if (inputActions == null) return;
            playerMap = inputActions.FindActionMap("Player", true);
            playerMap.Enable();

            moveA      = playerMap.FindAction("Move");
            lookA      = playerMap.FindAction("Look");
            fireA      = playerMap.FindAction("Fire");
            aimA       = playerMap.FindAction("Aim");
            reloadA    = playerMap.FindAction("Reload");
            interactA  = playerMap.FindAction("Interact");
            rollA      = playerMap.FindAction("Roll");
            sprintA    = playerMap.FindAction("Sprint");
            wSecondary = playerMap.FindAction("Weapon_Secondary");
            wPrimary1  = playerMap.FindAction("Weapon_Primary1");
            wPrimary2  = playerMap.FindAction("Weapon_Primary2");
            wMelee     = playerMap.FindAction("Weapon_Melee");
            quickslots = new InputAction[6];
            for (int i = 0; i < 6; i++)
                quickslots[i] = playerMap.FindAction($"Quickslot_{i + 1}");
            inventoryA = playerMap.FindAction("Inventory");
            questA     = playerMap.FindAction("Quest");
            mapA       = playerMap.FindAction("Map");
            helpA      = playerMap.FindAction("Help");
            pauseA     = playerMap.FindAction("Pause");
            spectatorNextA = playerMap.FindAction("SpectatorNext", false);
            spectatorPrevA = playerMap.FindAction("SpectatorPrev", false);
            spectatorModeA = playerMap.FindAction("SpectatorMode", false);

            if (fireA != null)     fireA.performed     += OnFire;
            if (aimA != null)    { aimA.performed      += OnAimDown;
                                   aimA.canceled       += OnAimUp; }
            if (reloadA != null)   reloadA.performed   += OnReload;
            if (interactA != null) interactA.performed += OnInteract;
            if (rollA != null)     rollA.performed     += OnRoll;
            if (sprintA != null) { sprintA.performed   += OnSprintDown;
                                   sprintA.canceled    += OnSprintUp; }
            if (wSecondary != null) wSecondary.performed += _ => Equip(WeaponSlot.Secondary);
            if (wPrimary1 != null)  wPrimary1.performed  += _ => Equip(WeaponSlot.Primary1);
            if (wPrimary2 != null)  wPrimary2.performed  += _ => Equip(WeaponSlot.Primary2);
            if (wMelee != null)     wMelee.performed     += _ => Equip(WeaponSlot.Melee);
            if (spectatorNextA != null) spectatorNextA.performed += OnSpectatorNext;
            if (spectatorPrevA != null) spectatorPrevA.performed += OnSpectatorPrev;
            if (spectatorModeA != null) spectatorModeA.performed += OnSpectatorMode;
        }

        private void OnDisable()
        {
            if (playerMap != null) playerMap.Disable();
        }

        private void Update()
        {
            if (netObj == null || !netObj.IsOwner) return;
            if (currentContext == null) return;

            Vector2 move = moveA != null ? moveA.ReadValue<Vector2>() : Vector2.zero;
            Vector2 look = lookA != null ? lookA.ReadValue<Vector2>() : Vector2.zero;
            currentContext.Tick(move, look);
        }

        private bool IsOwner => netObj != null && netObj.IsOwner;

        private void OnFire(InputAction.CallbackContext _)         { if (IsOwner) currentContext?.OnFire(); }
        private void OnAimDown(InputAction.CallbackContext _)      { if (IsOwner) currentContext?.OnAim(true); }
        private void OnAimUp(InputAction.CallbackContext _)        { if (IsOwner) currentContext?.OnAim(false); }
        private void OnReload(InputAction.CallbackContext _)       { if (IsOwner) currentContext?.OnReload(); }
        private void OnInteract(InputAction.CallbackContext _)     { if (IsOwner) currentContext?.OnInteract(); }
        private void OnRoll(InputAction.CallbackContext _)         { if (IsOwner) currentContext?.OnRoll(); }
        private void OnSprintDown(InputAction.CallbackContext _)   { if (IsOwner) currentContext?.OnSprint(true); }
        private void OnSprintUp(InputAction.CallbackContext _)     { if (IsOwner) currentContext?.OnSprint(false); }
        private void Equip(WeaponSlot s)                           { if (IsOwner) currentContext?.OnEquipSlot(s); }
        private void OnSpectatorNext(InputAction.CallbackContext _) { if (IsOwner) currentContext?.OnSpectatorNext(); }
        private void OnSpectatorPrev(InputAction.CallbackContext _) { if (IsOwner) currentContext?.OnSpectatorPrev(); }
        private void OnSpectatorMode(InputAction.CallbackContext _) { if (IsOwner) currentContext?.OnSpectatorToggleMode(); }
    }
}

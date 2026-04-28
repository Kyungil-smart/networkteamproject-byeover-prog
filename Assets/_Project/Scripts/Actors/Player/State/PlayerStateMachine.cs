using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// PlayerHealthSystem.State NetworkVariable을 구독하여 대응하는 PlayerStateBase를 실행한다.
    /// PlayerPrefab Root에 부착된다.
    /// </summary>
    /// <remarks>
    /// 상태 클래스는 POCO이지 MonoBehaviour가 아니므로 자체 Update를 가지지 않는다 —
    /// 본 클래스가 Owner 측에서 현재 상태의 OnUpdate를 프록시로 호출한다.
    /// </remarks>
    public class PlayerStateMachine : NetworkBehaviour
    {
        private readonly Dictionary<PlayerState, PlayerStateBase> states = new();
        private PlayerStateBase current;
        private PlayerStateContext context;
        private PlayerHealthSystem health;

        private void Awake()
        {
            states[PlayerState.Alive]   = new AlivePlayerState();
            states[PlayerState.Knocked] = new KnockedPlayerState();
            states[PlayerState.Dead]    = new DeadPlayerState();

            health = GetComponent<PlayerHealthSystem>();
        }

        public override void OnNetworkSpawn()
        {
            context = new PlayerStateContext
            {
                PlayerRoot = gameObject,
                Health = health,
                FPS = GetComponent<FPSController>(),
                Shooting = GetComponent<ShootingSystem>(),
                Reload = GetComponent<ReloadSystem>(),
                ADS = GetComponent<ADSSystem>(),
                Roll = GetComponent<RollSystem>(),
                WeaponSwitching = GetComponent<WeaponSwitching>(),
                Interaction = GetComponentInChildren<InteractionSystem>(),
                CharacterController = GetComponent<CharacterController>(),
                Animator = GetComponent<Animator>(),
                OwnerClientId = OwnerClientId,
                IsOwner = IsOwner,
                IsServer = IsServer,
            };

            if (health != null)
            {
                health.State.OnValueChanged += OnHealthStateChanged;
                EnterState(health.State.Value, health.State.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.State.OnValueChanged -= OnHealthStateChanged;
            current?.OnExit(context);
        }

        public PlayerStateBase Current => current;

        private void OnHealthStateChanged(PlayerState oldState, PlayerState newState)
        {
            EnterState(newState, oldState);
        }

        private void EnterState(PlayerState target, PlayerState previous)
        {
            if (context != null)
            {
                context.FromState = previous;
                context.ToState = target;
            }

            current?.OnExit(context);

            if (states.TryGetValue(target, out PlayerStateBase next))
            {
                current = next;
                current.OnEnter(context);
            }
            else
            {
                Debug.LogError($"[PlayerStateMachine] Missing state for {target}", this);
                current = null;
            }
        }

        private void Update()
        {
            current?.OnUpdate(context);
        }
    }
}

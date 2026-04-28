using UnityEngine;
using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 상태 클래스가 필요로 하는 참조 묶음. 시작 시 1회 주입된다.
    /// 컴포넌트 조회에 대해 상태를 무상태(stateless)로 유지한다.
    /// </summary>
    public class PlayerStateContext
    {
        public GameObject PlayerRoot;
        public PlayerHealthSystem Health;
        public FPSController FPS;
        public ShootingSystem Shooting;
        public ReloadSystem Reload;
        public ADSSystem ADS;
        public RollSystem Roll;
        public WeaponSwitching WeaponSwitching;
        public InteractionSystem Interaction;
        public CharacterController CharacterController;
        public Animator Animator;
        public PlayerState FromState;
        public PlayerState ToState;
        

        public ulong OwnerClientId;
        public bool IsOwner;
        public bool IsServer;
    }
}

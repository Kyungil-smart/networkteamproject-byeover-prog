using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// Hideout 씬의 루트 매니저. 시설 + 스폰 포인트 참조를 보유한다.
    /// ServiceLocator를 통한 싱글톤.
    /// </summary>
    public class HideoutManager : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform playerSpawn;

        public Transform PlayerSpawn => playerSpawn;

        public override void OnNetworkSpawn()
        {
            ServiceLocator.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            ServiceLocator.Unregister<HideoutManager>();
        }
    }
}

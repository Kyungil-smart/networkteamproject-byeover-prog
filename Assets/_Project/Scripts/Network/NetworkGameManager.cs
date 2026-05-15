using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Systems.Raid;

namespace DeadZone.Network
{
    /// <summary>
    /// 레이드 진행 중 남은 시간과 씬 전환 완료 이벤트 발행을 담당한다.
    /// 레이드 시작 요청은 로비의 LobbyRaidStartController에서 처리한다.
    /// </summary>
    public class NetworkGameManager : NetworkBehaviour
    {
        public NetworkVariable<float> RaidTimeRemaining = new(0f);

        public override void OnNetworkSpawn()
        {
            ServiceLocator.Register(this);

            if (IsServer)
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
            }

            ServiceLocator.Unregister<NetworkGameManager>();
        }

        private void Update()
        {
            if (!IsServer) return;

            if (RaidTimeRemaining.Value > 0f)
            {
                RaidTimeRemaining.Value = Mathf.Max(0f, RaidTimeRemaining.Value - Time.deltaTime);

                if (RaidTimeRemaining.Value <= 0f)
                {
                    OnRaidTimeExpired();
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToHideoutServerRpc()
        {
            RaidLoadoutTransferService.SaveCurrentRaidLoadoutsForConnectedClients();
            RaidTimeRemaining.Value = 0f;
            LoadingScreenService.ShowForNetworkLoadOrFallback("Hideout");
            NetworkManager.Singleton.SceneManager.LoadScene("Hideout", LoadSceneMode.Single);
        }

        private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            EventBus.Publish(new SceneChangedEvent { sceneName = sceneName });
        }

        private void OnRaidTimeExpired()
        {
            Debug.Log("[NetworkGameManager] Raid time expired -> return to hideout");
            ReturnToHideoutServerRpc();
        }
    }
}

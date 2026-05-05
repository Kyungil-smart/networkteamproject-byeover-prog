using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// 서버 권위 씬 전환 + 레이드 타이머.
    /// NetworkBootstrap (DontDestroyOnLoad)에 부착된다.
    /// </summary>
    public class NetworkGameManager : NetworkBehaviour
    {
        [Header("Raid")]
        [SerializeField] private float raidTimeLimit = 2100f;

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
            if (RaidTimeRemaining.Value > 0)
            {
                RaidTimeRemaining.Value = Mathf.Max(0f, RaidTimeRemaining.Value - Time.deltaTime);
                if (RaidTimeRemaining.Value <= 0)
                {
                    OnRaidTimeExpired();
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void StartRaidServerRpc(string sceneName)
        {
            StartRaidOnServer(sceneName);
        }

        public void StartRaidOnServer(string sceneName)
        {
            if (!IsServer) return;
            if (string.IsNullOrEmpty(sceneName)) return;

            RaidTimeRemaining.Value = raidTimeLimit;
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReturnToHideoutServerRpc()
        {
            RaidTimeRemaining.Value = 0;
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

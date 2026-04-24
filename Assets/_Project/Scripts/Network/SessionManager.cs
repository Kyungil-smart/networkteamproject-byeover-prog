using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비 + 매칭메이킹. NetworkBootstrap에 부착.
    /// NetworkManager의 Host/Client/Server 시작 호출을 래핑한다.
    ///
    /// v1.3 (Part VII Addendum): Relay 통합. RelayManager와 협업하여
    /// UnityTransport를 Relay 모드로 설정한 후 Host/Client를 시작한다.
    /// </summary>
    /// <remarks>
    /// 책임 분리:
    ///  - RelayManager: Relay Allocation 생성/조회만 담당
    ///  - SessionManager: Transport 설정 + NetworkManager.StartHost/Client 호출
    /// </remarks>
    public class SessionManager : MonoBehaviour
    {
        private void Awake()
        {
            ServiceLocator.Register(this);
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<SessionManager>();
        }

        // =================================================================
        // LOCAL / DEV - 에디터 로컬 테스트나 Dedicated 이관 시 사용
        // =================================================================

        public bool StartHost()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[SessionManager] NetworkManager.Singleton이 null이다");
                return false;
            }
            return NetworkManager.Singleton.StartHost();
        }

        public bool StartClient()
        {
            if (NetworkManager.Singleton == null) return false;
            return NetworkManager.Singleton.StartClient();
        }

        public bool StartServer()
        {
            if (NetworkManager.Singleton == null) return false;
            return NetworkManager.Singleton.StartServer();
        }

        public void Disconnect()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.Shutdown();
        }

        // =================================================================
        // RELAY - itch.io 배포용. NAT 터널링으로 포트포워딩 불필요
        // =================================================================

        /// <summary>
        /// Relay로 호스트 시작. 반환된 JoinCode를 UI에 표시하고 친구에게 공유한다.
        /// 실패 시 빈 문자열 반환.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync(int maxPlayers = 4)
        {
            var relay = ServiceLocator.Get<RelayManager>();
            if (relay == null)
            {
                Debug.LogError("[SessionManager] RelayManager가 등록되어 있지 않다");
                return string.Empty;
            }

            Allocation allocation = await relay.CreateAllocationAsync(maxPlayers);
            if (allocation == null) return string.Empty;

            string joinCode = await relay.GetJoinCodeAsync(allocation);
            if (string.IsNullOrEmpty(joinCode)) return string.Empty;

            if (!ConfigureTransportAsHost(allocation))
            {
                return string.Empty;
            }

            bool ok = NetworkManager.Singleton.StartHost();
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartHost 실패");
                return string.Empty;
            }

            EventBus.Publish(new RelayAllocationCreatedEvent
            {
                joinCode = joinCode,
                maxConnections = maxPlayers,
            });

            return joinCode;
        }

        /// <summary>
        /// 친구가 공유한 JoinCode로 Relay 접속. 실패 시 false.
        /// </summary>
        public async Task<bool> StartClientWithJoinCodeAsync(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode)) return false;

            var relay = ServiceLocator.Get<RelayManager>();
            if (relay == null)
            {
                Debug.LogError("[SessionManager] RelayManager가 등록되어 있지 않다");
                return false;
            }

            JoinAllocation joinAlloc = await relay.JoinAllocationAsync(joinCode);
            if (joinAlloc == null) return false;

            if (!ConfigureTransportAsClient(joinAlloc))
            {
                return false;
            }

            bool ok = NetworkManager.Singleton.StartClient();
            if (!ok)
            {
                Debug.LogError("[SessionManager] Relay 설정 후 StartClient 실패");
                return false;
            }

            EventBus.Publish(new RelayJoinedEvent { joinCode = joinCode });
            return true;
        }

        // =================================================================
        // Transport 설정 헬퍼 - UTP에 Relay 서버 정보 주입
        // =================================================================

        private bool ConfigureTransportAsHost(Allocation a)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[SessionManager] NetworkManager에 UnityTransport가 없다");
                return false;
            }

            transport.SetRelayServerData(new Unity.Networking.Transport.Relay.RelayServerData(a, "dtls"));
            return true;
        }

        private bool ConfigureTransportAsClient(JoinAllocation a)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[SessionManager] NetworkManager에 UnityTransport가 없다");
                return false;
            }

            transport.SetRelayServerData(new Unity.Networking.Transport.Relay.RelayServerData(a, "dtls"));
            return true;
        }
    }
}

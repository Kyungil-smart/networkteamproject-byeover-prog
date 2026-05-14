using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// Unity Relay Allocation 관리자.
    /// NetworkBootstrap GameObject에 부착된다.
    ///
    /// 책임:
    ///  - Unity Services 초기화 + Anonymous Sign-in (Firebase Auth와는 별개)
    ///  - 호스트: CreateAllocation → JoinCode 발급
    ///  - 클라이언트: JoinCode로 접속 → JoinAllocation 반환
    ///
    /// 주의: Anonymous Sign-in은 Relay 서비스 호출을 위한 Unity 내부 인증이다.
    /// 유저에게 보이는 계정 시스템은 Firebase Auth가 담당한다. 두 시스템은 독립이다.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        [Header("Relay 설정")]
        [SerializeField] private string relayRegion = null;  // null이면 가장 가까운 리전 자동 선택

        private bool isUnityServicesReady;

        public bool IsReady => isUnityServicesReady;

        private async void Awake()
        {
            ServiceLocator.Register(this);
            await InitializeUnityServicesAsync();
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
        }

        /// <summary>
        /// Unity Services 초기화 + Anonymous 로그인 (Relay 호출의 전제 조건).
        /// 한 번만 실행되면 된다. 실패해도 예외는 던지지 않고 로그만 남긴다.
        /// </summary>
        private async Task InitializeUnityServicesAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Initialized)
                {
                    isUnityServicesReady = true;
                    return;
                }

                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                isUnityServicesReady = true;
                Debug.Log($"[RelayManager] Unity Services 준비 완료. PlayerId={AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] Unity Services 초기화 실패: {e}");
                isUnityServicesReady = false;
            }
        }

        /// <summary>
        /// 호스트가 호출한다. Relay Allocation을 생성한다.
        /// </summary>
        /// <param name="maxPlayers">최대 플레이어 수 (호스트 포함). Relay 내부에서는 maxPlayers-1을 사용.</param>
        public async Task<Allocation> CreateAllocationAsync(int maxPlayers)
        {
            if (!isUnityServicesReady)
            {
                Debug.LogError("[RelayManager] Unity Services가 준비되지 않았다");
                return null;
            }

            try
            {
                // NGO 관점에서 maxPlayers=4면 Relay는 "나 외 3명"을 수용한다.
                // Relay API의 maxConnections는 호스트를 포함하지 않은 수.
                int maxConnections = Mathf.Max(1, maxPlayers - 1);
                return await RelayService.Instance.CreateAllocationAsync(maxConnections, relayRegion);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] CreateAllocation 실패: {e}");
                return null;
            }
        }

        /// <summary>
        /// 호스트가 호출한다. Allocation을 JoinCode로 변환한다 (친구에게 공유할 6자리 문자열).
        /// </summary>
        public async Task<string> GetJoinCodeAsync(Allocation allocation)
        {
            if (allocation == null) return string.Empty;

            try
            {
                return await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] GetJoinCode 실패: {e}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 클라이언트가 호출한다. JoinCode로 Allocation을 찾아 접속 정보를 가져온다.
        /// </summary>
        public async Task<JoinAllocation> JoinAllocationAsync(string joinCode)
        {
            if (!isUnityServicesReady)
            {
                Debug.LogError("[RelayManager] Unity Services가 준비되지 않았다");
                return null;
            }
            if (string.IsNullOrEmpty(joinCode)) return null;

            try
            {
                return await RelayService.Instance.JoinAllocationAsync(joinCode);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] JoinAllocation 실패 (코드 '{joinCode}'): {e}");
                return null;
            }
        }
    }
}

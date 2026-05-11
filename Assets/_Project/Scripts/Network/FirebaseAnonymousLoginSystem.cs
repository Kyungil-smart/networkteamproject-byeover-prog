using System.Threading.Tasks;

using Firebase;
using Firebase.Auth;
using UnityEngine;

namespace DeadZone.Network
{
    // Firebase 익명 로그인을 담당
    // Cloud Save 저장 전에 CurrentUser가 없으면 익명 로그인으로 인증 상태를 만듭니다.
    [DisallowMultipleComponent]
    public sealed class FirebaseAnonymousLoginSystem : MonoBehaviour
    {
        [Header("로그")]
        [SerializeField]
        private bool logAuthResult = true;

        private FirebaseAuth auth;
        private bool isInitializing;
        private bool isInitialized;

        public bool IsSignedIn => auth != null && auth.CurrentUser != null;

        private async void Awake()
        {
            await InitializeAsync();
        }

        public async Task<bool> EnsureSignedInAsync()
        {
            await InitializeAsync();

            if (auth == null)
            {
                Debug.LogWarning("[FirebaseAnonymousLoginSystem] FirebaseAuth 초기화 실패.");
                return false;
            }

            if (auth.CurrentUser != null)
            {
                if (logAuthResult)
                    Debug.Log($"[FirebaseAnonymousLoginSystem] 이미 로그인 상태입니다. UserId: {auth.CurrentUser.UserId}", this);

                return true;
            }

            try
            {
                AuthResult result = await auth.SignInAnonymouslyAsync();

                if (result == null || result.User == null)
                {
                    Debug.LogWarning("[FirebaseAnonymousLoginSystem] 익명 로그인 결과가 비어 있습니다.", this);
                    return false;
                }

                if (logAuthResult)
                    Debug.Log($"[FirebaseAnonymousLoginSystem] 익명 로그인 성공. UserId: {result.User.UserId}", this);

                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[FirebaseAnonymousLoginSystem] 익명 로그인 실패: {exception.Message}", this);
                return false;
            }
        }

        private async Task InitializeAsync()
        {
            if (isInitialized)
                return;

            while (isInitializing)
                await Task.Yield();

            if (isInitialized)
                return;

            isInitializing = true;

            try
            {
                DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

                if (dependencyStatus != DependencyStatus.Available)
                {
                    Debug.LogWarning($"[FirebaseAnonymousLoginSystem] Firebase 의존성 확인 실패: {dependencyStatus}", this);
                    return;
                }

                auth = FirebaseAuth.DefaultInstance;
                isInitialized = true;

                if (logAuthResult)
                    Debug.Log("[FirebaseAnonymousLoginSystem] FirebaseAuth 초기화 완료.", this);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[FirebaseAnonymousLoginSystem] FirebaseAuth 초기화 예외: {exception.Message}", this);
            }
            finally
            {
                isInitializing = false;
            }
        }
    }
}
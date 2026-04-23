using System;
using System.Threading.Tasks;
using Firebase.Auth;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// Firebase Auth 래퍼. 이메일/비번 가입, 로그인, 로그아웃을 담당한다.
    /// PersistentSystems 하위의 자기 GameObject에 부착 (DontDestroyOnLoad).
    ///
    /// Unity Anonymous Auth (RelayManager 담당)와 완전히 독립이다.
    /// 유저에게 보이는 계정 시스템은 이쪽이다.
    /// </summary>
    public class FirebaseAuthManager : MonoBehaviour
    {
        private FirebaseAuth auth;

        public FirebaseUser CurrentUser => auth?.CurrentUser;
        public string CurrentUid => CurrentUser?.UserId ?? string.Empty;
        public string CurrentEmail => CurrentUser?.Email ?? string.Empty;
        public bool IsSignedIn => CurrentUser != null;

        private void Awake()
        {
            ServiceLocator.Register(this);
        }

        private void Start()
        {
            // FirebaseBootstrap이 먼저 초기화되어야 하므로 약간의 지연을 둔다.
            // 실전에서는 FirebaseBootstrap.IsReady를 폴링하거나 이벤트로 받는 것이 안전하다.
            TryAttachAuth();
        }

        private void OnDestroy()
        {
            if (auth != null)
            {
                auth.StateChanged -= OnAuthStateChanged;
            }
            ServiceLocator.Unregister<FirebaseAuthManager>();
        }

        private void TryAttachAuth()
        {
            var bootstrap = ServiceLocator.Get<FirebaseBootstrap>();
            if (bootstrap == null || !bootstrap.IsReady)
            {
                // 아직 준비 안 됨 → 잠시 후 재시도
                Invoke(nameof(TryAttachAuth), 0.1f);
                return;
            }

            auth = FirebaseAuth.DefaultInstance;
            auth.StateChanged += OnAuthStateChanged;
            Debug.Log("[FirebaseAuthManager] FirebaseAuth에 연결 완료");
        }

        private void OnAuthStateChanged(object sender, EventArgs e)
        {
            if (auth.CurrentUser != null)
            {
                EventBus.Publish(new AuthSignedInEvent
                {
                    firebaseUid = auth.CurrentUser.UserId,
                    email = auth.CurrentUser.Email ?? string.Empty,
                });
            }
            else
            {
                EventBus.Publish(new AuthSignedOutEvent { firebaseUid = string.Empty });
            }
        }

        /// <summary>
        /// 신규 계정 생성. 성공 시 FirebaseUser 반환, 실패 시 null.
        /// </summary>
        public async Task<FirebaseUser> RegisterAsync(string email, string password)
        {
            if (auth == null)
            {
                Debug.LogError("[FirebaseAuthManager] Auth가 아직 연결되지 않았다");
                return null;
            }

            try
            {
                var result = await auth.CreateUserWithEmailAndPasswordAsync(email, password);
                return result.User;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseAuthManager] 가입 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 기존 계정으로 로그인. 성공 시 FirebaseUser 반환, 실패 시 null.
        /// </summary>
        public async Task<FirebaseUser> SignInAsync(string email, string password)
        {
            if (auth == null)
            {
                Debug.LogError("[FirebaseAuthManager] Auth가 아직 연결되지 않았다");
                return null;
            }

            try
            {
                var result = await auth.SignInWithEmailAndPasswordAsync(email, password);
                return result.User;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseAuthManager] 로그인 실패: {e.Message}");
                return null;
            }
        }

        public void SignOut()
        {
            if (auth == null) return;
            auth.SignOut();
        }
    }
}

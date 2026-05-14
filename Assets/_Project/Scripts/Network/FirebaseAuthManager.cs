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
        private string lastKnownUid = string.Empty;

        public FirebaseUser CurrentUser => auth?.CurrentUser;
        public string CurrentUid => CurrentUser?.UserId ?? string.Empty;
        public string CurrentEmail => CurrentUser?.Email ?? string.Empty;
        public bool IsReady => auth != null;
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
            ServiceLocator.Unregister(this);
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
                lastKnownUid = auth.CurrentUser.UserId;
                Debug.Log("[FirebaseAuthManager] FirebaseAuth signed-in state detected. Waiting for explicit login flow.");
            }
            else
            {
                EventBus.Publish(new AuthSignedOutEvent { firebaseUid = lastKnownUid });
                lastKnownUid = string.Empty;
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
                PublishSignedInEvent(result.User);
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
                PublishSignedInEvent(result.User);
                return result.User;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseAuthManager] 로그인 실패: type={e.GetType().Name}, message={e.Message}, inner={e.InnerException?.Message ?? "none"}");
                return null;
            }
        }

        /// <summary>
        /// 외부 인증 공급자의 Firebase Credential로 로그인합니다.
        /// Google 로그인은 Google ID Token을 받아 이 메서드로 연결합니다.
        /// </summary>
        public async Task<FirebaseUser> SignInWithCredentialAsync(Credential credential)
        {
            if (auth == null)
            {
                Debug.LogError("[FirebaseAuthManager] Auth가 아직 연결되지 않았다");
                return null;
            }

            if (credential == null)
            {
                Debug.LogError("[FirebaseAuthManager] Credential이 null입니다.");
                return null;
            }

            try
            {
                FirebaseUser user = await auth.SignInWithCredentialAsync(credential);
                PublishSignedInEvent(user);
                return user;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseAuthManager] Credential 로그인 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Firebase가 로컬에 보관한 기존 로그인 세션을 현재 게임 로그인 플로우로 복원합니다.
        /// </summary>
        public FirebaseUser RestoreCachedUser()
        {
            if (auth == null)
            {
                Debug.LogWarning("[FirebaseAuthManager] Auth가 아직 연결되지 않아 자동 로그인을 진행할 수 없습니다.");
                return null;
            }

            FirebaseUser user = auth.CurrentUser;
            if (user == null)
            {
                return null;
            }

            PublishSignedInEvent(user);
            Debug.Log($"[FirebaseAuthManager] 자동 로그인 세션 복원 완료. Uid={user.UserId}");
            return user;
        }

        /// <summary>
        /// 현재 Firebase 계정에서 로그아웃합니다.
        /// </summary>
        public void SignOut()
        {
            if (auth == null) return;
            auth.SignOut();
        }

        private void PublishSignedInEvent(FirebaseUser user)
        {
            if (user == null) return;

            lastKnownUid = user.UserId;

            EventBus.Publish(new AuthSignedInEvent
            {
                firebaseUid = user.UserId,
                email = user.Email ?? string.Empty,
            });
        }
    }
}

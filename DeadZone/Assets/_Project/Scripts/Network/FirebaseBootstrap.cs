using System.Threading.Tasks;
using Firebase;
using Firebase.Extensions;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// Firebase SDK 초기화 담당. NetworkBootstrap GameObject에 부착된다.
    /// 
    /// 이 컴포넌트는 단 한 가지 일만 한다:
    ///   앱 시작 시 FirebaseApp.CheckAndFixDependenciesAsync()를 1회 실행
    ///   → 성공하면 FirebaseApp 인스턴스를 ServiceLocator에 등록
    ///   → 이후 FirebaseAuthManager와 CloudSaveSystem이 이를 사용
    /// 
    /// Firebase Auth / Firestore 로직은 각자 별도 Manager에서 담당한다.
    /// </summary>
    public class FirebaseBootstrap : MonoBehaviour
    {
        public bool IsReady { get; private set; }
        public FirebaseApp App { get; private set; }

        private void Awake()
        {
            ServiceLocator.Register(this);
        }

        private async void Start()
        {
            await InitializeAsync();
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<FirebaseBootstrap>();
        }

        private Task InitializeAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    App = FirebaseApp.DefaultInstance;
                    ServiceLocator.Register(App);
                    IsReady = true;
                    Debug.Log("[FirebaseBootstrap] Firebase 준비 완료");
                }
                else
                {
                    Debug.LogError($"[FirebaseBootstrap] 의존성 체크 실패: {task.Result}");
                    IsReady = false;
                }
                tcs.TrySetResult(IsReady);
            });

            return tcs.Task;
        }
    }
}

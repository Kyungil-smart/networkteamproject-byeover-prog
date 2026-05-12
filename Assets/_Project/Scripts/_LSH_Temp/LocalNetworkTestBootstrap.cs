using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Network._LSH_Temp
{
    [DisallowMultipleComponent]
    public sealed class LocalNetworkTestBootstrap : MonoBehaviour
    {
        private static bool suppressNextAutoStart;
        private static string suppressReason;

        [Header("====로컬 네트워크 테스트====")]
        [Tooltip("Play Mode 시작 시 NetworkManager.StartHost()를 자동 호출" +
                 "\n테스트 씬 전용")]
        [SerializeField] private bool startHostOnStart = true;

        public static void SuppressNextAutoStart(string reason)
        {
            suppressNextAutoStart = true;
            suppressReason = reason;
        }

        private void Start()
        {
            if (!startHostOnStart) return;

            if (suppressNextAutoStart)
            {
                Debug.Log($"[PartySession] LocalNetworkTestBootstrap auto host skipped. reason={suppressReason}", this);
                suppressNextAutoStart = false;
                suppressReason = string.Empty;
                return;
            }

            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[LocalNetworkTestBootstrap] NetworkManager.Singleton이 없습니다.");
                return;
            }

            if (NetworkManager.Singleton.IsListening) return;

            bool started = NetworkManager.Singleton.StartHost();

            if (!started)
            {
                Debug.LogError
                    ("[LocalNetworkTestBootstrap] StartHost 실패. NetworkManager 설정과 Player Prefab 등록을 확인하세요.");
            }
        }
    }
}

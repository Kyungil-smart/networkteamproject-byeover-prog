using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MapSystemTargetBinder : MonoBehaviour
    {
        [BoxGroup("참조")]
        [Tooltip("이 맵 마커들을 로컬 플레이어에 바인딩합니다. 비워두면 자식에서 자동 수집합니다.")]
        [SerializeField] private MapMarkerFollower[] markerFollowers;

        [BoxGroup("옵션")]
        [SerializeField] private bool autoCollectMarkers = true;

        [BoxGroup("옵션")]
        [SerializeField, Min(0.05f)] private float retryInterval = 0.25f;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private Transform currentTarget;

        private float nextRetryTime;

        private void Awake()
        {
            CollectMarkersIfNeeded();
        }

        private void OnEnable()
        {
            TryBindLocalPlayer();
        }

        private void Update()
        {
            if (currentTarget != null)
                return;

            if (Time.unscaledTime < nextRetryTime)
                return;

            nextRetryTime = Time.unscaledTime + retryInterval;
            TryBindLocalPlayer();
        }

        [Button("맵 마커 수집")]
        public void CollectMarkersIfNeeded()
        {
            if (!autoCollectMarkers)
                return;

            markerFollowers = GetComponentsInChildren<MapMarkerFollower>(true);
        }

        [Button("로컬 플레이어 바인딩")]
        public bool TryBindLocalPlayer()
        {
            Transform localPlayer = ResolveLocalPlayerTransform();
            if (localPlayer == null)
                return false;

            BindTarget(localPlayer);
            return true;
        }

        public void BindTarget(Transform target)
        {
            if (target == null)
                return;

            CollectMarkersIfNeeded();

            currentTarget = target;

            foreach (MapMarkerFollower markerFollower in markerFollowers)
            {
                if (markerFollower == null)
                    continue;

                markerFollower.SetTarget(target);
            }
        }

        private static Transform ResolveLocalPlayerTransform()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
                return null;

            NetworkObject localPlayerObject = networkManager.LocalClient?.PlayerObject;
            return localPlayerObject != null ? localPlayerObject.transform : null;
        }
    }
}

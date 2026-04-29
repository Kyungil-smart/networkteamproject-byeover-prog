using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MapSystemTargetBinder : MonoBehaviour
    {
        [BoxGroup("References")]
        [Tooltip("Bind these map markers to the local player. Empty means auto-collect children.")]
        [SerializeField] private MapMarkerFollower[] markerFollowers;

        [BoxGroup("Options")]
        [SerializeField] private bool autoCollectMarkers = true;

        [BoxGroup("Options")]
        [SerializeField, Min(0.05f)] private float retryInterval = 0.25f;

        [TitleGroup("Debug")]
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

        [Button("Collect Map Markers")]
        public void CollectMarkersIfNeeded()
        {
            if (!autoCollectMarkers)
                return;

            markerFollowers = GetComponentsInChildren<MapMarkerFollower>(true);
        }

        [Button("Bind Local Player")]
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

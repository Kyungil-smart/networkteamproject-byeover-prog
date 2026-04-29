using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MapBoundsProvider : MonoBehaviour
    {
        [BoxGroup("Transform Bounds")]
        [Tooltip("World-space lower-left reference. Uses X/Z.")]
        [SerializeField] private Transform mapBoundMin;

        [BoxGroup("Transform Bounds")]
        [Tooltip("World-space upper-right reference. Uses X/Z.")]
        [SerializeField] private Transform mapBoundMax;

        [BoxGroup("Transform Bounds")]
        [SerializeField] private bool useTransformBounds = true;

        [BoxGroup("Fallback Bounds")]
        [Tooltip("Used only when transform bounds are disabled or missing.")]
        [SerializeField] private Vector2 fallbackWorldMin = new(-250f, -50f);

        [BoxGroup("Fallback Bounds")]
        [Tooltip("Used only when transform bounds are disabled or missing.")]
        [SerializeField] private Vector2 fallbackWorldMax = new(15f, 24f);

        [BoxGroup("Normalized Correction")]
        [Tooltip("이동량 보정의 기준점입니다. rawNormalized가 이 값과 같으면 보정 후에도 같은 위치를 유지합니다.")]
        [SerializeField] private Vector2 normalizedAnchor = new(0.9433962f, 0.6756757f);

        [BoxGroup("Normalized Correction")]
        [Tooltip("normalizedAnchor를 기준으로 플레이어 이동량을 줄이거나 늘립니다.")]
        [SerializeField] private Vector2 normalizedScale = new(0.65f, 0.65f);

        [BoxGroup("Normalized Correction")]
        [Tooltip("정규화 좌표 보정 후 전체 위치를 미세 조정합니다.")]
        [SerializeField] private Vector2 normalizedOffset = Vector2.zero;

        [BoxGroup("Debug")]
        [SerializeField] private bool warnWhenUsingFallback = true;

        private bool warnedMissingTransformBounds;

        private void Awake()
        {
            CreateOrRefreshBoundTransforms();
        }

        public Vector2 WorldMin
        {
            get
            {
                GetBounds(out Vector2 activeWorldMin, out _);
                return activeWorldMin;
            }
        }

        public Vector2 WorldMax
        {
            get
            {
                GetBounds(out _, out Vector2 activeWorldMax);
                return activeWorldMax;
            }
        }

        public Vector2 GetWorldMin()
        {
            if (useTransformBounds && mapBoundMin != null)
            {
                Vector3 localPos = mapBoundMin.localPosition;
                return new Vector2(localPos.x, localPos.z);
            }

            return WorldMin;
        }

        public Vector2 GetWorldMax()
        {
            if (useTransformBounds && mapBoundMax != null)
            {
                Vector3 localPos = mapBoundMax.localPosition;
                return new Vector2(localPos.x, localPos.z);
            }
            return fallbackWorldMax;
        }

        public Vector2 NormalizedScale => normalizedScale;
        public Vector2 NormalizedOffset => normalizedOffset;
        public Vector2 NormalizedAnchor => normalizedAnchor;

        public void GetBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax)
        {
            if (useTransformBounds && mapBoundMin != null && mapBoundMax != null)
            {
                activeWorldMin = new Vector2(mapBoundMin.localPosition.x, mapBoundMin.localPosition.z);
                activeWorldMax = new Vector2(mapBoundMax.localPosition.x, mapBoundMax.localPosition.z);
                MapCoordinateUtility.NormalizeBounds(ref activeWorldMin, ref activeWorldMax);
                warnedMissingTransformBounds = false;
                return;
            }

            activeWorldMin = fallbackWorldMin;
            activeWorldMax = fallbackWorldMax;
            MapCoordinateUtility.NormalizeBounds(ref activeWorldMin, ref activeWorldMax);

            if (!warnWhenUsingFallback || warnedMissingTransformBounds)
                return;

            warnedMissingTransformBounds = true;
            Debug.LogWarning(
                $"[MapBoundsProvider] Using fallback bounds. useTransformBounds={useTransformBounds}, mapBoundMin={(mapBoundMin != null ? mapBoundMin.name : "null")}, mapBoundMax={(mapBoundMax != null ? mapBoundMax.name : "null")}, worldMin={activeWorldMin}, worldMax={activeWorldMax}",
                this);
        }

        public bool Contains(Vector3 worldPosition)
        {
            GetBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            return MapCoordinateUtility.ContainsWorldPosition(worldPosition, activeWorldMin, activeWorldMax);
        }

        [BoxGroup("Preset Buttons")]
        [Button("Move Bounds To Full Map Preset")]
        public void ApplyFullMapBounds()
        {
            fallbackWorldMin = MapCoordinateUtility.FullMapWorldMin;
            fallbackWorldMax = MapCoordinateUtility.FullMapWorldMax;
            MoveBoundTransformsToFallback();
        }

        [BoxGroup("Preset Buttons")]
        [Button("Move Bounds To Fence Preset")]
        public void ApplyFenceBounds()
        {
            fallbackWorldMin = MapCoordinateUtility.FenceWorldMin;
            fallbackWorldMax = MapCoordinateUtility.FenceWorldMax;
            MoveBoundTransformsToFallback();
        }

        [BoxGroup("Transform Buttons")]
        [Button("Create / Refresh MapBound Transforms")]
        public void CreateOrRefreshBoundTransforms()
        {
            Transform root = transform.Find("MapBounds");
            if (root == null)
            {
                GameObject rootObject = new("MapBounds");
                root = rootObject.transform;
                root.SetParent(transform, false);
            }

            bool createdMin = root.Find("MapBound_Min") == null;
            bool createdMax = root.Find("MapBound_Max") == null;

            mapBoundMin = GetOrCreateChild(root, "MapBound_Min");
            mapBoundMax = GetOrCreateChild(root, "MapBound_Max");

            if (createdMin || createdMax)
                MoveBoundTransformsToFallback();
        }

        [BoxGroup("Transform Buttons")]
        [Button("Apply Bounds From Transforms")]
        public void ApplyBoundsFromTransforms()
        {
            if (mapBoundMin == null || mapBoundMax == null)
            {
                Debug.LogWarning("[MapBoundsProvider] Cannot apply bounds from transforms because MapBound_Min or MapBound_Max is missing.", this);
                return;
            }

            fallbackWorldMin = new Vector2(mapBoundMin.localPosition.x, mapBoundMin.localPosition.z);
            fallbackWorldMax = new Vector2(mapBoundMax.localPosition.x, mapBoundMax.localPosition.z);
            MapCoordinateUtility.NormalizeBounds(ref fallbackWorldMin, ref fallbackWorldMax);
            DebugActiveBounds();
        }

        [BoxGroup("Debug")]
        [Button("Debug Active Bounds")]
        public void DebugActiveBounds()
        {
            GetBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            string source = useTransformBounds && mapBoundMin != null && mapBoundMax != null ? "Transform" : "Fallback";
            Debug.Log($"[MapBoundsProvider] source={source}, provider={name}, worldMin={activeWorldMin}, worldMax={activeWorldMax}, normalizedAnchor={normalizedAnchor}, normalizedScale={normalizedScale}, normalizedOffset={normalizedOffset}", this);
        }

        [BoxGroup("Normalized Correction")]
        [Button("현재 로컬 플레이어 위치를 Anchor로 설정")]
        public void SetAnchorFromLocalPlayer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            NetworkObject localPlayerObject = networkManager != null && networkManager.IsListening && networkManager.LocalClient != null
                ? networkManager.LocalClient.PlayerObject
                : null;

            if (localPlayerObject == null)
            {
                Debug.LogWarning("[MapBoundsProvider] 로컬 플레이어를 찾지 못해 Anchor를 설정하지 못했습니다.", this);
                return;
            }

            GetBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            normalizedAnchor = MapCoordinateUtility.WorldToNormalized(
                localPlayerObject.transform.position,
                activeWorldMin,
                activeWorldMax,
                true,
                false);

            Debug.Log($"[MapBoundsProvider] Anchor set from local player. playerX={localPlayerObject.transform.position.x}, playerZ={localPlayerObject.transform.position.z}, normalizedAnchor={normalizedAnchor}", this);
        }

        [BoxGroup("Normalized Correction")]
        [Button("Anchor 초기화")]
        public void ResetAnchor()
        {
            GetBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            normalizedAnchor = MapCoordinateUtility.WorldToNormalized(
                Vector3.zero,
                activeWorldMin,
                activeWorldMax,
                true,
                false);
            Debug.Log($"[MapBoundsProvider] Anchor reset. normalizedAnchor={normalizedAnchor}", this);
        }

        private void MoveBoundTransformsToFallback()
        {
            if (mapBoundMin != null)
            {
                Vector3 localPos = mapBoundMin.localPosition;
                localPos.x = fallbackWorldMin.x;
                localPos.z = fallbackWorldMin.y;
                mapBoundMin.localPosition = localPos;
            }

            if (mapBoundMax != null)
            {
                Vector3 localPos = mapBoundMax.localPosition;
                localPos.x = fallbackWorldMax.x;
                localPos.z = fallbackWorldMax.y;
                mapBoundMax.localPosition = localPos;
            }
        }

        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
                return child;

            GameObject childObject = new(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            return child;
        }
    }
}

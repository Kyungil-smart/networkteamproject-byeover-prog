using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors
{
    public class MapMarkerFollower : MonoBehaviour
    {
        [BoxGroup("참조")]
        [Tooltip("마커 좌표 공간으로 사용할 UI 맵 RectTransform")]
        [SerializeField] private RectTransform mapRect;

        [BoxGroup("참조")]
        [Tooltip("이동시킬 마커 RectTransform")]
        [SerializeField] private RectTransform markerRect;

        [BoxGroup("참조")]
        [Tooltip("월드 대상 Transform입니다. 로컬 플레이어는 런타임에 할당됨.")]
        [SerializeField] private Transform target;

        [BoxGroup("월드 경계")]
        [Tooltip("선택 사항인 공유 경계 제공자입니다. 비워두면 부모에서 찾고, 없으면 아래 직렬화 값을 사용합니다.")]
        [SerializeField] private MapBoundsProvider boundsProvider;

        [BoxGroup("월드 경계")]
        [Tooltip("맵 이미지의 좌하단에 해당하는 월드 X/Z 좌표")]
        [SerializeField] private Vector2 worldMin = new(-406.74f, -149.42f);

        [BoxGroup("월드 경계")]
        [Tooltip("맵 이미지의 우상단에 해당하는 월드 X/Z 좌표")]
        [SerializeField] private Vector2 worldMax = new(6.08078f, 55.42514f);

        [BoxGroup("옵션")]
        [SerializeField] private bool updateEveryFrame = true;

        [BoxGroup("옵션")]
        [SerializeField] private bool clampToMap = true;

        [BoxGroup("옵션")]
        [SerializeField] private bool invertY;

        public Transform Target => target;

        private bool warnedTargetOutsideBounds;

        private void Reset()
        {
            markerRect = transform as RectTransform;
        }

        private void Awake()
        {
            ResolveBoundsProvider();
        }

        private void LateUpdate()
        {
            if (!updateEveryFrame)
                return;

            RefreshMarkerPosition();
        }

        public void SetTarget(Transform newTarget, bool refreshImmediately = true)
        {
            bool targetChanged = target != newTarget;
            target = newTarget;

            if (targetChanged)
                LogTargetBoundsState("SetTarget");

            if (refreshImmediately)
                RefreshMarkerPosition();
        }

        public void Setup(
            RectTransform newMapRect,
            RectTransform newMarkerRect,
            Transform newTarget,
            Vector2 newWorldMin,
            Vector2 newWorldMax)
        {
            mapRect = newMapRect;
            markerRect = newMarkerRect;
            target = newTarget;
            worldMin = newWorldMin;
            worldMax = newWorldMax;
            RefreshMarkerPosition();
        }

        public void SetBoundsProvider(MapBoundsProvider newBoundsProvider, bool refreshImmediately = true)
        {
            boundsProvider = newBoundsProvider;

            if (refreshImmediately)
                RefreshMarkerPosition();
        }

        public void SetInvertY(bool newInvertY)
        {
            invertY = newInvertY;
            RefreshMarkerPosition();
        }

        [Button("전체 맵 경계 적용")]
        public void ApplyFullMapBounds()
        {
            worldMin = MapCoordinateUtility.FullMapWorldMin;
            worldMax = MapCoordinateUtility.FullMapWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFullMapBounds();

            RefreshMarkerPosition();
        }

        [Button("울타리 경계 적용")]
        public void ApplyFenceBounds()
        {
            worldMin = MapCoordinateUtility.FenceWorldMin;
            worldMax = MapCoordinateUtility.FenceWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFenceBounds();

            RefreshMarkerPosition();
        }

        [Button("마커 위치 새로고침")]
        public void RefreshMarkerPosition()
        {
            if (mapRect == null || markerRect == null || target == null)
                return;

            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            WarnIfTargetOutsideBounds(activeWorldMin, activeWorldMax);

            Vector2 correctedNormalized = GetCorrectedNormalizedTargetPosition();
            markerRect.anchoredPosition = MapCoordinateUtility.NormalizedToCenteredRectPosition(correctedNormalized, mapRect);
        }

        [Button("현재 월드맵 위치 디버그")]
        public void DebugCurrentWorldMapPosition()
        {
            if (mapRect == null || markerRect == null || target == null)
            {
                Debug.LogWarning("[MapMarkerFollower] Cannot debug position because mapRect, markerRect, or target is missing.", this);
                return;
            }

            Vector2 rawNormalized = GetRawNormalizedTargetPosition();
            Vector2 correctedNormalized = CorrectNormalized(rawNormalized);
            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            GetNormalizedCorrection(out Vector2 anchor, out Vector2 scale, out Vector2 offset);
            Debug.Log(
                $"[MapMarkerFollower] provider={(boundsProvider != null ? boundsProvider.name : "null")}, mapRect={(mapRect != null ? mapRect.name : "null")}, targetX={target.position.x}, targetZ={target.position.z}, rawNormalized={rawNormalized}, correctedNormalized={correctedNormalized}, normalizedAnchor={anchor}, normalizedScale={scale}, normalizedOffset={offset}, markerPosition={markerRect.anchoredPosition}, worldMin={activeWorldMin}, worldMax={activeWorldMax}",
                this);
        }

        private Vector2 GetCorrectedNormalizedTargetPosition()
        {
            return CorrectNormalized(GetRawNormalizedTargetPosition());
        }

        private Vector2 GetRawNormalizedTargetPosition()
        {
            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            return MapCoordinateUtility.WorldToNormalized(target.position, activeWorldMin, activeWorldMax, false, invertY);
        }

        private void GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax)
        {
            ResolveBoundsProvider();

            if (boundsProvider != null)
            {
                boundsProvider.GetBounds(out activeWorldMin, out activeWorldMax);
                return;
            }

            activeWorldMin = worldMin;
            activeWorldMax = worldMax;
            MapCoordinateUtility.NormalizeBounds(ref activeWorldMin, ref activeWorldMax);
        }

        private void ResolveBoundsProvider()
        {
            if (boundsProvider != null)
                return;

            boundsProvider = GetComponentInParent<MapBoundsProvider>();

            if (boundsProvider == null)
                boundsProvider = FindObjectOfType<MapBoundsProvider>();
        }

        private Vector2 CorrectNormalized(Vector2 rawNormalized)
        {
            GetNormalizedCorrection(out Vector2 anchor, out Vector2 scale, out Vector2 offset);
            return MapCoordinateUtility.ApplyNormalizedCorrection(rawNormalized, anchor, scale, offset, clampToMap);
        }

        private void GetNormalizedCorrection(out Vector2 anchor, out Vector2 scale, out Vector2 offset)
        {
            ResolveBoundsProvider();

            if (boundsProvider != null)
            {
                anchor = boundsProvider.NormalizedAnchor;
                scale = boundsProvider.NormalizedScale;
                offset = boundsProvider.NormalizedOffset;
                return;
            }

            anchor = new Vector2(0.5f, 0.5f);
            scale = Vector2.one;
            offset = Vector2.zero;
        }

        private void WarnIfTargetOutsideBounds(Vector2 activeWorldMin, Vector2 activeWorldMax)
        {
            bool inside = MapCoordinateUtility.ContainsWorldPosition(target.position, activeWorldMin, activeWorldMax);
            if (inside)
            {
                warnedTargetOutsideBounds = false;
                return;
            }

            if (warnedTargetOutsideBounds)
                return;

            warnedTargetOutsideBounds = true;
            Debug.LogWarning(
                $"[MapMarkerFollower] Target is outside map bounds. target={target.name}, position=({target.position.x}, {target.position.z}), worldMin={activeWorldMin}, worldMax={activeWorldMax}",
                this);
        }

        private void LogTargetBoundsState(string reason)
        {
            if (target == null)
                return;

            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            Vector2 rawNormalized = MapCoordinateUtility.WorldToNormalized(target.position, activeWorldMin, activeWorldMax, false, invertY);
            Vector2 correctedNormalized = CorrectNormalized(rawNormalized);
            GetNormalizedCorrection(out Vector2 anchor, out Vector2 scale, out Vector2 offset);
            bool inside = MapCoordinateUtility.ContainsWorldPosition(target.position, activeWorldMin, activeWorldMax);
            Debug.Log(
                $"[MapMarkerFollower] {reason}: provider={(boundsProvider != null ? boundsProvider.name : "null")}, mapRect={(mapRect != null ? mapRect.name : "null")}, playerX={target.position.x}, playerZ={target.position.z}, worldMin={activeWorldMin}, worldMax={activeWorldMax}, inside={inside}, rawNormalized={rawNormalized}, correctedNormalized={correctedNormalized}, normalizedAnchor={anchor}, normalizedScale={scale}, normalizedOffset={offset}",
                this);
        }

#if UNITY_EDITOR
        [Button("마커를 자기 자신으로 설정")]
        private void SetMarkerToSelf()
        {
            markerRect = transform as RectTransform;
        }
#endif
    }


}

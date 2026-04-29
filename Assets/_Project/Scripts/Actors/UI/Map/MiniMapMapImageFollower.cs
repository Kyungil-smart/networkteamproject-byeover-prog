using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MiniMapMapImageFollower : MonoBehaviour
    {
        [BoxGroup("References")]
        [Tooltip("MiniMap root. If empty, this component uses itself or finds a child named MiniMap.")]
        [SerializeField] private RectTransform miniMapRoot;

        [BoxGroup("References")]
        [Tooltip("The moving world map image RectTransform. Example: MiniMapMapImage.")]
        [SerializeField] private RectTransform mapImageRect;

        [BoxGroup("References")]
        [Tooltip("The local player Transform to follow. In network play this is assigned at runtime.")]
        [SerializeField] private Transform target;

        [BoxGroup("References")]
        [Tooltip("Optional shared bounds provider. If empty, this component searches its parents and then falls back to the serialized values below.")]
        [SerializeField] private MapBoundsProvider boundsProvider;

        [BoxGroup("Runtime Structure")]
        [SerializeField] private RectTransform miniMapMask;

        [BoxGroup("Runtime Structure")]
        [SerializeField] private RectTransform markerRoot;

        [BoxGroup("Runtime Structure")]
        [SerializeField] private RectTransform localPlayerMarker;

        [BoxGroup("Runtime Structure")]
        [SerializeField] private RectTransform miniMapFrame;

        [BoxGroup("Marker")]
        [Tooltip("Optional fallback icon for the local player marker. If empty, the existing Image sprite is preserved.")]
        [SerializeField] private Sprite localPlayerIconSprite;

        [BoxGroup("World Bounds")]
        [Tooltip("World X/Z coordinate for the bottom-left of the map image.")]
        [SerializeField] private Vector2 worldMin = new(-406.74f, -149.42f);

        [BoxGroup("World Bounds")]
        [Tooltip("World X/Z coordinate for the top-right of the map image.")]
        [SerializeField] private Vector2 worldMax = new(6.08078f, 55.42514f);

        [BoxGroup("Layout")]
        [SerializeField] private Vector2 miniMapSize = new(300f, 300f);

        [BoxGroup("Layout")]
        [SerializeField] private Vector2 mapImageSize = new(1000f, 563f);

        [BoxGroup("Layout")]
        [SerializeField] private Vector2 localMarkerSize = new(16f, 16f);

        [BoxGroup("Options")]
        [SerializeField] private bool updateEveryFrame = true;

        [BoxGroup("Options")]
        [SerializeField] private bool clampToMap = true;

        [BoxGroup("Options")]
        [SerializeField] private bool invertY;

        [BoxGroup("Options")]
        [Tooltip("When true, the local marker stays centered even when the map image is clamped at the edges.")]
        [SerializeField] private bool keepLocalMarkerCenteredAtEdges;

        [BoxGroup("Options")]
        [Tooltip("If target is empty, bind to NetworkManager.Singleton.LocalClient.PlayerObject.")]
        [SerializeField] private bool autoBindLocalPlayer = true;

        [BoxGroup("Debug")]
        [SerializeField] private bool logStructureBuild = true;

        private bool warnedTargetOutsideBounds;

        private void Awake()
        {
            ResolveBoundsProvider();
            EnsureMiniMapStructure();
        }

        private void LateUpdate()
        {
            if (autoBindLocalPlayer && target == null)
                TryBindLocalPlayer();

            if (!updateEveryFrame)
                return;

            Refresh();
        }

        [Button("Resolve / Build MiniMap Structure")]
        public void EnsureMiniMapStructure()
        {
            ResolveMiniMapRoot();

            if (miniMapRoot == null)
            {
                Debug.LogWarning("[MiniMapMapImageFollower] MiniMap root was not found.", this);
                return;
            }

            EnsureBoundsProviderOnMapSystemRoot();
            miniMapFrame = FindDirectChild(miniMapRoot, "MiniMapFrame");
            if (miniMapFrame == null)
                miniMapFrame = FindDirectChild(miniMapRoot, "MiniMapBG");

            if (miniMapFrame != null)
                miniMapFrame.name = "MiniMapFrame";

            RebaseMiniMapRootToFrame();

            Image frameImage = miniMapFrame != null ? miniMapFrame.GetComponent<Image>() : null;
            Sprite frameSprite = frameImage != null ? frameImage.sprite : null;
            Sprite worldMapSprite = FindWorldMapSprite();

            miniMapMask = GetOrCreateRect(miniMapRoot, "MiniMapMask");
            miniMapMask.SetParent(miniMapRoot, false);
            ResetCenteredRect(miniMapMask, miniMapSize);

            Image maskImage = GetOrAddImage(miniMapMask.gameObject);
            maskImage.sprite = frameSprite;
            maskImage.color = Color.white;
            maskImage.raycastTarget = false;

            Mask mask = miniMapMask.GetComponent<Mask>();
            if (mask == null)
                mask = miniMapMask.gameObject.AddComponent<Mask>();

            mask.showMaskGraphic = false;

            mapImageRect = GetOrCreateRect(miniMapMask, "MiniMapMapImage");
            mapImageRect.SetParent(miniMapMask, false);
            ResetCenteredRect(mapImageRect, mapImageSize);

            Image mapImage = GetOrAddImage(mapImageRect.gameObject);
            mapImage.sprite = worldMapSprite;
            mapImage.preserveAspect = true;
            mapImage.raycastTarget = false;

            markerRoot = GetOrCreateRect(miniMapRoot, "MarkerRoot_Minimap");
            markerRoot.SetParent(miniMapRoot, false);
            ResetCenteredRect(markerRoot, miniMapSize);

            localPlayerMarker = FindDirectChild(markerRoot, "LocalPlayerMarker");
            if (localPlayerMarker == null)
            {
                RectTransform existingMarker = FindDirectChild(miniMapRoot, "PlayerMarker_Minimap");
                localPlayerMarker = existingMarker != null
                    ? existingMarker
                    : GetOrCreateRect(markerRoot, "LocalPlayerMarker");
            }

            localPlayerMarker.name = "LocalPlayerMarker";
            localPlayerMarker.SetParent(markerRoot, false);
            ResetCenteredRect(localPlayerMarker, localMarkerSize);

            RemoveFollowerFromLocalMarker();

            Image markerImage = GetOrAddImage(localPlayerMarker.gameObject);
            markerImage.raycastTarget = false;
            LogMarkerImage("Local marker", localPlayerMarker, markerImage);

            if (frameImage != null)
                frameImage.raycastTarget = false;

            miniMapMask.SetAsFirstSibling();
            markerRoot.SetSiblingIndex(1);

            if (miniMapFrame != null)
            {
                miniMapFrame.SetParent(miniMapRoot, false);
                ResetCenteredRect(miniMapFrame, miniMapSize);
                miniMapFrame.SetAsLastSibling();
            }

            LogRectState("MiniMapMask", miniMapMask);
            LogRectState("MiniMapMapImage", mapImageRect);
            LogRectState("MarkerRoot_Minimap", markerRoot);
            LogRectState("LocalPlayerMarker", localPlayerMarker);
            LogRectState("MiniMapFrame", miniMapFrame);
            RemoveGeneratedMiniMapMarkers();
            LogMiniMapMarkers();
            WarnUnexpectedMiniMapFollowers();
        }

        [Button("Bind Local Player")]
        public bool TryBindLocalPlayer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            NetworkObject localPlayerObject = networkManager != null && networkManager.IsListening && networkManager.LocalClient != null
                ? networkManager.LocalClient.PlayerObject
                : null;

            if (localPlayerObject == null)
                return false;

            SetTarget(localPlayerObject.transform);
            return true;
        }

        [Button("Refresh MiniMap Image Position")]
        public void Refresh()
        {
            if (mapImageRect == null || target == null)
                return;

            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            WarnIfTargetOutsideBounds(activeWorldMin, activeWorldMax);

            Vector2 correctedNormalized = GetCorrectedNormalizedTargetPosition();
            Vector2 mapPoint = MapCoordinateUtility.NormalizedToCenteredRectPosition(correctedNormalized, mapImageRect);
            Vector2 desiredPosition = -mapPoint;
            Vector2 clampedPosition = ClampMapPosition(desiredPosition);

            mapImageRect.anchoredPosition = clampedPosition;
            ApplyLocalMarkerEdgeOffset(desiredPosition, clampedPosition);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            LogTargetBoundsState("BindTarget");
            Refresh();
        }

        [Button("Apply Full Map Bounds")]
        public void ApplyFullMapBounds()
        {
            worldMin = MapCoordinateUtility.FullMapWorldMin;
            worldMax = MapCoordinateUtility.FullMapWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFullMapBounds();

            Refresh();
        }

        [Button("Apply Fence Bounds")]
        public void ApplyFenceBounds()
        {
            worldMin = MapCoordinateUtility.FenceWorldMin;
            worldMax = MapCoordinateUtility.FenceWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFenceBounds();

            Refresh();
        }

        [Button("Debug Current MiniMap Position")]
        public void DebugCurrentMiniMapPosition()
        {
            if (mapImageRect == null || target == null)
            {
                Debug.LogWarning("[MiniMapMapImageFollower] Cannot debug position because mapImageRect or target is missing.", this);
                return;
            }

            Vector2 rawNormalized = GetRawNormalizedTargetPosition();
            Vector2 correctedNormalized = CorrectNormalized(rawNormalized);
            Vector2 mapPoint = MapCoordinateUtility.NormalizedToCenteredRectPosition(correctedNormalized, mapImageRect);
            Vector2 desiredPosition = -mapPoint;
            Vector2 clampedPosition = ClampMapPosition(desiredPosition);
            Vector2 markerOffset = desiredPosition - clampedPosition;
            Vector2 markerPosition = localPlayerMarker != null ? localPlayerMarker.anchoredPosition : Vector2.zero;
            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            GetNormalizedCorrection(out Vector2 anchor, out Vector2 scale, out Vector2 offset);

            Debug.Log(
                $"[MiniMapMapImageFollower] provider={(boundsProvider != null ? boundsProvider.name : "null")}, mapImageRect={(mapImageRect != null ? mapImageRect.name : "null")}, targetX={target.position.x}, targetZ={target.position.z}, rawNormalized={rawNormalized}, correctedNormalized={correctedNormalized}, normalizedAnchor={anchor}, normalizedScale={scale}, normalizedOffset={offset}, desiredMapPosition={desiredPosition}, clampedMapPosition={clampedPosition}, markerOffset={markerOffset}, localMarkerPosition={markerPosition}, worldMin={activeWorldMin}, worldMax={activeWorldMax}",
                this);
        }

        [Button("Log MiniMap Markers")]
        public void LogMiniMapMarkers()
        {
            DebugMiniMapMarkerObjects();
        }

        [Button("Debug MiniMap Marker Objects")]
        public void DebugMiniMapMarkerObjects()
        {
            if (miniMapRoot == null)
                ResolveMiniMapRoot();

            if (miniMapRoot == null)
                return;

            RectTransform[] rectTransforms = miniMapRoot.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform rectTransform in rectTransforms)
            {
                if (!IsMarkerLikeName(rectTransform.name))
                    continue;

                Image image = rectTransform.GetComponent<Image>();
                string spriteName = image != null && image.sprite != null ? image.sprite.name : "null";
                Debug.Log(
                    $"[MiniMap] Marker object={rectTransform.name}, parent={rectTransform.parent?.name}, active={rectTransform.gameObject.activeInHierarchy}, sprite={spriteName}",
                    this);
            }
        }

        public void Setup(
            RectTransform newMapImageRect,
            Transform newTarget,
            Vector2 newWorldMin,
            Vector2 newWorldMax)
        {
            mapImageRect = newMapImageRect;
            target = newTarget;
            worldMin = newWorldMin;
            worldMax = newWorldMax;

            Refresh();
        }

        private void ResolveMiniMapRoot()
        {
            if (miniMapRoot != null)
                return;

            if (transform is RectTransform rectTransform && transform.name == "MiniMap")
            {
                miniMapRoot = rectTransform;
                return;
            }

            foreach (RectTransform child in GetComponentsInChildren<RectTransform>(true))
            {
                if (child.name == "MiniMap")
                {
                    miniMapRoot = child;
                    return;
                }
            }
        }

        private Sprite FindWorldMapSprite()
        {
            foreach (Image image in transform.root.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "Png_WorldMap_01" && image.sprite != null)
                    return image.sprite;
            }

            return null;
        }

        private Vector2 GetFrameSize()
        {
            if (miniMapFrame == null)
                return miniMapSize;

            float width = GetRectWidth(miniMapFrame);
            float height = GetRectHeight(miniMapFrame);
            return width > 0f && height > 0f ? new Vector2(width, height) : miniMapSize;
        }

        private void RebaseMiniMapRootToFrame()
        {
            if (miniMapRoot == null || miniMapFrame == null || miniMapFrame.parent != miniMapRoot)
                return;

            bool rootLooksLikeFullscreenContainer =
                miniMapRoot.anchorMin == Vector2.zero &&
                miniMapRoot.anchorMax == Vector2.one &&
                miniMapRoot.sizeDelta == Vector2.zero;

            bool frameCarriesVisualPosition = miniMapFrame.anchoredPosition != Vector2.zero;
            if (!rootLooksLikeFullscreenContainer || !frameCarriesVisualPosition)
                return;

            miniMapRoot.anchorMin = miniMapFrame.anchorMin;
            miniMapRoot.anchorMax = miniMapFrame.anchorMax;
            miniMapRoot.pivot = miniMapFrame.pivot;
            miniMapRoot.anchoredPosition = miniMapFrame.anchoredPosition;
            miniMapRoot.sizeDelta = miniMapSize;
            miniMapRoot.localScale = Vector3.one;
            miniMapRoot.localRotation = Quaternion.identity;

            miniMapFrame.anchorMin = new Vector2(0.5f, 0.5f);
            miniMapFrame.anchorMax = new Vector2(0.5f, 0.5f);
            miniMapFrame.pivot = new Vector2(0.5f, 0.5f);
            miniMapFrame.anchoredPosition = Vector2.zero;
            miniMapFrame.localScale = Vector3.one;
            miniMapFrame.localRotation = Quaternion.identity;

            if (logStructureBuild)
            {
                Debug.Log(
                    $"[MiniMapMapImageFollower] Rebased MiniMap root to MiniMapFrame. rootParent={miniMapRoot.parent?.name}, rootAnchoredPosition={miniMapRoot.anchoredPosition}, rootSize={miniMapRoot.sizeDelta}",
                    this);
            }
        }

        private Vector2 ClampMapPosition(Vector2 desiredPosition)
        {
            if (miniMapMask == null || mapImageRect == null)
                return desiredPosition;

            float maskWidth = GetRectWidth(miniMapMask);
            float maskHeight = GetRectHeight(miniMapMask);
            float mapImageWidth = GetRectWidth(mapImageRect);
            float mapImageHeight = GetRectHeight(mapImageRect);

            float maxX = Mathf.Max(0f, (mapImageWidth - maskWidth) * 0.5f);
            float maxY = Mathf.Max(0f, (mapImageHeight - maskHeight) * 0.5f);

            float x = Mathf.Clamp(desiredPosition.x, -maxX, maxX);
            float y = Mathf.Clamp(desiredPosition.y, -maxY, maxY);

            return new Vector2(x, y);
        }

        private void ApplyLocalMarkerEdgeOffset(Vector2 desiredMapPosition, Vector2 clampedMapPosition)
        {
            if (localPlayerMarker == null)
                return;

            if (keepLocalMarkerCenteredAtEdges)
            {
                localPlayerMarker.anchoredPosition = Vector2.zero;
                return;
            }

            Vector2 markerOffset = desiredMapPosition - clampedMapPosition;
            localPlayerMarker.anchoredPosition = -markerOffset;
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

        private void EnsureBoundsProviderOnMapSystemRoot()
        {
            ResolveBoundsProvider();
            if (boundsProvider != null || miniMapRoot == null)
                return;

            WorldMapController worldMapController = miniMapRoot.GetComponentInParent<WorldMapController>();
            if (worldMapController == null)
                return;

            boundsProvider = worldMapController.GetComponent<MapBoundsProvider>();
            if (boundsProvider == null)
            {
                boundsProvider = worldMapController.gameObject.AddComponent<MapBoundsProvider>();
                boundsProvider.CreateOrRefreshBoundTransforms();
            }
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
                $"[MiniMapMapImageFollower] Target is outside map bounds. target={target.name}, position=({target.position.x}, {target.position.z}), worldMin={activeWorldMin}, worldMax={activeWorldMax}",
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
                $"[MiniMapMapImageFollower] {reason}: provider={(boundsProvider != null ? boundsProvider.name : "null")}, mapImageRect={(mapImageRect != null ? mapImageRect.name : "null")}, playerX={target.position.x}, playerZ={target.position.z}, worldMin={activeWorldMin}, worldMax={activeWorldMax}, inside={inside}, rawNormalized={rawNormalized}, correctedNormalized={correctedNormalized}, normalizedAnchor={anchor}, normalizedScale={scale}, normalizedOffset={offset}",
                this);
        }

        private void WarnUnexpectedMiniMapFollowers()
        {
            if (miniMapRoot == null)
                return;

            foreach (MapMarkerFollower follower in miniMapRoot.GetComponentsInChildren<MapMarkerFollower>(true))
            {
                if (follower.name == "LocalPlayerMarker")
                    continue;

                Debug.LogWarning(
                    $"[MiniMap] Unexpected MapMarkerFollower under MiniMap. object={follower.name}, parent={follower.transform.parent?.name}. MiniMap should only use LocalPlayerMarker.",
                    follower);
            }
        }

        private void RemoveGeneratedMiniMapMarkers()
        {
            if (miniMapRoot == null)
                return;

            RectTransform[] children = miniMapRoot.GetComponentsInChildren<RectTransform>(true);
            for (int i = children.Length - 1; i >= 0; i--)
            {
                RectTransform child = children[i];
                if (child == null || child == miniMapRoot || child == localPlayerMarker)
                    continue;

                if (!IsGeneratedMiniMapMarkerName(child.name))
                    continue;

                Debug.LogWarning(
                    $"[MiniMap] Removing generated minimap marker because LocalPlayerMarker is the only minimap player marker. object={child.name}, parent={child.parent?.name}",
                    child);

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private static bool IsMarkerLikeName(string objectName)
        {
            return objectName == "LocalPlayerMarker" ||
                   objectName.StartsWith("PlayerMarker_Minimap") ||
                   objectName.StartsWith("PlayerMarker_Client_") ||
                   objectName.StartsWith("MiniMapMarker_");
        }

        private static bool IsGeneratedMiniMapMarkerName(string objectName)
        {
            return objectName == "PlayerMarker_Minimap" ||
                   objectName.StartsWith("PlayerMarker_Minimap_Client_") ||
                   objectName.StartsWith("PlayerMarker_Client_") ||
                   objectName.StartsWith("MiniMapMarker_") ||
                   objectName.StartsWith("PlayerMarker_WorldMap_Client_");
        }

        private void RemoveFollowerFromLocalMarker()
        {
            MapMarkerFollower follower = localPlayerMarker.GetComponent<MapMarkerFollower>();
            if (follower == null)
                return;

            if (Application.isPlaying)
                Destroy(follower);
            else
                DestroyImmediate(follower);
        }

        private static RectTransform FindDirectChild(RectTransform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child != null ? child as RectTransform : null;
        }

        private static RectTransform GetOrCreateRect(RectTransform parent, string objectName)
        {
            RectTransform existing = FindDirectChild(parent, objectName);
            if (existing != null)
            {
                existing.SetParent(parent, false);
                return existing;
            }

            GameObject gameObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            return rectTransform;
        }

        private static Image GetOrAddImage(GameObject gameObject)
        {
            Image image = gameObject.GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();

            return image;
        }

        private static void ResetCenteredRect(RectTransform rectTransform, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = size;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localPosition = new Vector3(rectTransform.localPosition.x, rectTransform.localPosition.y, 0f);
        }

        private void LogRectState(string label, RectTransform rectTransform)
        {
            if (!logStructureBuild || rectTransform == null)
                return;

            Debug.Log(
                $"[MiniMapMapImageFollower] {label}: parent={rectTransform.parent?.name}, anchoredPosition={rectTransform.anchoredPosition}, size={rectTransform.sizeDelta}",
                this);
        }

        private void LogMarkerImage(string label, RectTransform markerRect, Image image)
        {
            if (!logStructureBuild || markerRect == null || image == null)
                return;

            string spriteName = image.sprite != null ? image.sprite.name : "null";
            Debug.Log($"[MiniMap] {label} object={markerRect.name}, sprite={spriteName}", this);
        }

        private static float GetRectWidth(RectTransform rectTransform)
        {
            return rectTransform.rect.width > 0f ? rectTransform.rect.width : rectTransform.sizeDelta.x;
        }

        private static float GetRectHeight(RectTransform rectTransform)
        {
            return rectTransform.rect.height > 0f ? rectTransform.rect.height : rectTransform.sizeDelta.y;
        }
    }
}

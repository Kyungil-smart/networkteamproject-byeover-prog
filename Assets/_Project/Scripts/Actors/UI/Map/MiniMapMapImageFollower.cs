using Sirenix.OdinInspector;
using DeadZone.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MiniMapMapImageFollower : MonoBehaviour
    {
        [BoxGroup("참조")]
        [Tooltip("미니맵 루트입니다. 비워두면 이 컴포넌트 자신을 사용하거나 MiniMap 이름의 자식을 찾습니다.")]
        [SerializeField] private RectTransform miniMapRoot;

        [BoxGroup("참조")]
        [Tooltip("이동하는 월드맵 이미지 RectTransform입니다. 예: MiniMapMapImage")]
        [SerializeField] private RectTransform mapImageRect;

        [BoxGroup("참조")]
        [Tooltip("따라갈 로컬 플레이어 Transform입니다. 네트워크 플레이에서는 런타임에 할당됩니다.")]
        [SerializeField] private Transform target;

        [BoxGroup("참조")]
        [Tooltip("선택 사항인 공유 경계 제공자입니다. 비워두면 부모에서 찾고, 없으면 아래 직렬화 값을 사용합니다.")]
        [SerializeField] private MapBoundsProvider boundsProvider;

        [BoxGroup("스테이지 설정")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true)]
        [SerializeField] private MinimapStageConfig[] stageConfigs;

        [BoxGroup("이미지")]
        [Tooltip("미니맵 배경 Image입니다. 비워두면 MiniMapMapImage에서 자동으로 찾습니다.")]
        [SerializeField] private Image minimapImage;

        [BoxGroup("이미지")]
        [Tooltip("전체 지도 Image입니다. 비워두면 Png_WorldMap_01에서 자동으로 찾습니다.")]
        [SerializeField] private Image worldMapImage;

        [BoxGroup("런타임 구조")]
        [SerializeField] private RectTransform miniMapMask;

        [BoxGroup("런타임 구조")]
        [SerializeField] private RectTransform markerRoot;

        [BoxGroup("런타임 구조")]
        [SerializeField] private RectTransform localPlayerMarker;

        [BoxGroup("런타임 구조")]
        [SerializeField] private RectTransform miniMapFrame;

        [BoxGroup("마커")]
        [Tooltip("로컬 플레이어 마커에 사용할 선택 사항 대체 아이콘입니다. 비워두면 기존 이미지 스프라이트를 유지합니다.")]
        [SerializeField] private Sprite localPlayerIconSprite;

        [BoxGroup("월드 경계")]
        [Tooltip("맵 이미지의 좌하단에 해당하는 월드 X/Z 좌표입니다.")]
        [SerializeField] private Vector2 worldMin = new(-406.74f, -149.42f);

        [BoxGroup("월드 경계")]
        [Tooltip("맵 이미지의 우상단에 해당하는 월드 X/Z 좌표입니다.")]
        [SerializeField] private Vector2 worldMax = new(6.08078f, 55.42514f);

        [BoxGroup("레이아웃")]
        [SerializeField] private Vector2 miniMapSize = new(300f, 300f);

        [BoxGroup("레이아웃")]
        [SerializeField] private Vector2 mapImageSize = new(1000f, 563f);

        [BoxGroup("레이아웃")]
        [SerializeField] private Vector2 localMarkerSize = new(16f, 16f);

        [BoxGroup("옵션")]
        [SerializeField] private bool updateEveryFrame = true;

        [BoxGroup("옵션")]
        [SerializeField] private bool clampToMap = true;

        [BoxGroup("옵션")]
        [SerializeField] private bool invertY;

        [BoxGroup("옵션")]
        [Tooltip("켜면 맵 이미지가 가장자리에서 제한되어도 로컬 마커를 중앙에 유지합니다.")]
        [SerializeField] private bool keepLocalMarkerCenteredAtEdges;

        [BoxGroup("옵션")]
        [Tooltip("대상이 비어 있으면 NetworkManager.Singleton.LocalClient.PlayerObject에 바인딩합니다.")]
        [SerializeField] private bool autoBindLocalPlayer = true;

        [BoxGroup("디버그")]
        [SerializeField] private bool logStructureBuild = true;

        private bool warnedTargetOutsideBounds;
        private MinimapStageConfig currentStageConfig;

        private void OnEnable()
        {
            PartyPlayerColorCache.Changed += ApplyLocalPlayerMarkerColor;
            ApplyLocalPlayerMarkerColor();
        }

        private void OnDisable()
        {
            PartyPlayerColorCache.Changed -= ApplyLocalPlayerMarkerColor;
        }

        private void Awake()
        {
            ResolveBoundsProvider();
            ApplyConfigByCurrentScene();
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

        [Button("미니맵 구조 찾기/생성")]
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
            Sprite worldMapSprite = GetCurrentMinimapSprite();

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

            minimapImage = GetOrAddImage(mapImageRect.gameObject);
            minimapImage.sprite = worldMapSprite;
            minimapImage.preserveAspect = true;
            minimapImage.raycastTarget = false;
            ApplyWorldMapImageSprite();

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
            ApplyLocalPlayerMarkerColor(markerImage);
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

        [Button("로컬 플레이어 바인딩")]
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

        [Button("미니맵 이미지 위치 새로고침")]
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

        [Button("전체 맵 경계 적용")]
        public void ApplyFullMapBounds()
        {
            worldMin = MapCoordinateUtility.FullMapWorldMin;
            worldMax = MapCoordinateUtility.FullMapWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFullMapBounds();

            Refresh();
        }

        [Button("울타리 경계 적용")]
        public void ApplyFenceBounds()
        {
            worldMin = MapCoordinateUtility.FenceWorldMin;
            worldMax = MapCoordinateUtility.FenceWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFenceBounds();

            Refresh();
        }

        [Button("현재 미니맵 위치 디버그")]
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

        [Button("미니맵 마커 로그")]
        public void LogMiniMapMarkers()
        {
            DebugMiniMapMarkerObjects();
        }

        [Button("미니맵 마커 오브젝트 디버그")]
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

        [Button("현재 씬 미니맵 설정 적용")]
        public void ApplyConfigByCurrentScene()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;

            if (stageConfigs == null || stageConfigs.Length == 0)
            {
                Debug.LogWarning($"[MinimapSystem] No minimap configs assigned for scene: {currentSceneName}", this);
                return;
            }

            foreach (MinimapStageConfig config in stageConfigs)
            {
                if (config == null)
                    continue;

                if (config.SceneName == currentSceneName)
                {
                    ApplyConfig(config);
                    return;
                }
            }

            Debug.LogWarning($"[MinimapSystem] No minimap config found for scene: {currentSceneName}", this);
        }

        public void ApplyConfig(MinimapStageConfig config)
        {
            if (config == null)
                return;

            currentStageConfig = config;
            worldMin = config.WorldMin;
            worldMax = config.WorldMax;
            miniMapSize = config.MiniMapSize;
            mapImageSize = config.MapImageSize;

            if (boundsProvider != null)
                boundsProvider.ApplyStageConfig(config);

            ApplyMinimapImageSprite();
            ApplyWorldMapImageSprite();
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

        private Image FindWorldMapImage()
        {
            foreach (Image image in transform.root.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "Png_WorldMap_01")
                    return image;
            }

            return null;
        }

        private Sprite GetCurrentMinimapSprite()
        {
            if (currentStageConfig != null && currentStageConfig.MinimapSprite != null)
                return currentStageConfig.MinimapSprite;

            return FindWorldMapSprite();
        }

        private Sprite GetCurrentWorldMapSprite()
        {
            if (currentStageConfig != null && currentStageConfig.WorldMapSprite != null)
                return currentStageConfig.WorldMapSprite;

            return GetCurrentMinimapSprite();
        }

        private void ApplyMinimapImageSprite()
        {
            if (minimapImage == null && mapImageRect != null)
                minimapImage = mapImageRect.GetComponent<Image>();

            if (minimapImage == null)
                return;

            Sprite sprite = GetCurrentMinimapSprite();
            if (sprite == null)
            {
                Debug.LogWarning($"[MinimapSystem] Minimap sprite is missing for scene: {SceneManager.GetActiveScene().name}", this);
                return;
            }

            minimapImage.sprite = sprite;
            minimapImage.preserveAspect = true;
        }

        private void ApplyWorldMapImageSprite()
        {
            if (worldMapImage == null)
                worldMapImage = FindWorldMapImage();

            if (worldMapImage == null)
                return;

            Sprite sprite = GetCurrentWorldMapSprite();
            if (sprite == null)
            {
                Debug.LogWarning($"[MinimapSystem] World map sprite is missing for scene: {SceneManager.GetActiveScene().name}", this);
                return;
            }

            worldMapImage.sprite = sprite;
            worldMapImage.preserveAspect = true;
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

        private void ApplyLocalPlayerMarkerColor()
        {
            Image markerImage = localPlayerMarker != null ? localPlayerMarker.GetComponent<Image>() : null;
            ApplyLocalPlayerMarkerColor(markerImage);
        }

        private void ApplyLocalPlayerMarkerColor(Image markerImage)
        {
            if (markerImage == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
                return;

            if (PartyPlayerColorCache.TryGetColor(NetworkManager.Singleton.LocalClientId, out Color32 color))
                markerImage.color = color;
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
                boundsProvider = FindFirstObjectByType<MapBoundsProvider>();
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

            if (currentStageConfig != null)
                boundsProvider.ApplyStageConfig(currentStageConfig);
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

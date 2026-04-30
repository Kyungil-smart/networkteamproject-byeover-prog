using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class MapSystemPlayerMarkerManager : MonoBehaviour
    {
        [BoxGroup("참조")]
        [SerializeField] private RectTransform miniMapRect;

        [BoxGroup("참조")]
        [SerializeField] private RectTransform worldMapRect;

        [BoxGroup("참조")]
        [SerializeField] private RectTransform miniMapMarkerRoot;

        [BoxGroup("참조")]
        [SerializeField] private RectTransform worldMapMarkerRoot;

        [BoxGroup("참조")]
        [SerializeField] private MapBoundsProvider boundsProvider;

        [BoxGroup("템플릿")]
        [SerializeField] private RectTransform miniMapMarkerTemplate;

        [BoxGroup("템플릿")]
        [SerializeField] private RectTransform worldMapMarkerTemplate;

        [BoxGroup("월드 경계")]
        [SerializeField] private Vector2 worldMin = new(-406.74f, -149.42f);

        [BoxGroup("월드 경계")]
        [SerializeField] private Vector2 worldMax = new(6.08078f, 55.42514f);

        [BoxGroup("월드 경계")]
        [SerializeField] private bool invertY;

        [BoxGroup("마커")]
        [SerializeField] private Vector2 markerSize = new(18f, 18f);

        [BoxGroup("마커")]
        [SerializeField] private float localMarkerScale = 1.3f;

        [BoxGroup("마커")]
        [Tooltip("미니맵은 중앙 고정 로컬 마커와 이동하는 맵 이미지를 사용합니다. 원격 미니맵 마커를 나중에 의도적으로 추가할 때만 켜세요.")]
        [SerializeField] private bool createMinimapMarkers;

        [BoxGroup("마커")]
        [SerializeField] private bool createWorldMapMarkers = true;

        [BoxGroup("디버그")]
        [SerializeField] private bool logMarkerImages = true;

        [BoxGroup("마커")]
        [SerializeField] private Color[] markerColors =
        {
            Color.cyan,
            Color.green,
            Color.yellow,
            Color.red
        };

        private readonly Dictionary<ulong, PlayerMarkerPair> markersByClientId = new();
        private readonly List<ulong> seenClientIds = new();

        private void Awake()
        {
            ResolveBoundsProvider();
            ResolveReferences();
            HideTemplates();
        }

        private void Update()
        {
            SyncPlayerMarkers();
        }

        [Button("참조 찾기")]
        public void ResolveReferences()
        {
            ResolveBoundsProvider();
            miniMapRect ??= FindRectTransform("MiniMapMapImage");
            miniMapRect ??= FindRectTransform("MiniMapFrame");
            miniMapRect ??= FindRectTransform("MiniMapBG");
            worldMapRect ??= FindRectTransform("Png_WorldMap_01");
            miniMapMarkerTemplate ??= FindRectTransform("PlayerMarker_Minimap");
            worldMapMarkerTemplate ??= FindRectTransform("PlayerMarker_WorldMap");

            if (createMinimapMarkers && miniMapRect != null)
                miniMapMarkerRoot ??= GetOrCreateMarkerRoot(miniMapRect, "MarkerRoot_Minimap");
            else
                RemoveGeneratedMiniMapMarkers();

            if (createWorldMapMarkers && worldMapRect != null)
                worldMapMarkerRoot ??= GetOrCreateMarkerRoot(worldMapRect, "MarkerRoot_WorldMap");
        }

        [Button("전체 맵 경계 적용")]
        public void ApplyFullMapBounds()
        {
            worldMin = MapCoordinateUtility.FullMapWorldMin;
            worldMax = MapCoordinateUtility.FullMapWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFullMapBounds();
        }

        [Button("울타리 경계 적용")]
        public void ApplyFenceBounds()
        {
            worldMin = MapCoordinateUtility.FenceWorldMin;
            worldMax = MapCoordinateUtility.FenceWorldMax;
            if (boundsProvider != null)
                boundsProvider.ApplyFenceBounds();
        }

        private void HideTemplates()
        {
            if (createMinimapMarkers && miniMapMarkerTemplate != null)
                miniMapMarkerTemplate.gameObject.SetActive(false);

            if (createWorldMapMarkers && worldMapMarkerTemplate != null)
                worldMapMarkerTemplate.gameObject.SetActive(false);
        }

        private void SyncPlayerMarkers()
        {
            if (!createMinimapMarkers)
                RemoveGeneratedMiniMapMarkers();

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || networkManager.SpawnManager == null)
            {
                RemoveAllMarkers();
                return;
            }

            ResolveReferences();
            seenClientIds.Clear();

            foreach (NetworkObject playerObject in FindSpawnedPlayerObjects(networkManager))
            {
                if (playerObject == null)
                    continue;

                ulong clientId = playerObject.OwnerClientId;
                seenClientIds.Add(clientId);

                if (!markersByClientId.TryGetValue(clientId, out PlayerMarkerPair markerPair))
                {
                    markerPair = CreateMarkerPair(clientId, playerObject.transform, networkManager.LocalClientId);
                    markersByClientId.Add(clientId, markerPair);
                }

                markerPair.SetTarget(playerObject.transform);
            }

            RemoveMissingMarkers();
        }

        private IEnumerable<NetworkObject> FindSpawnedPlayerObjects(NetworkManager networkManager)
        {
            foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null || !networkObject.IsSpawned)
                    continue;

                if (networkObject.CompareTag("Player") ||
                    networkObject.GetComponent<PlayerInputController>() != null ||
                    networkObject.GetComponent<FPSController>() != null)
                {
                    yield return networkObject;
                }
            }
        }

        private PlayerMarkerPair CreateMarkerPair(ulong clientId, Transform target, ulong localClientId)
        {
            bool isLocal = clientId == localClientId;
            int colorIndex = markerColors.Length > 0 ? (int)(clientId % (ulong)markerColors.Length) : 0;
            Color color = markerColors.Length > 0 ? markerColors[colorIndex] : Color.white;
            float scale = isLocal ? localMarkerScale : 1f;

            MapMarkerFollower miniFollower = createMinimapMarkers
                ? CreateMarker(
                    miniMapMarkerRoot,
                    miniMapMarkerTemplate,
                    $"PlayerMarker_Minimap_Client_{clientId}",
                    miniMapRect,
                    target,
                    color,
                    scale)
                : null;

            MapMarkerFollower worldFollower = createWorldMapMarkers
                ? CreateMarker(
                    worldMapMarkerRoot,
                    worldMapMarkerTemplate,
                    $"PlayerMarker_WorldMap_Client_{clientId}",
                    worldMapRect,
                    target,
                    color,
                    scale)
                : null;

            return new PlayerMarkerPair(miniFollower, worldFollower);
        }

        private MapMarkerFollower CreateMarker(
            RectTransform root,
            RectTransform template,
            string markerName,
            RectTransform mapRect,
            Transform target,
            Color color,
            float scale)
        {
            if (root == null || mapRect == null)
                return null;

            GameObject markerObject;
            RectTransform markerRect;

            if (template != null)
            {
                markerObject = Instantiate(template.gameObject, root);
                markerRect = markerObject.GetComponent<RectTransform>();
            }
            else
            {
                markerObject = new GameObject(markerName, typeof(RectTransform), typeof(Image));
                markerRect = markerObject.GetComponent<RectTransform>();
                markerRect.SetParent(root, false);
                markerRect.sizeDelta = markerSize;
            }

            markerObject.name = markerName;
            markerObject.SetActive(true);

            markerRect.anchorMin = new Vector2(0.5f, 0.5f);
            markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.localScale = Vector3.one * scale;

            if (markerRect.sizeDelta == Vector2.zero)
                markerRect.sizeDelta = markerSize;

            Image image = markerObject.GetComponent<Image>();
            if (image == null)
                image = markerObject.AddComponent<Image>();

            Sprite spriteBeforeColor = image.sprite;
            image.color = color;
            image.raycastTarget = false;
            image.sprite = spriteBeforeColor;
            LogMarkerImage(markerObject.name, image);

            MapMarkerFollower follower = markerObject.GetComponent<MapMarkerFollower>();
            if (follower == null)
                follower = markerObject.AddComponent<MapMarkerFollower>();

            GetActiveBounds(out Vector2 activeWorldMin, out Vector2 activeWorldMax);
            GetNormalizedCorrection(out Vector2 normalizedAnchor, out Vector2 normalizedScale, out Vector2 normalizedOffset);
            follower.SetBoundsProvider(boundsProvider, false);
            follower.Setup(mapRect, markerRect, target, activeWorldMin, activeWorldMax);
            follower.SetInvertY(invertY);

            Debug.Log(
                $"[MapSystemPlayerMarkerManager] Created marker={markerName}, mapRect={mapRect.name}, provider={(boundsProvider != null ? boundsProvider.name : "null")}, target={target.name}, playerX={target.position.x}, playerZ={target.position.z}, worldMin={activeWorldMin}, worldMax={activeWorldMax}, normalizedAnchor={normalizedAnchor}, normalizedScale={normalizedScale}, normalizedOffset={normalizedOffset}",
                this);
            return follower;
        }

        private void RemoveMissingMarkers()
        {
            List<ulong> clientIdsToRemove = null;

            foreach (ulong clientId in markersByClientId.Keys)
            {
                if (seenClientIds.Contains(clientId))
                    continue;

                clientIdsToRemove ??= new List<ulong>();
                clientIdsToRemove.Add(clientId);
            }

            if (clientIdsToRemove == null)
                return;

            foreach (ulong clientId in clientIdsToRemove)
                RemoveMarker(clientId);
        }

        private void RemoveAllMarkers()
        {
            if (markersByClientId.Count == 0)
                return;

            List<ulong> clientIds = new(markersByClientId.Keys);
            foreach (ulong clientId in clientIds)
                RemoveMarker(clientId);
        }

        private void RemoveMarker(ulong clientId)
        {
            if (!markersByClientId.TryGetValue(clientId, out PlayerMarkerPair markerPair))
                return;

            markerPair.Destroy();
            markersByClientId.Remove(clientId);
        }

        private RectTransform FindRectTransform(string objectName)
        {
            foreach (RectTransform rectTransform in GetComponentsInChildren<RectTransform>(true))
            {
                if (rectTransform.name == objectName)
                    return rectTransform;
            }

            return null;
        }

        private void RemoveGeneratedMiniMapMarkers()
        {
            RectTransform root = FindRectTransform("MiniMap");
            if (root == null)
                root = miniMapMarkerRoot != null ? miniMapMarkerRoot : FindRectTransform("MarkerRoot_Minimap");

            if (root == null)
                return;

            RectTransform[] children = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = children.Length - 1; i >= 0; i--)
            {
                RectTransform child = children[i];
                if (child == null || child == root || child.name == "LocalPlayerMarker")
                    continue;

                if (!IsGeneratedMiniMapMarkerName(child.name))
                    continue;

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void ResolveBoundsProvider()
        {
            if (boundsProvider != null)
                return;

            boundsProvider = GetComponentInParent<MapBoundsProvider>();

            if (boundsProvider == null)
                boundsProvider = FindObjectOfType<MapBoundsProvider>();
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

        private void GetNormalizedCorrection(out Vector2 normalizedAnchor, out Vector2 normalizedScale, out Vector2 normalizedOffset)
        {
            ResolveBoundsProvider();

            if (boundsProvider != null)
            {
                normalizedAnchor = boundsProvider.NormalizedAnchor;
                normalizedScale = boundsProvider.NormalizedScale;
                normalizedOffset = boundsProvider.NormalizedOffset;
                return;
            }

            normalizedAnchor = new Vector2(0.5f, 0.5f);
            normalizedScale = Vector2.one;
            normalizedOffset = Vector2.zero;
        }

        private static bool IsGeneratedMiniMapMarkerName(string objectName)
        {
            return objectName == "PlayerMarker_Minimap" ||
                   objectName.StartsWith("PlayerMarker_Minimap_Client_") ||
                   objectName.StartsWith("PlayerMarker_Client_") ||
                   objectName.StartsWith("MiniMapMarker_") ||
                   objectName.StartsWith("PlayerMarker_WorldMap_Client_");
        }

        private void LogMarkerImage(string markerName, Image image)
        {
            if (!logMarkerImages || image == null)
                return;

            string spriteName = image.sprite != null ? image.sprite.name : "null";
            Debug.Log($"[MapSystemPlayerMarkerManager] Marker object={markerName}, sprite={spriteName}", this);
        }

        private static RectTransform GetOrCreateMarkerRoot(RectTransform mapRect, string rootName)
        {
            Transform existing = mapRect.Find(rootName);
            if (existing != null)
                return existing as RectTransform;

            GameObject rootObject = new(rootName, typeof(RectTransform));
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.SetParent(mapRect, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            return root;
        }

        private readonly struct PlayerMarkerPair
        {
            private readonly MapMarkerFollower miniMapMarker;
            private readonly MapMarkerFollower worldMapMarker;

            public PlayerMarkerPair(MapMarkerFollower miniMapMarker, MapMarkerFollower worldMapMarker)
            {
                this.miniMapMarker = miniMapMarker;
                this.worldMapMarker = worldMapMarker;
            }

            public void SetTarget(Transform target)
            {
                if (miniMapMarker != null)
                    miniMapMarker.SetTarget(target, false);

                if (worldMapMarker != null)
                    worldMapMarker.SetTarget(target, false);
            }

            public void Destroy()
            {
                if (miniMapMarker != null)
                    Object.Destroy(miniMapMarker.gameObject);

                if (worldMapMarker != null)
                    Object.Destroy(worldMapMarker.gameObject);
            }
        }
    }
}

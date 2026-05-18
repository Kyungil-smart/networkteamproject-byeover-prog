using System;
using System.Collections.Generic;
using System.Reflection;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

using DeadZone.Core;
using DeadZone.InputActions;
using DeadZone.Actors.UI;
using DeadZone.Network;

// 작성자 : 홍정옥
// 기능 : M키 전체 맵 UI 표시/숨김, 퀘스트 클리어에 따른 맵 구역 잠금/해금 표시
// 사용 위치 : MapSystem 루트 오브젝트
namespace DeadZone.Actors
{
    /// <summary>
    /// M키로 WorldMapUI를 열고 닫으며, Area 하위 Lock_ 구역들의 Dim/Icon을 제어한다.
    /// 구역 이름 텍스트는 잠금/해금 여부와 관계없이 계속 표시한다.
    /// </summary>
    public class WorldMapController : MonoBehaviour
    {
        [BoxGroup("참조")]
        [Required, Tooltip("M키로 켜고 끌 전체 맵 UI")]
        [SerializeField] private GameObject worldMapUI;

        [BoxGroup("참조")]
        [Tooltip("잠금 구역 오브젝트들이 들어있는 부모")]
        [SerializeField] private Transform areaRoot;

        [BoxGroup("Map")]
        [Tooltip("World map background RawImage RectTransform. Area overlay is aligned to this rect.")]
        [SerializeField] private RectTransform mapImageRect;

        [BoxGroup("Map")]
        [Tooltip("Hide this minimap root while the world map is open.")]
        [SerializeField] private GameObject minimapRoot;

        [BoxGroup("참조")]
        [SerializeField] private RectTransform playerMarkerRoot;

        [BoxGroup("참조")]
        [SerializeField] private RectTransform playerMarkerMapRect;

        [BoxGroup("참조")]
        [SerializeField] private MonoBehaviour playerMarkerPrefab;

        [BoxGroup("입력")]
        [Tooltip("전체 맵을 열고 닫는 입력 시스템 액션")]
        [SerializeField] private InputActionReference toggleMapAction;

        [BoxGroup("입력")]
        [Tooltip("전체 맵이 열려 있을 때 닫는 입력 시스템 액션")]
        [SerializeField] private InputActionReference closeMapAction;
        
        [BoxGroup("입력")]
        [Tooltip("맵 닫기 입력으로 전체 맵을 닫을지 여부")]
        [SerializeField] private bool closeWithEscape = true;

        [BoxGroup("상태")]
        [SerializeField] private bool hideMapOnAwake = true;

        [BoxGroup("상태")]
        [Tooltip("맵 시스템 루트 스케일이 0으로 저장된 경우 런타임에서 1로 복구")]
        [SerializeField] private bool forceRootScaleOne = true;

        [BoxGroup("Map")]
        [Tooltip("Keep Area lock overlay on the same RectTransform basis as the RenderTexture RawImage.")]
        [SerializeField] private bool alignAreaRootToMapRect = true;
        
        [BoxGroup("플레이어 마커")]
        [Tooltip("RenderTexture 안의 월드 스프라이트 마커를 쓰면 false. 기존 UI 마커 방식을 쓰면 true.")]
        [SerializeField] private bool useUiPlayerMarkers;

        [FoldoutGroup("피드백")]
        [Tooltip("맵을 열 때 재생할 피드백")]
        [SerializeField] private MMF_Player onOpenFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("맵을 닫을 때 재생할 피드백")]
        [SerializeField] private MMF_Player onCloseFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("구역이 해금될 때 재생할 피드백")]
        [SerializeField] private MMF_Player onAreaUnlockedFeedback;

        [FoldoutGroup("구역 잠금")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true)]
        [SerializeField] private List<MapAreaLock> areaLocks = new();

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool isOpen;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private string lastUnlockedAreaId;

        private DeadZoneInputActions fallbackInputActions;
        private InputAction activeToggleMapAction;
        private InputAction activeCloseMapAction;
        private readonly Dictionary<ulong, MonoBehaviour> playerMarkersByClientId = new();
        private const string DefaultMapImageRectName = "Png_WorldMap_01";
        private const string DefaultMinimapRootName = "Minimap";
        private bool minimapWasActiveBeforeOpen;

        private void Awake()
        {
            if (forceRootScaleOne)
                transform.localScale = Vector3.one;

            AlignAreaRootToMapRect();

            if (hideMapOnAwake && worldMapUI != null)
                worldMapUI.SetActive(false);

            isOpen = worldMapUI != null && worldMapUI.activeSelf;
        }

        // 퀘스트 완료 이벤트 구독
        private void OnEnable()
        {
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);

            activeToggleMapAction = ResolveToggleMapAction();
            if (activeToggleMapAction != null)
            {
                activeToggleMapAction.performed += OnToggleMapInput;
                activeToggleMapAction.Enable();
            }

            activeCloseMapAction = ResolveCloseMapAction();
            if (activeCloseMapAction != null)
            {
                activeCloseMapAction.performed += OnCloseMapInput;
                activeCloseMapAction.Enable();
            }
        }

        private void Start()
        {
            AlignAreaRootToMapRect();
            RefreshAllAreaLocks();
        }

        // 오브젝트가 비활성화될 때 퀘스트 완료 이벤트 구독을 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Map, false);
            CursorStateController.PopUiOwner(this);
            RestoreMinimapVisibility();
            if (useUiPlayerMarkers)
                ClearPlayerMarkers();

            if (activeToggleMapAction != null)
            {
                activeToggleMapAction.performed -= OnToggleMapInput;
                activeToggleMapAction.Disable();
                activeToggleMapAction = null;
            }

            if (activeCloseMapAction != null)
            {
                activeCloseMapAction.performed -= OnCloseMapInput;
                activeCloseMapAction.Disable();
                activeCloseMapAction = null;
            }

            if (fallbackInputActions != null)
            {
                fallbackInputActions.Dispose();
                fallbackInputActions = null;
            }
        }

        private void Update()
        {
            if (isOpen && useUiPlayerMarkers)
                RefreshPlayerMarkers();
        }

        private void OnToggleMapInput(InputAction.CallbackContext context)
        {
            Debug.Log("[WorldMapController] Toggle map input performed", this);
            ToggleMap();
        }

        private void OnCloseMapInput(InputAction.CallbackContext context)
        {
            if (activeToggleMapAction != null && context.action == activeToggleMapAction)
                return;

            if (closeWithEscape && isOpen)
                CloseMap();
        }

        // 퀘스트 완료 이벤트를 받았을 때 실행
        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            string completedQuestId = e.questId.ToString();
            string unlockZoneId = e.unlockZoneId.ToString();
            Debug.Log($"[WorldMapController] QuestCompleted questId={completedQuestId}", this);
            UnlockAreasByQuestId(completedQuestId, playFeedback: true);

            if (!string.IsNullOrWhiteSpace(unlockZoneId) && FindArea(unlockZoneId) != null)
                UnlockArea(unlockZoneId, playFeedback: true);
        }

        // 전체 맵 토글
        public void ToggleMap()
        {
            bool currentlyOpen = worldMapUI != null ? worldMapUI.activeSelf : isOpen;

            if (currentlyOpen) CloseMap();
            else OpenMap();
        }
        
        // 전체 맵을 여는 함수
        public void OpenMap()
        {
            Debug.Log("[WorldMapController] OpenMap", this);
            isOpen = true;

            if (worldMapUI != null)
                worldMapUI.SetActive(true);

            HideMinimapWhileWorldMapOpen();
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Map, true);
            CursorStateController.PushUiOwner(this);
            AlignAreaRootToMapRect();
            RefreshAllAreaLocks();
            if (useUiPlayerMarkers)
                RefreshPlayerMarkers();
            UIFeedbackTester.Play(onOpenFeedback, this, "전체 맵 열기");
        }
        
        // 전체 맵을 닫는 함수
        public void CloseMap()
        {
            Debug.Log("[WorldMapController] CloseMap", this);
            isOpen = false;

            if (worldMapUI != null)
                worldMapUI.SetActive(false);

            if (useUiPlayerMarkers)
                ClearPlayerMarkers();
            RestoreMinimapVisibility();
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Map, false);
            CursorStateController.PopUiOwner(this);
            UIFeedbackTester.Play(onCloseFeedback, this, "전체 맵 닫기");
        }
        
        private InputAction ResolveToggleMapAction()
        {
            if (toggleMapAction == null)
            {
                Debug.Log("[WorldMapController] toggleMapAction is not assigned. Falling back to DeadZoneInputActions Player/Map.", this);
                return ResolveFallbackMapAction();
            }

            if (toggleMapAction.action == null)
            {
                Debug.LogWarning("[WorldMapController] toggleMapAction.action is null. Falling back to DeadZoneInputActions Player/Map.", this);
                return ResolveFallbackMapAction();
            }

            return toggleMapAction.action;
        }

        private InputAction ResolveCloseMapAction()
        {
            if (closeMapAction == null)
            {
                Debug.Log("[WorldMapController] closeMapAction is not assigned. The map can still be closed by pressing the toggle map input again.", this);
                return null;
            }

            if (closeMapAction.action == null)
            {
                Debug.LogWarning("[WorldMapController] closeMapAction.action is null. The map can still be closed by pressing the toggle map input again.", this);
                return null;
            }

            if (toggleMapAction != null && toggleMapAction.action == closeMapAction.action)
                return null;

            return closeMapAction.action;
        }

        private InputAction ResolveFallbackMapAction()
        {
            fallbackInputActions ??= new DeadZoneInputActions();
            return fallbackInputActions.Player.Map;
        }

        // 등록된 모든 구역의 잠금 상태를 다시 적용
        public void RefreshAllAreaLocks()
        {
            HashSet<string> unlockedAreaIds = GetCloudUnlockedAreaIds();
            HashSet<string> completedQuestIds = GetCloudCompletedQuestIds();

            foreach (MapAreaLock areaLock in areaLocks)
            {
                if (areaLock == null)
                    continue;

                if (IsAreaUnlockedByCloud(areaLock, unlockedAreaIds, completedQuestIds))
                    areaLock.SetUnlocked(true);
                else
                    areaLock.Refresh();
            }
        }

        private static bool IsAreaUnlockedByCloud(
            MapAreaLock areaLock,
            HashSet<string> unlockedAreaIds,
            HashSet<string> completedQuestIds)
        {
            if (areaLock == null)
                return false;

            if (unlockedAreaIds != null && unlockedAreaIds.Contains(areaLock.AreaId))
                return true;

            if (completedQuestIds == null || completedQuestIds.Count == 0)
                return false;

            foreach (string questId in completedQuestIds)
            {
                if (areaLock.HasRequiredQuest(questId))
                    return true;
            }

            return false;
        }

        private static HashSet<string> GetCloudUnlockedAreaIds()
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            List<string> unlockedZones = cloudSaveSystem?.CurrentData?.progress?.unlockedZones;
            if (unlockedZones == null)
                return ids;

            for (int i = 0; i < unlockedZones.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(unlockedZones[i]))
                    ids.Add(unlockedZones[i]);
            }

            return ids;
        }

        private static HashSet<string> GetCloudCompletedQuestIds()
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem();
            List<string> completedQuestIds = cloudSaveSystem?.CurrentData?.progress?.personalCompletedQuestIds;
            if (completedQuestIds == null)
                return ids;

            for (int i = 0; i < completedQuestIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(completedQuestIds[i]))
                    ids.Add(completedQuestIds[i]);
            }

            return ids;
        }

        private static CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            return cloudSaveSystem != null
                ? cloudSaveSystem
                : FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);
        }

        private void RefreshPlayerMarkers()
        {
            RectTransform markerRoot = ResolvePlayerMarkerRoot();
            RectTransform mapRect = ResolvePlayerMarkerMapRect(markerRoot);
            if (markerRoot == null || mapRect == null)
                return;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.SpawnManager == null)
            {
                ClearPlayerMarkers();
                return;
            }

            HashSet<ulong> activeClientIds = new();
            foreach (NetworkObject netObj in nm.SpawnManager.SpawnedObjectsList)
            {
                if (netObj == null || !netObj.IsPlayerObject)
                    continue;

                activeClientIds.Add(netObj.OwnerClientId);

                if (!playerMarkersByClientId.TryGetValue(netObj.OwnerClientId, out MonoBehaviour marker) || marker == null)
                {
                    marker = CreatePlayerMarker(markerRoot);
                    playerMarkersByClientId[netObj.OwnerClientId] = marker;
                }

                BindPlayerMarker(marker, netObj.transform, mapRect);
            }

            RemoveInactivePlayerMarkers(activeClientIds);
        }

        private RectTransform ResolvePlayerMarkerRoot()
        {
            if (playerMarkerRoot != null)
                return playerMarkerRoot;

            RectTransform mapRect = ResolveMapImageRect();
            if (mapRect != null)
                return mapRect;

            return worldMapUI != null ? worldMapUI.GetComponent<RectTransform>() : null;
        }

        private RectTransform ResolvePlayerMarkerMapRect(RectTransform fallback)
        {
            if (playerMarkerMapRect != null)
                return playerMarkerMapRect;

            RectTransform mapRect = ResolveMapImageRect();
            return mapRect != null ? mapRect : fallback;
        }

        private RectTransform ResolveMapImageRect()
        {
            if (mapImageRect != null)
                return mapImageRect;

            return ResolveNamedRectTransform(DefaultMapImageRectName);
        }

        private GameObject ResolveMinimapRoot()
        {
            if (minimapRoot != null)
                return minimapRoot;

            RectTransform minimapRect = ResolveNamedRectTransform(DefaultMinimapRootName);
            return minimapRect != null ? minimapRect.gameObject : null;
        }

        private void HideMinimapWhileWorldMapOpen()
        {
            GameObject root = ResolveMinimapRoot();
            if (root == null)
                return;

            minimapWasActiveBeforeOpen = root.activeSelf;
            root.SetActive(false);
        }

        private void RestoreMinimapVisibility()
        {
            GameObject root = ResolveMinimapRoot();
            if (root == null)
                return;

            root.SetActive(minimapWasActiveBeforeOpen);
        }

        private void AlignAreaRootToMapRect()
        {
            if (!alignAreaRootToMapRect)
                return;

            RectTransform areaRect = areaRoot as RectTransform;
            RectTransform mapRect = ResolveMapImageRect();
            if (areaRect == null || mapRect == null)
                return;

            if (areaRect.parent != mapRect.parent)
            {
                Debug.LogWarning("[WorldMapController] Area and map image do not share the same parent RectTransform.", this);
                return;
            }

            areaRect.anchorMin = mapRect.anchorMin;
            areaRect.anchorMax = mapRect.anchorMax;
            areaRect.pivot = mapRect.pivot;
            areaRect.anchoredPosition = mapRect.anchoredPosition;
            areaRect.sizeDelta = mapRect.sizeDelta;
            areaRect.localRotation = Quaternion.identity;
            areaRect.localScale = Vector3.one;

            if (areaRect.GetSiblingIndex() <= mapRect.GetSiblingIndex())
                areaRect.SetSiblingIndex(mapRect.GetSiblingIndex() + 1);
        }

        private RectTransform ResolveNamedRectTransform(string targetName)
        {
            if (worldMapUI == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            RectTransform[] rects = worldMapUI.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] != null && rects[i].name == targetName)
                    return rects[i];
            }

            return null;
        }

        private MonoBehaviour CreatePlayerMarker(RectTransform markerRoot)
        {
            MonoBehaviour marker;
            if (playerMarkerPrefab != null)
            {
                marker = Instantiate(playerMarkerPrefab, markerRoot);
            }
            else
            {
                GameObject markerObject = new GameObject(
                    "PlayerMarker_WorldMap_Runtime",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));

                markerObject.transform.SetParent(markerRoot, false);
                RectTransform markerRect = markerObject.GetComponent<RectTransform>();
                markerRect.sizeDelta = new Vector2(24f, 24f);

                Image image = markerObject.GetComponent<Image>();
                image.raycastTarget = false;

                Type markerType = Type.GetType("DeadZone.Actors.MapMarkerFollower, Assembly-CSharp");
                marker = markerType != null ? markerObject.AddComponent(markerType) as MonoBehaviour : null;
            }

            return marker;
        }

        private void BindPlayerMarker(MonoBehaviour marker, Transform target, RectTransform mapRect)
        {
            if (marker == null)
                return;

            MethodInfo bindMethod = marker.GetType().GetMethod(
                "Bind",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(Transform), typeof(RectTransform) },
                modifiers: null);

            bindMethod?.Invoke(marker, new object[] { target, mapRect });
        }

        private void RemoveInactivePlayerMarkers(HashSet<ulong> activeClientIds)
        {
            List<ulong> removeBuffer = new();
            foreach (KeyValuePair<ulong, MonoBehaviour> pair in playerMarkersByClientId)
            {
                if (!activeClientIds.Contains(pair.Key))
                    removeBuffer.Add(pair.Key);
            }

            for (int i = 0; i < removeBuffer.Count; i++)
            {
                ulong clientId = removeBuffer[i];
                if (playerMarkersByClientId.TryGetValue(clientId, out MonoBehaviour marker) && marker != null)
                    Destroy(marker.gameObject);

                playerMarkersByClientId.Remove(clientId);
            }
        }

        private void ClearPlayerMarkers()
        {
            foreach (MonoBehaviour marker in playerMarkersByClientId.Values)
            {
                if (marker != null)
                    Destroy(marker.gameObject);
            }

            playerMarkersByClientId.Clear();
        }
        
        // 특정 구역 ID를 직접 해금하는 함수
        public void UnlockArea(string areaId, bool playFeedback = true)
        {
            MapAreaLock target = FindArea(areaId);
            if (target == null)
            {
                Debug.LogWarning($"[WorldMapController] 해금할 구역을 찾지 못했습니다. areaId={areaId}", this);
                return;
            }

            target.SetUnlocked(true);
            lastUnlockedAreaId = target.AreaId;

            Debug.Log($"[WorldMapController] Area unlocked: {target.AreaId}", this);

            if (playFeedback)
                UIFeedbackTester.Play(onAreaUnlockedFeedback, this, $"맵 구역 해금 {target.AreaId}");
        }
        
        // 특정 구역 ID를 다시 잠그는 함수
        public void LockArea(string areaId)
        {
            MapAreaLock target = FindArea(areaId);
            if (target == null)
            {
                Debug.LogWarning($"[WorldMapController] 잠글 구역을 찾지 못했습니다. areaId={areaId}", this);
                return;
            }

            target.SetUnlocked(false);
            Debug.Log($"[WorldMapController] Area locked: {target.AreaId}", this);
        }
        
        // 완료된 퀘스트 ID와 연결된 구역을 찾아서 해금
        private void UnlockAreasByQuestId(string completedQuestId, bool playFeedback)
        {
            bool found = false;

            foreach (MapAreaLock areaLock in areaLocks)
            {
                if (!areaLock.HasRequiredQuest(completedQuestId))
                    continue;

                areaLock.SetUnlocked(true);
                lastUnlockedAreaId = areaLock.AreaId;
                found = true;

                Debug.Log($"[WorldMapController] Quest unlock matched. questId={completedQuestId}, areaId={areaLock.AreaId}", this);
            }

            if (found && playFeedback)
                UIFeedbackTester.Play(onAreaUnlockedFeedback, this, $"퀘스트 구역 해금 {completedQuestId}");
        }

        // areaLocks 리스트에서 특정 areaId를 가진 구역을 찾음
        private MapAreaLock FindArea(string areaId)
        {
            if (string.IsNullOrWhiteSpace(areaId))
                return null;

            foreach (MapAreaLock areaLock in areaLocks)
            {
                if (string.Equals(areaLock.AreaId, areaId, StringComparison.OrdinalIgnoreCase))
                    return areaLock;
            }

            return null;
        }

#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("맵 열기"), GUIColor(0.65f, 0.9f, 1f)]
        private void TestOpenMap()
        {
            if (!Application.isPlaying)
            {
                if (worldMapUI != null) worldMapUI.SetActive(true);
                isOpen = true;
                RefreshAllAreaLocks();
                return;
            }

            OpenMap();
        }

        [TitleGroup("디버그")]
        [Button("맵 닫기"), GUIColor(0.8f, 0.8f, 0.8f)]
        private void TestCloseMap()
        {
            if (!Application.isPlaying)
            {
                if (worldMapUI != null) worldMapUI.SetActive(false);
                isOpen = false;
                return;
            }

            CloseMap();
        }

        [TitleGroup("디버그")]
        [Button("잠금 상태 새로고침")]
        private void TestRefresh() => RefreshAllAreaLocks();

        [TitleGroup("디버그")]
        [Button("모든 구역 잠금"), GUIColor(1f, 0.7f, 0.7f)]
        private void TestLockAll()
        {
            foreach (MapAreaLock areaLock in areaLocks)
                areaLock.SetUnlocked(false);
        }

        [TitleGroup("디버그")]
        [Button("모든 구역 해금"), GUIColor(0.65f, 1f, 0.65f)]
        private void TestUnlockAll()
        {
            foreach (MapAreaLock areaLock in areaLocks)
                areaLock.SetUnlocked(true);
        }

        [TitleGroup("디버그")]
        [Button("구역 하위 잠금 오브젝트 자동 수집"), GUIColor(0.7f, 0.85f, 1f)]
        private void AutoCollectAreaLocks()
        {
            if (areaRoot == null)
            {
                Debug.LogWarning("[WorldMapController] areaRoot가 비어 있습니다. WorldMapUI/Area 오브젝트를 넣어주세요.", this);
                return;
            }

            areaLocks.Clear();

            foreach (Transform child in areaRoot)
            {
                if (!child.name.StartsWith("Lock_", StringComparison.OrdinalIgnoreCase))
                    continue;

                MapAreaLock areaLock = new MapAreaLock();
                areaLock.ConfigureFromHierarchy(child);
                areaLocks.Add(areaLock);
            }

            Debug.Log($"[WorldMapController] 자동 수집 완료. count={areaLocks.Count}", this);
            RefreshAllAreaLocks();
        }

        [TitleGroup("디버그")]
        [Button("잠금 화면 레이캐스트 대상 끄기")]
        private void DisableRaycastTargetsInLockUI()
        {
            int count = 0;

            foreach (MapAreaLock areaLock in areaLocks)
                count += areaLock.DisableRaycastTargets();

            Debug.Log($"[WorldMapController] Raycast Target Off count={count}", this);
        }
#endif
    }

    [Serializable]
    public class MapAreaLock
    {
        [HorizontalGroup("Header", Width = 140)]
        [SerializeField] private string areaId;

        [HorizontalGroup("Header")]
        [SerializeField] private bool unlocked;

        [BoxGroup("퀘스트")]
        [Tooltip("이 퀘스트가 완료되면 해당 구역이 해금")]
        [SerializeField] private string requiredQuestId;

        [BoxGroup("참조")]
        [Tooltip("구역 전체 루트")]
        [SerializeField] private GameObject areaRoot;

        [BoxGroup("참조")]
        [Tooltip("잠금 상태일 때만 켜질 오브젝트들")]
        [SerializeField] private GameObject[] lockOnlyObjects;

        [BoxGroup("참조")]
        [Tooltip("잠금/해금과 관계없이 계속 보일 오브젝트들")]
        [SerializeField] private GameObject[] alwaysVisibleObjects;

        [BoxGroup("참조")]
        [Tooltip("나중에 실제 월드 진입을 막을 충돌체 또는 게이트 오브젝트")]
        [SerializeField] private GameObject[] worldBlockerObjects;

        [ShowInInspector, ReadOnly, BoxGroup("디버그")]
        public string AreaId => areaId;

        [ShowInInspector, ReadOnly, BoxGroup("디버그")]
        public bool IsUnlocked => unlocked;

        public bool HasRequiredQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(requiredQuestId))
                return false;

            return string.Equals(requiredQuestId, questId, StringComparison.OrdinalIgnoreCase);
        }

        public void SetUnlocked(bool value)
        {
            unlocked = value;
            Refresh();
        }

        public void Refresh()
        {
            if (areaRoot != null && !areaRoot.activeSelf)
                areaRoot.SetActive(true);

            bool showLock = !unlocked;

            SetObjectsActive(lockOnlyObjects, showLock);
            SetObjectsActive(alwaysVisibleObjects, true);
            SetObjectsActive(worldBlockerObjects, showLock);
        }

        private void SetObjectsActive(GameObject[] objects, bool active)
        {
            if (objects == null)
                return;

            foreach (GameObject obj in objects)
            {
                if (obj != null)
                    obj.SetActive(active);
            }
        }

#if UNITY_EDITOR
        [BoxGroup("디버그")]
        [Button("이 구역 잠금"), GUIColor(1f, 0.7f, 0.7f)]
        private void TestLock() => SetUnlocked(false);

        [BoxGroup("디버그")]
        [Button("이 구역 해금"), GUIColor(0.65f, 1f, 0.65f)]
        private void TestUnlock() => SetUnlocked(true);
#endif

        public void ConfigureFromHierarchy(Transform root)
        {
            areaRoot = root.gameObject;
            areaId = root.name.StartsWith("Lock_", StringComparison.OrdinalIgnoreCase)
                ? root.name.Substring("Lock_".Length)
                : root.name;

            unlocked = false;

            List<GameObject> lockObjects = new();
            List<GameObject> labelObjects = new();

            foreach (Transform child in root)
            {
                string childName = child.name;

                if (childName.StartsWith("Dim", StringComparison.OrdinalIgnoreCase) ||
                    childName.StartsWith("Icon_Lock", StringComparison.OrdinalIgnoreCase) ||
                    childName.Contains("Lock", StringComparison.OrdinalIgnoreCase))
                {
                    lockObjects.Add(child.gameObject);
                    continue;
                }

                if (childName.StartsWith("Text", StringComparison.OrdinalIgnoreCase))
                {
                    labelObjects.Add(child.gameObject);
                    continue;
                }
            }

            lockOnlyObjects = lockObjects.ToArray();
            alwaysVisibleObjects = labelObjects.ToArray();
            worldBlockerObjects = Array.Empty<GameObject>();
        }

        public int DisableRaycastTargets()
        {
            int count = 0;
            count += DisableRaycastTargets(areaRoot);
            count += DisableRaycastTargets(lockOnlyObjects);
            count += DisableRaycastTargets(alwaysVisibleObjects);
            return count;
        }

        private int DisableRaycastTargets(GameObject obj)
        {
            if (obj == null)
                return 0;

            int count = 0;
            foreach (Graphic graphic in obj.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.raycastTarget)
                {
                    graphic.raycastTarget = false;
                    count++;
                }
            }

            return count;
        }

        private int DisableRaycastTargets(GameObject[] objects)
        {
            if (objects == null)
                return 0;

            int count = 0;
            foreach (GameObject obj in objects)
                count += DisableRaycastTargets(obj);

            return count;
        }
    }
}

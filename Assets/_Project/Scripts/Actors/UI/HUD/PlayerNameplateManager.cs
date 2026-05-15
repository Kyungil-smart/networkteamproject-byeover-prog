using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors.Player;
using DeadZone.Core;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 모든 PlayerObject의 머리 위 위치를 로컬 카메라 기준 화면 좌표로 변환해
    /// HUD Canvas 위에 이름표를 표시합니다.
    /// 쿼터뷰 카메라에서도 글자가 기울어지지 않게 하기 위한 Screen Space 방식입니다.
    /// </summary>
    public sealed class PlayerNameplateManager : MonoBehaviour
    {
        [Header("==== HUD 참조 ====")]
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private RectTransform nameplateRoot;
        [SerializeField] private PlayerNameplateUI nameplatePrefab;

        [Header("==== 카메라 ====")]
        [SerializeField] private Camera targetCamera;

        [Header("==== 표시 규칙 ====")]
        [SerializeField] private bool hideLocalOwnerNameplate = true;
        [SerializeField] private float fallbackHeightOffset = 2.15f;
        [SerializeField] private float maxVisibleDistance = 60f;
        [SerializeField] private Vector2 screenPadding = new Vector2(40f, 40f);
        [SerializeField] private float rebuildInterval = 0.25f;

        private readonly Dictionary<ulong, NameplateEntry> entriesByNetworkObjectId = new();
        private readonly HashSet<ulong> activeNetworkObjectIds = new();
        private readonly List<ulong> removeBuffer = new();

        private RectTransform canvasRect;
        private float nextRebuildTime;

        private void OnEnable()
        {
            ResolveCanvasReferences();

            EventBus.Subscribe<OwnerPlayerCameraRegisteredEvent>(OnOwnerCameraRegistered);
            EventBus.Subscribe<OwnerPlayerCameraUnregisteredEvent>(OnOwnerCameraUnregistered);

            RebuildNameplates();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OwnerPlayerCameraRegisteredEvent>(OnOwnerCameraRegistered);
            EventBus.Unsubscribe<OwnerPlayerCameraUnregisteredEvent>(OnOwnerCameraUnregistered);

            ClearNameplates();
        }

        private void LateUpdate()
        {
            ResolveCanvasReferences();
            ResolveCameraReference();

            if (Time.unscaledTime >= nextRebuildTime)
            {
                nextRebuildTime = Time.unscaledTime + rebuildInterval;
                RebuildNameplates();
            }

            UpdateNameplatePositions();
        }

        private void OnOwnerCameraRegistered(OwnerPlayerCameraRegisteredEvent e)
        {
            targetCamera = e.playerCamera;
        }

        private void OnOwnerCameraUnregistered(OwnerPlayerCameraUnregisteredEvent e)
        {
            if (targetCamera == e.playerCamera)
                targetCamera = null;
        }

        private void RebuildNameplates()
        {
            if (nameplatePrefab == null || nameplateRoot == null)
                return;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || networkManager.SpawnManager == null)
            {
                ClearNameplates();
                return;
            }

            activeNetworkObjectIds.Clear();

            foreach (NetworkObject netObj in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (!TryCreateEntrySource(netObj, out PlayerDisplayNameIdentity displayNameIdentity))
                    continue;

                if (hideLocalOwnerNameplate && netObj.IsOwner)
                    continue;

                ulong networkObjectId = netObj.NetworkObjectId;
                activeNetworkObjectIds.Add(networkObjectId);

                if (entriesByNetworkObjectId.ContainsKey(networkObjectId))
                    continue;

                PlayerNameplateUI ui = Instantiate(nameplatePrefab, nameplateRoot);
                PlayerTeamIdentity teamIdentity = netObj.GetComponent<PlayerTeamIdentity>()
                                                  ?? netObj.GetComponentInChildren<PlayerTeamIdentity>(true);

                ui.Bind(displayNameIdentity, teamIdentity);

                entriesByNetworkObjectId[networkObjectId] = new NameplateEntry
                {
                    NetworkObject = netObj,
                    Anchor = ResolveAnchor(netObj.transform),
                    UI = ui
                };
            }

            RemoveInactiveEntries();
        }


        private bool TryCreateEntrySource(NetworkObject netObj, out PlayerDisplayNameIdentity displayNameIdentity)
        {
            displayNameIdentity = null;

            if (netObj == null || !netObj.IsSpawned)
                return false;

            displayNameIdentity = netObj.GetComponent<PlayerDisplayNameIdentity>()
                ?? netObj.GetComponentInChildren<PlayerDisplayNameIdentity>(true);

            return displayNameIdentity != null;
        }

        private void UpdateNameplatePositions()
        {
            if (targetCamera == null || canvasRect == null)
            {
                SetAllVisible(false);
                return;
            }

            foreach (NameplateEntry entry in entriesByNetworkObjectId.Values)
            {
                UpdateEntry(entry);
            }
        }

        private void UpdateEntry(NameplateEntry entry)
        {
            if (entry == null || entry.NetworkObject == null || entry.UI == null)
                return;

            Transform targetTransform = entry.Anchor != null
                ? entry.Anchor
                : entry.NetworkObject.transform;

            Vector3 worldPosition = entry.Anchor != null
                ? targetTransform.position
                : targetTransform.position + Vector3.up * fallbackHeightOffset;

            Vector3 screenPosition = targetCamera.WorldToScreenPoint(worldPosition);

            if (!IsScreenPositionVisible(screenPosition, worldPosition))
            {
                entry.UI.SetVisible(false);
                return;
            }

            Camera eventCamera = ResolveCanvasEventCamera();

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPosition,
                    eventCamera,
                    out Vector2 localPoint))
            {
                entry.UI.SetVisible(false);
                return;
            }

            entry.UI.SetAnchoredPosition(localPoint);
            entry.UI.SetVisible(true);
        }

        private bool IsScreenPositionVisible(Vector3 screenPosition, Vector3 worldPosition)
        {
            if (screenPosition.z <= 0f)
                return false;

            if (maxVisibleDistance > 0f)
            {
                float distance = Vector3.Distance(targetCamera.transform.position, worldPosition);
                if (distance > maxVisibleDistance)
                    return false;
            }

            if (screenPosition.x < -screenPadding.x || screenPosition.x > Screen.width + screenPadding.x)
                return false;

            if (screenPosition.y < -screenPadding.y || screenPosition.y > Screen.height + screenPadding.y)
                return false;

            return true;
        }

        private Transform ResolveAnchor(Transform playerRoot)
        {
            if (playerRoot == null)
                return null;

            Transform direct = playerRoot.Find("NameplateAnchor");
            if (direct != null)
                return direct;

            Transform[] children = playerRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == "NameplateAnchor")
                    return children[i];
            }

            return null;
        }

        private void RemoveInactiveEntries()
        {
            removeBuffer.Clear();

            foreach (ulong networkObjectId in entriesByNetworkObjectId.Keys)
            {
                if (!activeNetworkObjectIds.Contains(networkObjectId))
                    removeBuffer.Add(networkObjectId);
            }

            for (int i = 0; i < removeBuffer.Count; i++)
            {
                ulong networkObjectId = removeBuffer[i];

                if (!entriesByNetworkObjectId.TryGetValue(networkObjectId, out NameplateEntry entry))
                    continue;

                if (entry.UI != null)
                    Destroy(entry.UI.gameObject);

                entriesByNetworkObjectId.Remove(networkObjectId);
            }
        }

        private void SetAllVisible(bool visible)
        {
            foreach (NameplateEntry entry in entriesByNetworkObjectId.Values)
            {
                if (entry != null && entry.UI != null)
                    entry.UI.SetVisible(visible);
            }
        }

        private void ClearNameplates()
        {
            foreach (NameplateEntry entry in entriesByNetworkObjectId.Values)
            {
                if (entry != null && entry.UI != null)
                    Destroy(entry.UI.gameObject);
            }

            entriesByNetworkObjectId.Clear();
            activeNetworkObjectIds.Clear();
            removeBuffer.Clear();
        }

        private void ResolveCanvasReferences()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponentInParent<Canvas>();

            if (targetCanvas != null)
                targetCanvas = targetCanvas.rootCanvas;

            if (targetCanvas != null)
                canvasRect = targetCanvas.transform as RectTransform;

            if (nameplateRoot == null && canvasRect != null)
                nameplateRoot = canvasRect;
        }

        private void ResolveCameraReference()
        {
            if (targetCamera != null && targetCamera.enabled)
                return;

            if (Camera.main != null)
                targetCamera = Camera.main;
        }

        private Camera ResolveCanvasEventCamera()
        {
            if (targetCanvas == null || targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return targetCanvas.worldCamera != null
                ? targetCanvas.worldCamera
                : targetCamera;
        }

        private sealed class NameplateEntry
        {
            public NetworkObject NetworkObject;
            public Transform Anchor;
            public PlayerNameplateUI UI;
        }
    }
}

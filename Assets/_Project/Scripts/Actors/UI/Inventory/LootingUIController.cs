using System.Collections.Generic;
using DeadZone.Actors;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class LootingUIController : MonoBehaviour
    {
        public static LootingUIController ActiveInstance { get; private set; }

        [BoxGroup("UI 연결")]
        [SerializeField] private InventoryUI inventoryUI;

        [BoxGroup("UI 연결")]
        [Tooltip("InventoryVisibleRoot입니다. 비워두면 이름으로 자동 탐색합니다.")]
        [SerializeField] private RectTransform inventoryVisibleRoot;

        [BoxGroup("UI 연결")]
        [Tooltip("직접 지정한 이동 대상입니다. 비워두면 InventoryVisibleRoot 하위에서 Dim/LootingPanel/Tooltip을 제외한 자식들을 모두 이동합니다.")]
        [SerializeField] private List<RectTransform> inventoryMoveTargets = new();

        [BoxGroup("UI 연결")]
        [Tooltip("비어 있는 Inventory Move Targets를 자동으로 채웁니다. UI 구조가 확실할 때만 켜세요.")]
        [SerializeField] private bool autoCollectMoveTargets;

        [BoxGroup("UI 연결")]
        [Tooltip("파밍용 패널 오브젝트입니다. 이름은 LootingPanel을 사용하세요.")]
        [SerializeField] private GameObject lootingPanel;

        [BoxGroup("UI 연결")]
        [SerializeField] private ContainerGridView containerGridView;

        [BoxGroup("패널 이동")]
        [SerializeField] private Vector2 lootingInventoryOffset = new(-420f, 0f);

        [BoxGroup("로컬 테스트")]
        [SerializeField] private LootContainer testContainer;

        private readonly List<RectTransform> activeMoveTargets = new();
        private readonly List<Vector2> originalMoveTargetPositions = new();
        private LootContainer currentContainer;
        private bool hasOriginalPositionCache;

        public bool IsOpen => lootingPanel != null && lootingPanel.activeSelf;

        private void Awake()
        {
            ActiveInstance = this;
            ResolveReferences();
            CacheOriginalPositions();
            CloseLootingPanelOnly();
        }

        private void OnEnable()
        {
            ActiveInstance = this;
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;
        }

        private void Update()
        {
            if (IsOpen && inventoryUI != null && !inventoryUI.IsOpen)
                Close(closeInventory: false);
        }

        public void Open(LootContainer container)
        {
            ResolveReferences();
            CacheOriginalPositions();

            if (container == null)
            {
                Debug.LogWarning("[LootingUIController] 열 LootContainer가 없습니다.", this);
                return;
            }

            if (IsOpen && currentContainer == container)
            {
                Close();
                return;
            }

            currentContainer = container;

            if (inventoryUI != null)
                inventoryUI.Open();

            if (lootingPanel != null)
                lootingPanel.SetActive(true);

            ApplyLootingPosition();

            if (containerGridView != null)
                containerGridView.Bind(container);
        }

        public void Close()
        {
            Close(closeInventory: true);
        }

        public void Close(bool closeInventory)
        {
            if (containerGridView != null)
                containerGridView.Clear();

            currentContainer = null;
            RestoreInventoryPosition();
            CloseLootingPanelOnly();

            if (closeInventory && inventoryUI != null)
                inventoryUI.Close();
        }

        [BoxGroup("로컬 테스트")]
        [Button("지정 상자 열기")]
        private void TestOpenAssignedContainer()
        {
            LootContainer container = testContainer != null
                ? testContainer
                : FindFirstObjectByType<LootContainer>(FindObjectsInactive.Include);

            if (container == null)
            {
                Debug.LogWarning("[LootingUIController] 테스트할 LootContainer를 찾지 못했습니다. Test Container에 상자를 연결하세요.", this);
                return;
            }

            container.OpenLocalForLootingUITest();
            Open(container);
        }

        [BoxGroup("로컬 테스트")]
        [Button("UI만 열기")]
        private void TestOpenPanelOnly()
        {
            if (IsOpen)
                RestoreInventoryPosition();

            ResolveReferences();
            CacheOriginalPositions();

            currentContainer = null;

            if (inventoryUI != null)
                inventoryUI.Open();

            if (lootingPanel != null)
                lootingPanel.SetActive(true);

            ApplyLootingPosition();

            if (containerGridView != null)
                containerGridView.Clear();
        }

        [BoxGroup("로컬 테스트")]
        [Button("닫기")]
        private void TestClose()
        {
            Close();
        }

        [BoxGroup("로컬 테스트")]
        [Button("현재 위치를 원위치로 저장")]
        private void SaveCurrentPositionsAsOriginal()
        {
            EnsureMoveTargets();
            originalMoveTargetPositions.Clear();

            for (int i = 0; i < activeMoveTargets.Count; i++)
                originalMoveTargetPositions.Add(activeMoveTargets[i] != null ? activeMoveTargets[i].anchoredPosition : Vector2.zero);

            hasOriginalPositionCache = originalMoveTargetPositions.Count == activeMoveTargets.Count;
        }

        [BoxGroup("로컬 테스트")]
        [Button("인벤토리 위치 복구")]
        private void TestRestoreInventoryPosition()
        {
            RestoreInventoryPosition();
        }

        private void CloseLootingPanelOnly()
        {
            if (lootingPanel != null)
                lootingPanel.SetActive(false);
        }

        private void ApplyLootingPosition()
        {
            EnsureMoveTargets();
            EnsureOriginalPositionCache();

            for (int i = 0; i < activeMoveTargets.Count; i++)
            {
                if (activeMoveTargets[i] == null || i >= originalMoveTargetPositions.Count)
                    continue;

                activeMoveTargets[i].anchoredPosition = originalMoveTargetPositions[i] + lootingInventoryOffset;
            }
        }

        private void RestoreInventoryPosition()
        {
            for (int i = 0; i < activeMoveTargets.Count; i++)
            {
                if (activeMoveTargets[i] == null || i >= originalMoveTargetPositions.Count)
                    continue;

                activeMoveTargets[i].anchoredPosition = originalMoveTargetPositions[i];
            }
        }

        private void CacheOriginalPositions()
        {
            EnsureMoveTargets();
            if (hasOriginalPositionCache && originalMoveTargetPositions.Count == activeMoveTargets.Count)
                return;

            originalMoveTargetPositions.Clear();

            for (int i = 0; i < activeMoveTargets.Count; i++)
                originalMoveTargetPositions.Add(activeMoveTargets[i] != null ? activeMoveTargets[i].anchoredPosition : Vector2.zero);

            hasOriginalPositionCache = originalMoveTargetPositions.Count == activeMoveTargets.Count;
        }

        private void EnsureOriginalPositionCache()
        {
            if (originalMoveTargetPositions.Count == activeMoveTargets.Count)
                return;

            CacheOriginalPositions();
        }

        private void EnsureMoveTargets()
        {
            int previousCount = activeMoveTargets.Count;
            activeMoveTargets.Clear();

            if (inventoryMoveTargets != null)
            {
                foreach (RectTransform target in inventoryMoveTargets)
                {
                    if (target != null && !activeMoveTargets.Contains(target))
                        activeMoveTargets.Add(target);
                }
            }

            if (activeMoveTargets.Count > 0)
                return;

            if (inventoryVisibleRoot == null)
                ResolveInventoryVisibleRoot();

            if (inventoryVisibleRoot == null)
                return;

            RectTransform mainPanel = FindDirectChildRect(inventoryVisibleRoot, "InventoryMainPanel");
            if (mainPanel != null)
            {
                activeMoveTargets.Add(mainPanel);
                if (previousCount != activeMoveTargets.Count)
                    hasOriginalPositionCache = false;

                return;
            }

            if (!autoCollectMoveTargets)
                return;

            foreach (Transform child in inventoryVisibleRoot)
            {
                if (child == null || ShouldIgnoreInventoryRootChild(child))
                    continue;

                RectTransform childRect = child as RectTransform;
                if (childRect != null)
                    activeMoveTargets.Add(childRect);
            }

            if (previousCount != activeMoveTargets.Count)
                hasOriginalPositionCache = false;
        }

        private void ResolveReferences()
        {
            if (inventoryUI == null)
                inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);

            if (inventoryVisibleRoot == null)
                ResolveInventoryVisibleRoot();

            if (lootingPanel == null)
                lootingPanel = FindSceneObjectByName("LootingPanel");

            if (containerGridView == null && lootingPanel != null)
                containerGridView = lootingPanel.GetComponentInChildren<ContainerGridView>(true);
        }

        private void ResolveInventoryVisibleRoot()
        {
            GameObject inventoryRoot = FindSceneObjectByName("InventoryVisibleRoot");
            if (inventoryRoot != null)
                inventoryVisibleRoot = inventoryRoot.transform as RectTransform;
        }

        private static bool ShouldIgnoreInventoryRootChild(Transform child)
        {
            string lowerName = child.name.ToLowerInvariant();

            return lowerName == "dim" ||
                   lowerName.Contains("dim") ||
                   lowerName == "lootingpanel" ||
                   lowerName.Contains("tooltip");
        }

        private static RectTransform FindDirectChildRect(Transform parent, string childName)
        {
            if (parent == null)
                return null;

            foreach (Transform child in parent)
            {
                if (child != null && child.name == childName)
                    return child as RectTransform;
            }

            return null;
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            GameObject[] candidates = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject candidate in candidates)
            {
                if (candidate == null || candidate.name != objectName)
                    continue;

                if (!candidate.scene.IsValid())
                    continue;

                return candidate;
            }

            return null;
        }
    }
}

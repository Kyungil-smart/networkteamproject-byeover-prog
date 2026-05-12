using System.Collections.Generic;
using DeadZone.Core;
using DeadZone.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class LobbyPlayerInventoryUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform bagRoot;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private ItemTooltipUI tooltipUI;

        [Header("Bag Level")]
        [Range(0, 4)]
        [SerializeField] private int bagLevel;
        [SerializeField] private int baseCapacity = 20;
        [SerializeField] private int level1Capacity = 25;
        [SerializeField] private int level2Capacity = 30;
        [SerializeField] private int level3Capacity = 35;
        [SerializeField] private int level4Capacity = 40;

        [Header("Scroll")]
        [SerializeField] private bool autoConfigureScroll = true;
        [SerializeField] private bool createScrollStructureIfMissing = true;
        [SerializeField] private bool hideRootImageWithoutSprite = true;
        [SerializeField] private float scrollSensitivity = 35f;

        [Header("Grid")]
        [SerializeField] private Vector2 cellSize = new Vector2(100f, 100f);
        [SerializeField] private Vector2 spacing = new Vector2(10f, 10f);
        [SerializeField] private int fixedColumnCount = 4;
        [SerializeField] private TextAnchor childAlignment = TextAnchor.UpperLeft;

        [Header("Slots")]
        [SerializeField] private List<InventorySlotUI> bagSlots = new List<InventorySlotUI>();

        private GridLayoutGroup originalRootGridLayout;
        private IItemDatabase itemDatabase;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            bagLevel = 0;
            AutoBindReferences();
            ConfigureScrollAndGrid();
            RefreshSlots();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<BackpackChangedEvent>(HandleBackpackChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BackpackChangedEvent>(HandleBackpackChanged);
        }

        private void Start()
        {
            RefreshSlots();
        }

        private void OnValidate()
        {
            bagLevel = Mathf.Clamp(bagLevel, 0, 4);
            baseCapacity = Mathf.Max(1, baseCapacity);
            level1Capacity = Mathf.Max(baseCapacity, level1Capacity);
            level2Capacity = Mathf.Max(level1Capacity, level2Capacity);
            level3Capacity = Mathf.Max(level2Capacity, level3Capacity);
            level4Capacity = Mathf.Max(level3Capacity, level4Capacity);
            fixedColumnCount = Mathf.Max(1, fixedColumnCount);
            scrollSensitivity = Mathf.Max(1f, scrollSensitivity);

            if (!Application.isPlaying)
                return;

            AutoBindReferences();
            ConfigureScrollAndGrid();
            RefreshSlots();
        }

        public void SetBagLevel(int level)
        {
            bagLevel = Mathf.Clamp(level, 0, 4);
            RefreshSlots();
        }

        private void HandleBackpackChanged(BackpackChangedEvent evt)
        {
            SetBagLevel(GetBagLevelFromBackpackId(evt.newBackpackId.ToString()));
        }

        public void RefreshSlots()
        {
            AutoBindReferences();
            ConfigureScrollAndGrid();
            RebuildSlotCache();

            int unlockedCount = Mathf.Min(GetCapacityByBagLevel(bagLevel), bagSlots.Count);

            for (int i = 0; i < bagSlots.Count; i++)
            {
                InventorySlotUI slot = bagSlots[i];

                if (slot == null)
                    continue;

                slot.PrepareDropSlot(tooltipUI, i);
                slot.SetLocked(i >= unlockedCount);
            }

            ApplyContentSize();
            Canvas.ForceUpdateCanvases();

            if (contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);

            if (viewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);

            Canvas.ForceUpdateCanvases();
            ResetScrollToTop();
        }

        private void AutoBindReferences()
        {
            if (bagRoot == null)
                bagRoot = transform as RectTransform;

            if (scrollRect == null)
                scrollRect = GetComponent<ScrollRect>();

            if (scrollRect == null)
                scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (scrollRect == null)
                scrollRect = GetComponentInParent<ScrollRect>(true);

            if (viewport == null && scrollRect != null)
                viewport = scrollRect.viewport;

            if (viewport == null && scrollRect != null)
                viewport = FindNamedRectTransform(scrollRect.transform, "Viewport");

            if (contentRoot == null && scrollRect != null)
                contentRoot = scrollRect.content;

            if (contentRoot == null || contentRoot == transform)
                contentRoot = FindContentRoot();

            if (viewport == null && contentRoot != null && contentRoot.parent is RectTransform parentRect)
                viewport = parentRect;

            if (scrollRect == null && createScrollStructureIfMissing && viewport != null && viewport.parent is RectTransform scrollRoot)
                scrollRect = scrollRoot.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>();

            if (scrollRect != null && scrollRect.content == null && contentRoot != null)
                scrollRect.content = contentRoot;

            if (scrollRect != null && scrollRect.viewport == null && viewport != null)
                scrollRect.viewport = viewport;

            if (tooltipUI == null)
                tooltipUI = GetComponentInParent<ItemTooltipUI>(true);
        }

        private void ConfigureScrollAndGrid()
        {
            if (!autoConfigureScroll || bagRoot == null)
                return;

            if (createScrollStructureIfMissing)
                EnsureScrollStructure();

            ConfigureViewportRect();
            ConfigureScrollRect();
            ConfigureGridLayout();
        }

        private void EnsureScrollStructure()
        {
            if (viewport == null)
                viewport = CreateRectChild(bagRoot, "Viewport");

            if (contentRoot == null)
                contentRoot = CreateRectChild(viewport, "Content");

            MoveSlotsToContent();
        }

        private void MoveSlotsToContent()
        {
            if (bagRoot == null || contentRoot == null)
                return;

            List<InventorySlotUI> slotsToMove = new List<InventorySlotUI>();

            for (int i = 0; i < bagRoot.childCount; i++)
            {
                Transform child = bagRoot.GetChild(i);

                if (child == viewport || child.GetComponent<InventorySlotUI>() == null)
                    continue;

                slotsToMove.Add(child.GetComponent<InventorySlotUI>());
            }

            for (int i = 0; i < slotsToMove.Count; i++)
                slotsToMove[i].transform.SetParent(contentRoot, false);
        }

        private RectTransform CreateRectChild(RectTransform parent, string objectName)
        {
            GameObject childObject = new GameObject(objectName, typeof(RectTransform));
            RectTransform childRect = childObject.GetComponent<RectTransform>();
            childRect.SetParent(parent, false);
            childRect.anchorMin = Vector2.zero;
            childRect.anchorMax = Vector2.one;
            childRect.offsetMin = Vector2.zero;
            childRect.offsetMax = Vector2.zero;
            childRect.pivot = new Vector2(0.5f, 0.5f);
            return childRect;
        }

        private void ConfigureScrollRect()
        {
            if (scrollRect == null)
                scrollRect = GetComponent<ScrollRect>() ?? gameObject.AddComponent<ScrollRect>();

            if (viewport == null || contentRoot == null)
                return;

            scrollRect.content = contentRoot;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.inertia = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = scrollSensitivity;

            HideUnusedRootImage();
        }

        private void ConfigureViewportRect()
        {
            if (viewport == null)
                return;

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            viewport.sizeDelta = Vector2.zero;

            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            Image viewportImage = viewport.GetComponent<Image>();
            if (viewportImage == null)
                viewportImage = viewport.gameObject.AddComponent<Image>();

            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;
        }

        private void HideUnusedRootImage()
        {
            if (!hideRootImageWithoutSprite || scrollRect == null)
                return;

            Image rootImage = scrollRect.GetComponent<Image>();

            if (rootImage != null && rootImage.sprite == null)
                rootImage.enabled = false;
        }

        private void ConfigureGridLayout()
        {
            if (contentRoot == null)
                return;

            originalRootGridLayout = bagRoot != null ? bagRoot.GetComponent<GridLayoutGroup>() : null;

            if (originalRootGridLayout != null)
                originalRootGridLayout.enabled = false;

            GridLayoutGroup gridLayout = contentRoot.GetComponent<GridLayoutGroup>();
            if (gridLayout == null)
                gridLayout = contentRoot.gameObject.AddComponent<GridLayoutGroup>();

            gridLayout.cellSize = cellSize;
            gridLayout.spacing = spacing;
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = childAlignment;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = fixedColumnCount;

            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(0f, 1f);
            contentRoot.pivot = new Vector2(0f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;

            ContentSizeFitter contentSizeFitter = contentRoot.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter == null)
                contentSizeFitter = contentRoot.gameObject.AddComponent<ContentSizeFitter>();

            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private void RebuildSlotCache()
        {
            bagSlots.Clear();

            RectTransform searchRoot = contentRoot != null ? contentRoot : bagRoot;

            if (searchRoot == null)
                return;

            InventorySlotUI[] foundSlots = searchRoot.GetComponentsInChildren<InventorySlotUI>(true);

            for (int i = 0; i < foundSlots.Length; i++)
            {
                if (foundSlots[i] != null && !bagSlots.Contains(foundSlots[i]))
                    bagSlots.Add(foundSlots[i]);
            }
        }

        private void ApplyContentSize()
        {
            if (contentRoot == null)
                return;

            int slotCount = Mathf.Max(1, bagSlots.Count);
            int rows = Mathf.CeilToInt(slotCount / (float)Mathf.Max(1, fixedColumnCount));
            float width = fixedColumnCount * cellSize.x + Mathf.Max(0, fixedColumnCount - 1) * spacing.x;
            float height = rows * cellSize.y + Mathf.Max(0, rows - 1) * spacing.y;

            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            contentRoot.anchoredPosition = Vector2.zero;
        }

        private void ResetScrollToTop()
        {
            if (scrollRect == null || contentRoot == null)
                return;

            scrollRect.StopMovement();
            scrollRect.velocity = Vector2.zero;
            scrollRect.verticalNormalizedPosition = 1f;
            contentRoot.anchoredPosition = Vector2.zero;
        }

        private int GetCapacityByBagLevel(int level)
        {
            return Mathf.Clamp(level, 0, 4) switch
            {
                0 => baseCapacity,
                1 => level1Capacity,
                2 => level2Capacity,
                3 => level3Capacity,
                4 => level4Capacity,
                _ => baseCapacity
            };
        }

        private int GetBagLevelFromBackpackId(string backpackId)
        {
            if (string.IsNullOrWhiteSpace(backpackId))
                return 0;

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();
            BackpackDataSO backpackData = itemDatabase?.GetById<BackpackDataSO>(backpackId);
            return backpackData != null ? Mathf.Clamp(backpackData.backpackLevel, 0, 4) : 0;
        }

        private RectTransform FindDirectChildRect(string objectName)
        {
            if (bagRoot == null)
                return null;

            return FindDirectChildRect(bagRoot, objectName);
        }

        private static RectTransform FindDirectChildRect(Transform parent, string objectName)
        {
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                RectTransform child = parent.GetChild(i) as RectTransform;

                if (child != null && child.name == objectName)
                    return child;
            }

            return null;
        }

        private RectTransform FindContentRoot()
        {
            if (scrollRect != null && scrollRect.content != null && scrollRect.content != transform)
                return scrollRect.content;

            if (viewport != null)
            {
                RectTransform viewportContent = FindNamedRectTransform(viewport, "Content");
                if (viewportContent != null && viewportContent != transform)
                    return viewportContent;
            }

            RectTransform namedContent = FindNamedRectTransform(transform, "Content");
            if (namedContent != null && namedContent != transform)
                return namedContent;

            GridLayoutGroup[] gridLayoutGroups = GetComponentsInChildren<GridLayoutGroup>(true);
            for (int i = 0; i < gridLayoutGroups.Length; i++)
            {
                if (gridLayoutGroups[i] != null && gridLayoutGroups[i].transform != transform)
                    return gridLayoutGroups[i].transform as RectTransform;
            }

            return null;
        }

        private static RectTransform FindNamedRectTransform(Transform root, string objectName)
        {
            if (root == null)
                return null;

            RectTransform[] rectTransforms = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rectTransforms.Length; i++)
            {
                RectTransform rectTransform = rectTransforms[i];
                if (rectTransform != null && rectTransform.name == objectName)
                    return rectTransform;
            }

            return null;
        }
    }
}

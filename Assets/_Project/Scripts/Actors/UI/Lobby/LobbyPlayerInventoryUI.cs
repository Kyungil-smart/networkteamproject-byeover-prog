using System.Collections.Generic;
using TMPro;
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
        [SerializeField] private TMP_Text slotCountText;
        [SerializeField] private ItemTooltipUI tooltipUI;

        [Header("Bag Level")]
        [Range(1, 3)]
        [SerializeField] private int bagLevel = 1;
        [SerializeField] private int level1Capacity = 10;
        [SerializeField] private int level2Capacity = 15;
        [SerializeField] private int level3Capacity = 20;

        [Header("Scroll")]
        [SerializeField] private bool autoConfigureScroll = true;
        [SerializeField] private bool createScrollStructureIfMissing = true;
        [SerializeField] private float scrollSensitivity = 35f;

        [Header("Grid")]
        [SerializeField] private Vector2 cellSize = new Vector2(100f, 100f);
        [SerializeField] private Vector2 spacing = new Vector2(10f, 10f);
        [SerializeField] private int fixedColumnCount = 2;
        [SerializeField] private TextAnchor childAlignment = TextAnchor.UpperCenter;

        [Header("Slots")]
        [SerializeField] private List<InventorySlotUI> bagSlots = new List<InventorySlotUI>();

        private GridLayoutGroup originalRootGridLayout;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
            ConfigureScrollAndGrid();
            RefreshSlots();
        }

        private void Start()
        {
            RefreshSlots();
        }

        private void OnValidate()
        {
            bagLevel = Mathf.Clamp(bagLevel, 1, 3);
            level1Capacity = Mathf.Max(1, level1Capacity);
            level2Capacity = Mathf.Max(level1Capacity, level2Capacity);
            level3Capacity = Mathf.Max(level2Capacity, level3Capacity);
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
            bagLevel = Mathf.Clamp(level, 1, 3);
            RefreshSlots();
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

            if (slotCountText != null)
                slotCountText.text = $"({unlockedCount}\uCE78)";

            ApplyContentSize();
            Canvas.ForceUpdateCanvases();

            if (contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        private void AutoBindReferences()
        {
            if (bagRoot == null)
                bagRoot = transform as RectTransform;

            if (scrollRect == null)
                scrollRect = GetComponent<ScrollRect>();

            if (viewport == null && scrollRect != null)
                viewport = scrollRect.viewport;

            if (contentRoot == null && scrollRect != null)
                contentRoot = scrollRect.content;

            if (viewport == null)
                viewport = FindDirectChildRect("Viewport");

            if (contentRoot == null && viewport != null)
                contentRoot = FindDirectChildRect(viewport, "Content");

            if (tooltipUI == null)
                tooltipUI = GetComponentInParent<ItemTooltipUI>(true);

            if (slotCountText == null)
                slotCountText = FindSlotCountText();
        }

        private void ConfigureScrollAndGrid()
        {
            if (!autoConfigureScroll || bagRoot == null)
                return;

            if (createScrollStructureIfMissing)
                EnsureScrollStructure();

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

            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            Image viewportImage = viewport.GetComponent<Image>();
            if (viewportImage == null)
                viewportImage = viewport.gameObject.AddComponent<Image>();

            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;
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
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;
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
            float height = rows * cellSize.y + Mathf.Max(0, rows - 1) * spacing.y;

            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            contentRoot.anchoredPosition = Vector2.zero;
        }

        private int GetCapacityByBagLevel(int level)
        {
            return Mathf.Clamp(level, 1, 3) switch
            {
                1 => level1Capacity,
                2 => level2Capacity,
                3 => level3Capacity,
                _ => level1Capacity
            };
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

        private TMP_Text FindSlotCountText()
        {
            Transform current = transform.parent;

            while (current != null)
            {
                TMP_Text[] texts = current.GetComponentsInChildren<TMP_Text>(true);

                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].name == "Text_SlotCount")
                        return texts[i];
                }

                current = current.parent;
            }

            return null;
        }
    }
}

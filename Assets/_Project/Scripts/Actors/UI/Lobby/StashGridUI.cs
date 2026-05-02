using System.Collections.Generic;
using DeadZone.Core;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class StashGridUI : MonoBehaviour
    {
        private const int BaseSlotCount = 50;
        private const int AdditionalSlotsPerLevel = 20;
        private const int MaxStashLevel = 4;
        private const int FixedColumnCount = 10;

        [Title("참조")]
        [Required]
        [SerializeField] private RectTransform contentRoot;

        [Required]
        [SerializeField] private InventorySlotUI slotPrefab;

        [SerializeField] private ItemTooltipUI tooltipUI;

        [Title("스크롤 설정")]
        [SerializeField] private ScrollRect scrollRect;

        [SerializeField] private RectTransform viewport;

        [SerializeField] private bool autoConfigureScrollRect = true;

        [SerializeField] private bool createScrollRectIfMissing = true;

        [MinValue(1f)]
        [SerializeField] private float scrollSensitivity = 35f;

        [Title("보관함 설정")]
        [MinValue(1)]
        [MaxValue(MaxStashLevel)]
        [SerializeField] private int stashLevel = 1;

        [SerializeField] private bool refreshOnAwake = true;

        [Title("그리드 설정")]
        [SerializeField] private Vector2 cellSize = new Vector2(100f, 100f);

        [SerializeField] private Vector2 spacing = new Vector2(4f, 4f);

        [SerializeField] private int paddingLeft;

        [SerializeField] private int paddingRight;

        [SerializeField] private int paddingTop;

        [SerializeField] private int paddingBottom;

        [SerializeField] private TextAnchor childAlignment = TextAnchor.UpperLeft;

        [Title("테스트 아이템 생성")]
        [Tooltip("보관함 랜덤 배치 테스트에 사용할 아이템 목록입니다.")]
        [SerializeField] private List<ItemDataSO> testItemPool = new List<ItemDataSO>();

        [Tooltip("스택 가능한 아이템을 생성할 때 1개가 아니라 랜덤 수량으로 생성합니다.")]
        [SerializeField] private bool randomizeStackCount = true;

        [Title("상태")]
        [ReadOnly]
        [SerializeField] private int activeSlotCount;

        [ReadOnly]
        [SerializeField] private List<InventorySlotUI> slots = new List<InventorySlotUI>();

        public int StashLevel => stashLevel;
        public int ActiveSlotCount => activeSlotCount;

        private void Reset()
        {
            AutoBindReferences();
            ApplyGridSettings();
        }

        private void Awake()
        {
            AutoBindReferences();

            if (refreshOnAwake)
                RefreshSlots();
        }

        private void Start()
        {
            RefreshSlots();
        }

        private void OnValidate()
        {
            stashLevel = Mathf.Clamp(stashLevel, 1, MaxStashLevel);
            AutoBindReferences();
            ApplyGridSettings();
        }

        public void SetLevel(int level)
        {
            stashLevel = Mathf.Clamp(level, 1, MaxStashLevel);
            RefreshSlots();
        }

        public void RefreshSlots()
        {
            AutoBindReferences();
            ApplyGridSettings();
            RebuildSlotCache();

            int targetCount = GetSlotCountByLevel(stashLevel);
            EnsureSlotCount(targetCount);
            ApplySlotVisibility(targetCount);
            ApplyContentSize(targetCount);

            activeSlotCount = targetCount;

            Canvas.ForceUpdateCanvases();
            if (contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            if (viewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        public int GetSlotCountByLevel(int level)
        {
            int clampedLevel = Mathf.Clamp(level, 1, MaxStashLevel);
            return BaseSlotCount + (clampedLevel - 1) * AdditionalSlotsPerLevel;
        }

        [Button("Lv1 테스트")]
        private void TestLevel1()
        {
            SetLevel(1);
        }

        [Button("Lv2 테스트")]
        private void TestLevel2()
        {
            SetLevel(2);
        }

        [Button("Lv3 테스트")]
        private void TestLevel3()
        {
            SetLevel(3);
        }

        [Button("Lv4 테스트")]
        private void TestLevel4()
        {
            SetLevel(4);
        }

        [Button("보관함 갱신")]
        private void RefreshFromInspector()
        {
            RefreshSlots();
        }

        [Button("랜덤 10개 생성")]
        private void GenerateRandom10()
        {
            GenerateRandomTestItems(10);
        }

        [Button("랜덤 20개 생성")]
        private void GenerateRandom20()
        {
            GenerateRandomTestItems(20);
        }

        [Button("랜덤 30개 생성")]
        private void GenerateRandom30()
        {
            GenerateRandomTestItems(30);
        }

        [Button("랜덤 40개 생성")]
        private void GenerateRandom40()
        {
            GenerateRandomTestItems(40);
        }

        [Button("랜덤 50개 생성")]
        private void GenerateRandom50()
        {
            GenerateRandomTestItems(50);
        }

        [Button("테스트 아이템 비우기")]
        private void ClearTestItems()
        {
            RefreshSlots();

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    slots[i].ClearItem();
            }
        }

        [Button("스크롤 상태 로그")]
        private void LogScrollState()
        {
            Debug.Log($"[StashGridUI] ScrollRect={scrollRect}, Viewport={viewport}, Content={contentRoot}, ContentHeight={(contentRoot != null ? contentRoot.rect.height : 0f)}, ViewportHeight={(viewport != null ? viewport.rect.height : 0f)}, Vertical={(scrollRect != null && scrollRect.vertical)}", this);
        }

        [Button("참조 자동 연결")]
        private void AutoBindReferences()
        {
            if (scrollRect == null)
                scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (scrollRect == null)
                scrollRect = GetComponentInParent<ScrollRect>(true);

            if (viewport == null && scrollRect != null)
                viewport = scrollRect.viewport;

            if (viewport == null && scrollRect != null)
                viewport = FindNamedRectTransform(scrollRect.transform, "Viewport");

            if (contentRoot == null || contentRoot == transform)
                contentRoot = FindContentRoot();

            if (viewport == null && contentRoot != null && contentRoot.parent is RectTransform parentRect)
                viewport = parentRect;

            if (scrollRect == null && contentRoot != null)
                scrollRect = contentRoot.GetComponentInParent<ScrollRect>(true);

            if (scrollRect == null && createScrollRectIfMissing && viewport != null && viewport.parent is RectTransform scrollRoot)
                scrollRect = scrollRoot.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>();

            if (scrollRect != null && scrollRect.content == null && contentRoot != null)
                scrollRect.content = contentRoot;

            if (scrollRect != null && scrollRect.viewport == null && viewport != null)
                scrollRect.viewport = viewport;

            if (slotPrefab == null)
                slotPrefab = contentRoot != null
                    ? contentRoot.GetComponentInChildren<InventorySlotUI>(true)
                    : GetComponentInChildren<InventorySlotUI>(true);

            if (tooltipUI == null)
                tooltipUI = GetComponentInParent<ItemTooltipUI>(true);
        }

        private void ApplyGridSettings()
        {
            if (contentRoot == null)
                return;

            if (contentRoot == transform)
            {
                Debug.LogError("[StashGridUI] Content Root가 StashGridUI 자신으로 잡혀 있습니다. ScrollView/Viewport/Content 오브젝트를 Content Root에 연결해야 합니다.", this);
                return;
            }

            ConfigureContentRectTransform();

            GridLayoutGroup gridLayoutGroup = contentRoot.GetComponent<GridLayoutGroup>();
            if (gridLayoutGroup == null)
                gridLayoutGroup = contentRoot.gameObject.AddComponent<GridLayoutGroup>();

            gridLayoutGroup.padding = CreatePadding();
            gridLayoutGroup.cellSize = cellSize;
            gridLayoutGroup.spacing = spacing;
            gridLayoutGroup.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayoutGroup.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayoutGroup.childAlignment = childAlignment;
            gridLayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayoutGroup.constraintCount = FixedColumnCount;

            ContentSizeFitter contentSizeFitter = contentRoot.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter == null)
                contentSizeFitter = contentRoot.gameObject.AddComponent<ContentSizeFitter>();

            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            ConfigureViewportRectTransform();
            ConfigureScrollRect();
        }

        private void ConfigureContentRectTransform()
        {
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(0f, 1f);
            contentRoot.pivot = new Vector2(0f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;
        }

        private void ConfigureViewportRectTransform()
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

        private void ConfigureScrollRect()
        {
            if (!autoConfigureScrollRect || scrollRect == null || contentRoot == null)
                return;

            if (viewport == null && contentRoot.parent is RectTransform parentRect)
                viewport = parentRect;

            scrollRect.content = contentRoot;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.inertia = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = scrollSensitivity;
        }

        private void RebuildSlotCache()
        {
            slots.Clear();

            if (contentRoot == null)
                return;

            InventorySlotUI[] foundSlots = contentRoot.GetComponentsInChildren<InventorySlotUI>(true);
            for (int i = 0; i < foundSlots.Length; i++)
            {
                InventorySlotUI slot = foundSlots[i];
                if (slot == null || slot == slotPrefab && slotPrefab.transform.IsChildOf(contentRoot) == false)
                    continue;

                if (!slots.Contains(slot))
                    slots.Add(slot);
            }
        }

        private void EnsureSlotCount(int targetCount)
        {
            if (contentRoot == null || slotPrefab == null)
            {
                Debug.LogWarning("[StashGridUI] Content 또는 슬롯 프리팹이 연결되지 않았습니다.", this);
                return;
            }

            while (slots.Count < targetCount)
            {
                InventorySlotUI slot = Instantiate(slotPrefab, contentRoot);
                slot.name = $"StashSlot_{slots.Count:000}";
                slots.Add(slot);
            }
        }

        private void ApplySlotVisibility(int targetCount)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                bool active = i < targetCount;
                slot.gameObject.SetActive(active);

                if (active)
                    slot.PrepareDropSlot(tooltipUI, i);
            }
        }

        private void ApplyContentSize(int targetCount)
        {
            if (contentRoot == null || contentRoot == transform)
                return;

            int rows = Mathf.CeilToInt(targetCount / (float)FixedColumnCount);
            float width = paddingLeft + paddingRight + FixedColumnCount * cellSize.x + Mathf.Max(0, FixedColumnCount - 1) * spacing.x;
            float height = paddingTop + paddingBottom + rows * cellSize.y + Mathf.Max(0, rows - 1) * spacing.y;

            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            contentRoot.anchoredPosition = Vector2.zero;
        }

        private RectOffset CreatePadding()
        {
            return new RectOffset(
                Mathf.Max(0, paddingLeft),
                Mathf.Max(0, paddingRight),
                Mathf.Max(0, paddingTop),
                Mathf.Max(0, paddingBottom));
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

        private void GenerateRandomTestItems(int count)
        {
            RefreshSlots();

            List<ItemDataSO> validItems = GetValidTestItems();
            if (validItems.Count == 0)
            {
                Debug.LogWarning("[StashGridUI] 테스트 아이템 풀이 비어 있습니다. Inspector의 '테스트 아이템 생성' 목록에 ItemDataSO를 넣어주세요.", this);
                return;
            }

            ClearActiveSlots();

            int targetCount = Mathf.Min(count, activeSlotCount, slots.Count);
            for (int i = 0; i < targetCount; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                ItemDataSO itemData = validItems[Random.Range(0, validItems.Count)];
                int stackCount = GetRandomStackCount(itemData);
                slot.SetItem(itemData, stackCount);
            }
        }

        private List<ItemDataSO> GetValidTestItems()
        {
            List<ItemDataSO> validItems = new List<ItemDataSO>();

            for (int i = 0; i < testItemPool.Count; i++)
            {
                ItemDataSO itemData = testItemPool[i];
                if (itemData != null)
                    validItems.Add(itemData);
            }

            return validItems;
        }

        private int GetRandomStackCount(ItemDataSO itemData)
        {
            if (itemData == null || !randomizeStackCount)
                return 1;

            int maxStack = Mathf.Max(1, itemData.maxStackSize);
            return maxStack <= 1 ? 1 : Random.Range(1, maxStack + 1);
        }

        private void ClearActiveSlots()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null || !slot.gameObject.activeSelf)
                    continue;

                slot.ClearItem();
            }
        }
    }
}

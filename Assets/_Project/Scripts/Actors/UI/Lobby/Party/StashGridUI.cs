using System.Collections.Generic;
using DeadZone.Core;
using DeadZone.Systems;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Network;
using DeadZone.Systems.Save;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 로비 보관함 그리드 UI를 생성하고 아이템 추가/소모 요청을 처리합니다.
    /// </summary>
    public class StashGridUI : MonoBehaviour, IInventory
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

        [SerializeField] private TMP_Text levelText;

        [SerializeField] private TMP_Text slotCountText;

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

        [Title("클라우드 저장")]
        [Tooltip("Cloud Save 로드 이벤트를 받으면 보관함 UI를 저장된 stash 데이터로 갱신합니다.")]
        [SerializeField] private bool loadCloudStashOnLoadedEvent = true;

        [Tooltip("IItemDatabase 서비스가 아직 등록되지 않은 로비 씬에서 Cloud Save itemId를 해석할 보조 데이터베이스입니다.")]
        [SerializeField] private ItemDatabaseSO cloudStashItemDatabase;

        [Tooltip("클라우드 보관함 데이터가 비어 있을 때 현재 UI 슬롯을 비울지 여부입니다.")]
        [SerializeField] private bool clearSlotsWhenCloudStashEmpty = true;

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

        [Title("디버그")]
        [Tooltip("Play 중 보관함 슬롯 캐시와 0번 슬롯 상태를 콘솔에 출력합니다.")]
        [SerializeField] private bool logSlotCacheOnRefresh = true;

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

        private void OnEnable()
        {
            EventBus.Subscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
        }

        private void Start()
        {
            RefreshSlots();

            if (TryApplyLobbyInventoryState())
                return;

            ApplyCloudStashIfAvailable();
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
            PrepareSlotTemplate();
            RebuildSlotCache();

            int targetCount = GetSlotCountByLevel(stashLevel);
            EnsureSlotCount(targetCount);
            ApplySlotVisibility(targetCount);
            ApplyContentSize(targetCount);

            activeSlotCount = targetCount;
            RefreshStatusText();

            Canvas.ForceUpdateCanvases();
            if (contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            if (viewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;

            LogSlotCacheState();
        }

        public int GetSlotCountByLevel(int level)
        {
            int clampedLevel = Mathf.Clamp(level, 1, MaxStashLevel);
            return BaseSlotCount + (clampedLevel - 1) * AdditionalSlotsPerLevel;
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (item == null || amount <= 0)
                return false;

            RefreshSlots();

            int maxStack = Mathf.Max(1, item.maxStackSize);
            if (GetRemainingCapacity(item, maxStack) < amount)
                return false;

            int remaining = amount;

            for (int i = 0; i < activeSlotCount && i < slots.Count && remaining > 0; i++)
            {
                InventorySlotUI slot = slots[i];
                if (!IsSameItemSlot(slot, item))
                    continue;

                int available = maxStack - slot.CurrentStackCount;
                if (available <= 0)
                    continue;

                int addCount = Mathf.Min(available, remaining);
                slot.SetItem(item, slot.CurrentStackCount + addCount);
                remaining -= addCount;
            }

            while (remaining > 0)
            {
                InventorySlotUI emptySlot = FindFirstEmptySlot();
                if (emptySlot == null)
                    return false;

                int stackCount = Mathf.Min(maxStack, remaining);
                emptySlot.SetItem(item, stackCount);
                remaining -= stackCount;
            }

            return true;
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            return GetItemCount(itemId) >= count;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            RefreshSlots();

            if (!HasItem(itemId, count))
                return false;

            int remaining = count;
            for (int i = 0; i < activeSlotCount && i < slots.Count && remaining > 0; i++)
            {
                InventorySlotUI slot = slots[i];
                if (!IsItemIdSlot(slot, itemId))
                    continue;

                ItemDataSO item = slot.CurrentItemData;
                int consumeCount = Mathf.Min(slot.CurrentStackCount, remaining);
                int nextCount = slot.CurrentStackCount - consumeCount;

                if (nextCount <= 0)
                    slot.ClearItem();
                else
                    slot.SetItem(item, nextCount);

                remaining -= consumeCount;
            }

            return true;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            RefreshSlots();

            int foundCount = 0;
            for (int i = 0; i < activeSlotCount && i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (IsItemIdSlot(slot, itemId))
                    foundCount += slot.CurrentStackCount;
            }

            return foundCount;
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

            if (levelText == null)
                levelText = FindNamedText(transform, "Text_Level");

            if (slotCountText == null)
                slotCountText = FindNamedText(transform, "Text_SlotCount");
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

        private void HandleCloudSaveLoaded(CloudSaveLoadedEvent e)
        {
            if (!loadCloudStashOnLoadedEvent)
                return;

            if (TryApplyLobbyInventoryState())
                return;

            ApplyCloudStashIfAvailable();
        }

        private bool TryApplyLobbyInventoryState()
        {
            LobbyInventoryState inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);

            if (inventoryState == null ||
                inventoryState.StashItems == null ||
                inventoryState.StashItems.Count == 0)
            {
                return false;
            }

            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();

            if (itemDatabase == null && cloudStashItemDatabase == null)
            {
                Debug.LogWarning("[StashGridUI] LobbyInventoryState has stash items, but no item database was found. Skipping legacy cloud stash apply to avoid wiping restored stash UI.", this);
                return true;
            }

            Debug.Log($"[StashGridUI] Applying LobbyInventoryState stash items. Count={inventoryState.StashItems.Count}", this);
            ApplyLobbyStashItems(inventoryState.StashItems, itemDatabase);
            return true;
        }

        private void ApplyLobbyStashItems(IReadOnlyList<ItemSaveDTO> stashItems, IItemDatabase itemDatabase)
        {
            RefreshSlots();
            ClearActiveSlots();

            if (stashItems == null)
                return;

            int appliedCount = 0;

            for (int i = 0; i < stashItems.Count; i++)
            {
                ItemSaveDTO savedItem = stashItems[i];

                if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.itemId))
                    continue;

                ItemDataSO itemData = ResolveCloudStashItemData(savedItem.itemId, itemDatabase);
                if (itemData == null)
                {
                    Debug.LogWarning($"[StashGridUI] LobbyInventoryState stash item could not be resolved. itemId={savedItem.itemId}", this);
                    continue;
                }

                InventorySlotUI targetSlot = FindSlotForLobbyItem(savedItem);
                if (targetSlot == null)
                {
                    Debug.LogWarning($"[StashGridUI] LobbyInventoryState stash slot could not be resolved. itemId={savedItem.itemId}, x={savedItem.x}", this);
                    continue;
                }

                targetSlot.SetItem(itemData, Mathf.Max(1, savedItem.stackCount));
                appliedCount++;
            }

            Debug.Log($"[StashGridUI] Applied LobbyInventoryState stash items. Applied={appliedCount}/{stashItems.Count}", this);
        }

        private InventorySlotUI FindSlotForLobbyItem(ItemSaveDTO savedItem)
        {
            if (savedItem == null)
                return null;

            int requestedIndex = savedItem.x;

            if (requestedIndex >= 0 && requestedIndex < activeSlotCount && requestedIndex < slots.Count)
            {
                InventorySlotUI requestedSlot = slots[requestedIndex];
                if (requestedSlot != null && requestedSlot != slotPrefab)
                    return requestedSlot;
            }

            return FindFirstEmptySlot();
        }

        private void ApplyCloudStashIfAvailable()
        {
            if (HasLobbyStashState())
                return;

            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData || cloudSaveSystem.CurrentData == null)
                return;

            StashData stashData = cloudSaveSystem.CurrentData.stash;
            if (stashData == null || stashData.slots == null)
                return;

            ApplyCloudStash(stashData.slots);
        }

        private static bool HasLobbyStashState()
        {
            LobbyInventoryState inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);
            return inventoryState != null &&
                   inventoryState.StashItems != null &&
                   inventoryState.StashItems.Count > 0;
        }

        private void ApplyCloudStash(IReadOnlyList<StashSlot> cloudSlots)
        {
            RefreshSlots();

            if (cloudSlots == null || cloudSlots.Count == 0)
            {
                if (clearSlotsWhenCloudStashEmpty)
                    ClearActiveSlots();

                return;
            }

            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null && cloudStashItemDatabase == null)
            {
                Debug.LogWarning("[StashGridUI] Cloud Save 보관함을 적용할 수 없습니다. IItemDatabase 서비스 또는 보조 ItemDatabaseSO가 필요합니다.", this);
                return;
            }

            ClearActiveSlots();

            for (int i = 0; i < cloudSlots.Count; i++)
            {
                StashSlot cloudSlot = cloudSlots[i];
                if (cloudSlot == null || string.IsNullOrWhiteSpace(cloudSlot.itemId) || cloudSlot.stackCount <= 0)
                    continue;

                ItemDataSO itemData = ResolveCloudStashItemData(cloudSlot.itemId, itemDatabase);
                if (itemData == null)
                {
                    Debug.LogWarning($"[StashGridUI] Cloud Save 보관함 아이템을 찾을 수 없습니다. ItemId={cloudSlot.itemId}", this);
                    continue;
                }

                InventorySlotUI targetSlot = FindSlotForCloudSlot(cloudSlot);
                if (targetSlot == null)
                {
                    Debug.LogWarning($"[StashGridUI] Cloud Save 보관함 슬롯이 부족합니다. ItemId={cloudSlot.itemId}", this);
                    continue;
                }

                targetSlot.SetItem(itemData, cloudSlot.stackCount);
            }
        }

        private ItemDataSO ResolveCloudStashItemData(string itemId, IItemDatabase itemDatabase)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            ItemDataSO itemData = itemDatabase?.GetById(itemId);
            if (itemData != null)
                return itemData;

            return cloudStashItemDatabase != null
                ? cloudStashItemDatabase.GetByID(itemId)
                : null;
        }

        private InventorySlotUI FindSlotForCloudSlot(StashSlot cloudSlot)
        {
            int requestedIndex = cloudSlot.gridY * FixedColumnCount + cloudSlot.gridX;
            if (requestedIndex >= 0 && requestedIndex < activeSlotCount && requestedIndex < slots.Count)
            {
                InventorySlotUI requestedSlot = slots[requestedIndex];
                if (requestedSlot != null && requestedSlot != slotPrefab && !requestedSlot.HasItem)
                    return requestedSlot;
            }

            return FindFirstEmptySlot();
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
                if (slot == null || slot == slotPrefab)
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
                slot.gameObject.SetActive(true);
                slot.ClearItem();
                slot.PrepareDropSlot(tooltipUI, slots.Count);
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
                {
                    slot.name = $"StashSlot_{i:000}";
                    slot.CopyRarityBackgroundSpritesFrom(slotPrefab);
                    slot.PrepareDropSlot(tooltipUI, i);

                    if (!slot.HasItem)
                        slot.ClearItem();
                }
            }
        }

        private void PrepareSlotTemplate()
        {
            if (slotPrefab == null)
                return;

            slotPrefab.ClearItem();

            if (contentRoot != null && slotPrefab.transform.IsChildOf(contentRoot))
                slotPrefab.gameObject.SetActive(false);
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

        private void RefreshStatusText()
        {
            if (levelText != null)
                levelText.text = $"Lv.{stashLevel}";

            if (slotCountText != null)
                slotCountText.text = $"({activeSlotCount}\uCE78)";
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

        private static TMP_Text FindNamedText(Transform root, string objectName)
        {
            if (root == null)
                return null;

            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text != null && text.name == objectName)
                    return text;
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

            int targetCount = Mathf.Min(count, activeSlotCount, slots.Count, validItems.Count);
            for (int i = 0; i < targetCount; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                int itemIndex = Random.Range(0, validItems.Count);
                ItemDataSO itemData = validItems[itemIndex];
                validItems.RemoveAt(itemIndex);

                int stackCount = GetRandomStackCount(itemData);
                slot.SetItem(itemData, stackCount);
            }

            LogSlotCacheState();
        }

        private List<ItemDataSO> GetValidTestItems()
        {
            List<ItemDataSO> validItems = new List<ItemDataSO>();
            HashSet<ItemDataSO> addedItems = new HashSet<ItemDataSO>();
            HashSet<string> addedItemIds = new HashSet<string>();

            for (int i = 0; i < testItemPool.Count; i++)
            {
                ItemDataSO itemData = testItemPool[i];
                if (itemData == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(itemData.itemID))
                {
                    if (!addedItemIds.Add(itemData.itemID))
                        continue;
                }
                else if (!addedItems.Add(itemData))
                {
                    continue;
                }

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

        private int GetRemainingCapacity(ItemDataSO item, int maxStack)
        {
            int capacity = 0;

            for (int i = 0; i < activeSlotCount && i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null || slot == slotPrefab)
                    continue;

                if (!slot.HasItem)
                {
                    capacity += maxStack;
                    continue;
                }

                if (IsSameItemSlot(slot, item))
                    capacity += Mathf.Max(0, maxStack - slot.CurrentStackCount);
            }

            return capacity;
        }

        private InventorySlotUI FindFirstEmptySlot()
        {
            for (int i = 0; i < activeSlotCount && i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot != null && slot != slotPrefab && !slot.HasItem)
                    return slot;
            }

            return null;
        }

        private static bool IsSameItemSlot(InventorySlotUI slot, ItemDataSO item)
        {
            if (slot == null || item == null || !slot.HasItem || slot.CurrentItemData == null)
                return false;

            if (!string.IsNullOrWhiteSpace(item.itemID) && !string.IsNullOrWhiteSpace(slot.CurrentItemData.itemID))
                return slot.CurrentItemData.itemID == item.itemID;

            return slot.CurrentItemData == item;
        }

        private static bool IsItemIdSlot(InventorySlotUI slot, string itemId)
        {
            return slot != null &&
                   slot.HasItem &&
                   slot.CurrentItemData != null &&
                   slot.CurrentItemData.itemID == itemId;
        }

        private void LogSlotCacheState()
        {
            if (!logSlotCacheOnRefresh || !Application.isPlaying)
                return;

            string prefabPath = slotPrefab != null ? GetHierarchyPath(slotPrefab.transform) : "null";
            string firstSlotPath = slots.Count > 0 && slots[0] != null ? GetHierarchyPath(slots[0].transform) : "null";
            string firstIconInfo = slots.Count > 0 && slots[0] != null ? GetSlotIconInfo(slots[0]) : "null";
            bool prefabInsideContent = contentRoot != null && slotPrefab != null && slotPrefab.transform.IsChildOf(contentRoot);
            bool firstIsPrefab = slots.Count > 0 && slots[0] == slotPrefab;

            Debug.Log($"[StashGridUI] SlotCache: count={slots.Count}, activeSlotCount={activeSlotCount}, slotPrefabInsideContent={prefabInsideContent}, slotPrefabActive={(slotPrefab != null && slotPrefab.gameObject.activeSelf)}, slots[0].IsPrefab={firstIsPrefab}, slotPrefab={prefabPath}, slots[0]={firstSlotPath}, slots[0].Icon={firstIconInfo}", this);
        }

        private static string GetSlotIconInfo(InventorySlotUI slot)
        {
            RectTransform iconTransform = FindNamedRectTransform(slot.transform, "Icon_Item");
            if (iconTransform == null)
                return "Icon_Item=null";

            Image iconImage = iconTransform.GetComponent<Image>();
            bool hasSprite = iconImage != null && iconImage.sprite != null;
            return $"parent={GetHierarchyPath(iconTransform.parent)}, active={iconTransform.gameObject.activeSelf}, enabled={(iconImage != null && iconImage.enabled)}, hasSprite={hasSprite}";
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return "null";

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }
    }
}

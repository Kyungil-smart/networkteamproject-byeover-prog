using System.Collections.Generic;
using DeadZone.Core;
using DeadZone.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public enum TraderId
    {
        Igor,
        Vera,
        Doc,
        Shade
    }

    public sealed class TraderPageView : MonoBehaviour
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상세 화면 참조")]
#else
        [Header("상세 화면 참조")]
#endif
        [SerializeField] private Image selectedTraderPortraitImage;
        [SerializeField] private TMP_Text traderNameText;
        [SerializeField] private Button buyTabButton;
        [SerializeField] private Button sellTabButton;
        [SerializeField] private Transform buyContent;
        [SerializeField] private Transform sellContent;
        [SerializeField] private TraderItemEntryUI itemEntryPrefab;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상인 데이터")]
#else
        [Header("상인 데이터")]
#endif
        [SerializeField] private TraderDataSO igorTraderData;
        [SerializeField] private TraderDataSO veraTraderData;
        [SerializeField] private TraderDataSO docTraderData;
        [SerializeField] private TraderDataSO shadeTraderData;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("상인 초상화")]
#else
        [Header("상인 초상화")]
#endif
        [SerializeField] private Sprite igorPortrait;
        [SerializeField] private Sprite veraPortrait;
        [SerializeField] private Sprite docPortrait;
        [SerializeField] private Sprite shadePortrait;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("통신장비")]
#else
        [Header("통신장비")]
#endif
        [Tooltip("테스트용 통신장비 레벨입니다. TraderEntry.requiredCommLevel보다 낮으면 Lock을 켭니다.")]
        [SerializeField] private int currentCommLevel;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("연동 대상")]
#else
        [Header("연동 대상")]
#endif
        [SerializeField] private TraderManager traderManager;

        private TraderId currentTraderId;
        private bool showingBuyTab = true;
        private readonly List<TraderEntry> testPurchasedEntries = new List<TraderEntry>();

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("리스트 레이아웃")]
#else
        [Header("리스트 레이아웃")]
#endif
        [SerializeField] private bool autoConfigureListLayout = true;
        [SerializeField] private float itemEntryWidth = 0f;
        [SerializeField] private float itemEntryHeight = 72f;
        [SerializeField] private float itemEntrySpacing = 0f;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("테스트 거래")]
#else
        [Header("테스트 거래")]
#endif
        [SerializeField] private bool useTestCurrency = true;
        [SerializeField] private int testCurrency = 999999;
        [SerializeField] private MonoBehaviour testInventoryTarget;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            currentCommLevel = Mathf.Max(0, currentCommLevel);
            testCurrency = Mathf.Max(0, testCurrency);
            itemEntryWidth = Mathf.Max(0f, itemEntryWidth);
            itemEntryHeight = Mathf.Max(1f, itemEntryHeight);
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
            BindTabButtons();
        }

        private void OnDestroy()
        {
            UnbindTabButtons();
        }

        public void Close()
        {
            ClearContent(buyContent);
            ClearContent(sellContent);

            if (selectedTraderPortraitImage != null)
            {
                selectedTraderPortraitImage.sprite = null;
                selectedTraderPortraitImage.enabled = false;
            }

            if (traderNameText != null)
                traderNameText.text = string.Empty;
        }

        public void SelectTrader(TraderId traderId)
        {
            Debug.Log($"[TraderPageView] SelectTrader 호출: {traderId}", this);

            currentTraderId = traderId;
            TraderDataSO traderData = GetTraderData(traderId);

            if (traderNameText != null)
                traderNameText.text = GetTraderName(traderId, traderData);

            if (selectedTraderPortraitImage != null)
            {
                Sprite portrait = GetTraderPortrait(traderId);
                selectedTraderPortraitImage.sprite = portrait;
                selectedTraderPortraitImage.enabled = portrait != null;
                selectedTraderPortraitImage.raycastTarget = false;
            }

            RebuildBuyList(traderData);
            RebuildSellList();
            ShowBuyTab();
        }

        public void SelectTrader(int traderIndex)
        {
            if (traderIndex < 0 || traderIndex > 3)
            {
                Debug.LogWarning($"[TraderPageView] 잘못된 상인 인덱스입니다. Index={traderIndex}", this);
                return;
            }

            SelectTrader((TraderId)traderIndex);
        }

        public void SetCurrentCommLevel(int commLevel)
        {
            currentCommLevel = Mathf.Max(0, commLevel);
            SelectTrader(currentTraderId);
        }

        private void RebuildBuyList(TraderDataSO traderData)
        {
            AutoBindReferences();
            ConfigureListRoot(buyContent);

            if (buyContent == null)
            {
                Debug.LogWarning("[TraderPageView] buyContent가 연결되지 않았습니다. Content_BuyList를 연결해야 구매 리스트를 생성할 수 있습니다.", this);
                return;
            }

            TraderItemEntryUI entryTemplate = ResolveItemEntryTemplate();
            ClearContentExceptTemplate(buyContent, entryTemplate);

            if (entryTemplate == null)
            {
                Debug.LogWarning(
                    $"[TraderPageView] ItemEntry 템플릿을 찾지 못했습니다. itemEntryPrefab이 비어 있거나 ItemEntry에 TraderItemEntryUI가 없습니다. Trader={currentTraderId}, BuyContent={GetObjectName(buyContent)}, StockCount={GetStockCount(traderData)}",
                    this);
                return;
            }

            if (traderData == null || traderData.stock == null)
            {
                Debug.LogWarning($"[TraderPageView] {currentTraderId} TraderDataSO 또는 stock 목록이 비어 있습니다.", this);
                return;
            }

            for (int i = 0; i < traderData.stock.Count; i++)
            {
                TraderItemEntryUI entryUI = Instantiate(entryTemplate, buyContent);
                ConfigureEntryLayout(entryUI, entryTemplate);
                entryUI.SetupBuyEntry(traderData.stock[i], currentCommLevel, HandleBuyClicked);
            }

            if (entryTemplate.transform.IsChildOf(buyContent))
                entryTemplate.gameObject.SetActive(false);

            if (buyContent is RectTransform buyContentRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(buyContentRect);

            Debug.Log($"[TraderPageView] 구매 리스트 생성 완료. Trader={currentTraderId}, Count={traderData.stock.Count}, Content={buyContent.name}, Template={entryTemplate.name}", this);
        }

        private void RebuildSellList()
        {
            AutoBindReferences();
            ConfigureListRoot(sellContent);

            if (sellContent == null)
                return;

            TraderItemEntryUI entryTemplate = ResolveItemEntryTemplate();
            ClearContentExceptTemplate(sellContent, entryTemplate);

            if (entryTemplate == null)
            {
                Debug.LogWarning("[TraderPageView] 판매 리스트를 만들 수 없습니다. ItemEntry 프리팹 또는 TraderItemEntryUI 템플릿 연결이 필요합니다.", this);
                return;
            }

            if (useTestCurrency && testPurchasedEntries.Count > 0)
            {
                for (int i = 0; i < testPurchasedEntries.Count; i++)
                {
                    TraderItemEntryUI entryUI = Instantiate(entryTemplate, sellContent);
                    ConfigureEntryLayout(entryUI, entryTemplate);
                    entryUI.SetupSellEntry(testPurchasedEntries[i], HandleSellClicked);
                }

                if (entryTemplate.transform.IsChildOf(sellContent))
                    entryTemplate.gameObject.SetActive(false);

                if (sellContent is RectTransform sellContentRect)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(sellContentRect);

                Debug.Log($"[TraderPageView] 테스트 판매 리스트 생성 완료. Count={testPurchasedEntries.Count}, Content={sellContent.name}", this);
                return;
            }

            Debug.LogWarning(
                "[TraderPageView] 판매 리스트를 생성하려면 현재 플레이어 인벤토리/보관함의 판매 가능 아이템 조회 API와 선택 아이템 판매 API가 필요합니다. 예: Stash/Inventory GetSellableItems, ConsumeItem 또는 RemoveItem, WalletSystem.Earn.",
                this);
        }

        public void ShowBuyTab()
        {
            showingBuyTab = true;
            SetListVisible(buyContent, true);
            SetListVisible(sellContent, false);
            SetTabUnderline(buyTabButton, true);
            SetTabUnderline(sellTabButton, false);
            BringTabButtonsToFront();
        }

        public void ShowSellTab()
        {
            showingBuyTab = false;
            SetListVisible(buyContent, false);
            SetListVisible(sellContent, true);
            SetTabUnderline(buyTabButton, false);
            SetTabUnderline(sellTabButton, true);
            BringTabButtonsToFront();
            RebuildSellList();
        }

        private void HandleBuyClicked(TraderEntry entry)
        {
            if (entry.item == null)
            {
                Debug.LogWarning("[TraderPageView] 구매 요청 실패: TraderEntry.item이 비어 있습니다.", this);
                return;
            }

            if (entry.requiredCommLevel > currentCommLevel)
            {
                Debug.LogWarning(
                    $"[TraderPageView] 구매 요청 차단: 통신장비 레벨 부족. 현재 Lv.{currentCommLevel}, 필요 Lv.{entry.requiredCommLevel}, Item={entry.item.itemID}",
                    this);
                return;
            }

            if (traderManager != null && traderManager.IsSpawned)
            {
                traderManager.BuyItemServerRpc(entry.item.itemID);
                return;
            }

            if (useTestCurrency)
            {
                TryBuyWithTestCurrency(entry);
                return;
            }

            Debug.LogWarning(
                "[TraderPageView] 실제 구매 확정을 위해 Spawn된 TraderManager.BuyItemServerRpc(string itemID)와 서버 내부 WalletSystem.TryPay, Stash/Inventory TryAddItem API 연동이 필요합니다.",
                this);
        }

        private void HandleSellClicked(TraderEntry entry)
        {
            if (entry.item == null)
            {
                Debug.LogWarning("[TraderPageView] 판매 요청 실패: TraderEntry.item이 비어 있습니다.", this);
                return;
            }

            int entryIndex = FindPurchasedEntryIndex(entry);
            if (entryIndex < 0)
            {
                Debug.LogWarning($"[TraderPageView] 테스트 판매 실패: 구매 테스트 목록에서 아이템을 찾지 못했습니다. Item={entry.item.itemID}", this);
                return;
            }

            IInventory inventory = ResolveInventoryTarget();
            if (inventory != null && !inventory.ConsumeItem(entry.item.itemID, 1))
            {
                Debug.LogWarning("[TraderPageView] IInventory.ConsumeItem(string itemId, int count) 호출이 실패했습니다. 로비 시스템 보관함에서 아이템을 제거하는 API가 필요합니다.", this);
            }

            testPurchasedEntries.RemoveAt(entryIndex);
            int sellPrice = CalculateSellPrice(entry);
            testCurrency += sellPrice;

            Debug.Log($"[TraderPageView] 테스트 판매 완료. Item={entry.item.itemID}, Price={sellPrice}, TestCurrency={testCurrency}", this);
            RebuildSellList();
        }

        private void TryBuyWithTestCurrency(TraderEntry entry)
        {
            int price = Mathf.Max(0, entry.basePrice);
            if (testCurrency < price)
            {
                Debug.LogWarning($"[TraderPageView] 테스트 구매 실패: 테스트 재화가 부족합니다. 현재={testCurrency}, 필요={price}", this);
                return;
            }

            IInventory inventory = ResolveInventoryTarget();
            if (inventory != null && !inventory.TryAddItem(entry.item, 1))
            {
                Debug.LogWarning("[TraderPageView] IInventory.TryAddItem(ItemDataSO item, int amount) 호출이 실패했습니다. 구매 아이템을 인벤토리 탭의 시스템 보관함에 넣는 API가 필요합니다.", this);
            }
            else if (inventory == null)
            {
                Debug.LogWarning("[TraderPageView] 시스템 보관함 IInventory 대상을 찾지 못했습니다. 테스트 목록에는 추가하지만, 인벤토리 탭 보관함에 실제 반영하려면 시스템 보관함 TryAddItem API 연결이 필요합니다.", this);
            }

            testCurrency -= price;
            testPurchasedEntries.Add(entry);

            Debug.Log($"[TraderPageView] 테스트 구매 완료. Item={entry.item.itemID}, Price={price}, TestCurrency={testCurrency}", this);

            if (!showingBuyTab)
                RebuildSellList();
        }

        private IInventory ResolveInventoryTarget()
        {
            if (testInventoryTarget is IInventory configuredInventory)
                return configuredInventory;

            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IInventory inventory)
                    return inventory;
            }

            return null;
        }

        private int FindPurchasedEntryIndex(TraderEntry entry)
        {
            string itemId = entry.item != null ? entry.item.itemID : string.Empty;
            for (int i = 0; i < testPurchasedEntries.Count; i++)
            {
                TraderEntry purchasedEntry = testPurchasedEntries[i];
                if (purchasedEntry.item == entry.item)
                    return i;

                if (purchasedEntry.item != null && !string.IsNullOrWhiteSpace(itemId) && purchasedEntry.item.itemID == itemId)
                    return i;
            }

            return -1;
        }

        private int CalculateSellPrice(TraderEntry entry)
        {
            TraderDataSO traderData = GetTraderData(currentTraderId);
            float multiplier = traderData != null ? traderData.sellMultiplier : 0.5f;
            return Mathf.Max(0, Mathf.RoundToInt(entry.basePrice * multiplier));
        }

        private TraderDataSO GetTraderData(TraderId traderId)
        {
            return traderId switch
            {
                TraderId.Igor => igorTraderData,
                TraderId.Vera => veraTraderData,
                TraderId.Doc => docTraderData,
                TraderId.Shade => shadeTraderData,
                _ => null
            };
        }

        private Sprite GetTraderPortrait(TraderId traderId)
        {
            return traderId switch
            {
                TraderId.Igor => igorPortrait,
                TraderId.Vera => veraPortrait,
                TraderId.Doc => docPortrait,
                TraderId.Shade => shadePortrait,
                _ => null
            };
        }

        private static string GetTraderName(TraderId traderId, TraderDataSO traderData)
        {
            if (traderData != null && !string.IsNullOrWhiteSpace(traderData.traderName))
                return traderData.traderName;

            return traderId switch
            {
                TraderId.Igor => "무기상 이고르",
                TraderId.Vera => "군수상 베라",
                TraderId.Doc => "의료상 닥터",
                TraderId.Shade => "밀수상 셰이드",
                _ => traderId.ToString()
            };
        }

        private void AutoBindReferences()
        {
            if (traderManager == null)
                traderManager = FindObjectOfType<TraderManager>();

            if (selectedTraderPortraitImage == null)
                selectedTraderPortraitImage = FindImage("Img_SelectedTrader", "SelectedTrader", "Img_Igor", "Igor", "Portrait");

            if (traderNameText == null)
                traderNameText = FindText("TraderName", "Name");

            if (!IsNamedButton(buyTabButton, "Tab_Buy"))
                buyTabButton = FindOrCreateTabButton("Tab_Buy", "Buy", "구매");

            if (!IsNamedButton(sellTabButton, "Tab_Sell"))
                sellTabButton = FindOrCreateTabButton("Tab_Sell", "Sell", "판매");

            if (buyContent != null && IsTabTransform(buyContent))
                buyContent = null;

            if (sellContent != null && IsTabTransform(sellContent))
                sellContent = null;

            if (buyContent == null)
                buyContent = FindTransform("Content_BuyList");

            if (sellContent == null)
                sellContent = FindTransform("Content_SellList");

            if (itemEntryPrefab == null)
                itemEntryPrefab = FindItemEntryTemplateInDetailRoot();
        }

        private void BindTabButtons()
        {
            UnbindTabButtons();

            if (buyTabButton != null)
                buyTabButton.onClick.AddListener(ShowBuyTab);

            if (sellTabButton != null)
                sellTabButton.onClick.AddListener(ShowSellTab);

            BringTabButtonsToFront();
        }

        private void UnbindTabButtons()
        {
            if (buyTabButton != null)
                buyTabButton.onClick.RemoveListener(ShowBuyTab);

            if (sellTabButton != null)
                sellTabButton.onClick.RemoveListener(ShowSellTab);
        }

        private TraderItemEntryUI ResolveItemEntryTemplate()
        {
            if (itemEntryPrefab != null)
                return itemEntryPrefab;

            itemEntryPrefab = FindItemEntryTemplateInDetailRoot();
            if (itemEntryPrefab != null)
                return itemEntryPrefab;

            if (buyContent == null)
                return null;

            TraderItemEntryUI template = buyContent.GetComponentInChildren<TraderItemEntryUI>(true);
            if (template != null)
                itemEntryPrefab = template;

            return template;
        }

        private TraderItemEntryUI FindItemEntryTemplateInDetailRoot()
        {
            TraderItemEntryUI[] candidates = GetComponentsInChildren<TraderItemEntryUI>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                TraderItemEntryUI candidate = candidates[i];
                if (candidate == null)
                    continue;

                Transform candidateTransform = candidate.transform;
                if (buyContent != null && candidateTransform.IsChildOf(buyContent))
                    continue;

                if (sellContent != null && candidateTransform.IsChildOf(sellContent))
                    continue;

                return candidate;
            }

            return null;
        }

        private void ConfigureListRoot(Transform content)
        {
            if (content == null)
                return;

            RectTransform contentRect = content as RectTransform;
            if (contentRect != null)
                contentRect.pivot = new Vector2(contentRect.pivot.x, 1f);

            if (!autoConfigureListLayout)
                return;

            VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
                layoutGroup = content.gameObject.AddComponent<VerticalLayoutGroup>();

            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = false;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = itemEntrySpacing;

            ContentSizeFitter sizeFitter = content.GetComponent<ContentSizeFitter>();
            if (sizeFitter == null)
                sizeFitter = content.gameObject.AddComponent<ContentSizeFitter>();

            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = content.GetComponentInParent<ScrollRect>(true);
            if (scrollRect == null && content.parent is RectTransform scrollRoot)
                scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();

            if (scrollRect != null)
            {
                scrollRect.content = contentRect;
                if (scrollRect.viewport == null && content.parent is RectTransform parentRect)
                    scrollRect.viewport = parentRect;

                if (scrollRect.viewport != null)
                    EnsureViewportRaycastAndMask(scrollRect.viewport);

                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 35f;
            }
        }

        private static void SetListVisible(Transform content, bool visible)
        {
            GameObject listRoot = ResolveListRoot(content);
            if (listRoot != null)
            {
                listRoot.SetActive(visible);
                return;
            }

            SetActiveSafe(content != null ? content.gameObject : null, visible);
        }

        private static GameObject ResolveListRoot(Transform content)
        {
            if (content == null)
                return null;

            ScrollRect scrollRect = content.GetComponentInParent<ScrollRect>(true);
            if (scrollRect != null)
                return scrollRect.gameObject;

            return content.gameObject;
        }

        private void BringTabButtonsToFront()
        {
            if (buyTabButton != null)
                buyTabButton.transform.SetAsLastSibling();

            if (sellTabButton != null)
                sellTabButton.transform.SetAsLastSibling();
        }

        private static void EnsureViewportRaycastAndMask(RectTransform viewport)
        {
            if (viewport == null)
                return;

            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            Graphic graphic = viewport.GetComponent<Graphic>();
            if (graphic == null)
            {
                Image image = viewport.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
                graphic = image;
            }

            graphic.raycastTarget = true;
        }

        private void ConfigureEntryLayout(TraderItemEntryUI entryUI, TraderItemEntryUI template)
        {
            if (entryUI == null)
                return;

            RectTransform entryRect = entryUI.transform as RectTransform;
            RectTransform templateRect = template != null ? template.transform as RectTransform : null;
            if (entryRect == null)
                return;

            if (templateRect != null)
            {
                entryRect.anchorMin = templateRect.anchorMin;
                entryRect.anchorMax = templateRect.anchorMax;
                entryRect.pivot = templateRect.pivot;
                entryRect.sizeDelta = templateRect.sizeDelta;
                entryRect.localScale = templateRect.localScale;
            }

            LayoutElement layoutElement = entryUI.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = entryUI.gameObject.AddComponent<LayoutElement>();

            float templateHeight = templateRect != null ? templateRect.rect.height : 0f;
            float preferredHeight = itemEntryHeight > 0f ? itemEntryHeight : templateHeight;
            if (preferredHeight <= 0f)
                preferredHeight = 72f;

            float contentWidth = entryRect.parent is RectTransform parentRect ? parentRect.rect.width : 0f;
            float templateWidth = templateRect != null ? templateRect.rect.width : 0f;
            float preferredWidth = itemEntryWidth > 0f ? itemEntryWidth : contentWidth > 0f ? contentWidth : templateWidth;

            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;
            layoutElement.flexibleHeight = 0f;
            layoutElement.flexibleWidth = 0f;
        }

        private Image FindImage(params string[] nameTokens)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                    continue;

                string lowerName = image.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return image;
                }
            }

            return null;
        }

        private TMP_Text FindText(params string[] nameTokens)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                    continue;

                string lowerName = text.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return text;
                }
            }

            return null;
        }

        private Transform FindTransform(params string[] nameTokens)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                string lowerName = child.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return child;
                }
            }

            return null;
        }

        private Button FindOrCreateTabButton(params string[] nameTokens)
        {
            Transform tabTransform = FindTransformByExactOrToken(nameTokens);
            if (tabTransform != null)
            {
                Button button = tabTransform.GetComponent<Button>();
                if (button == null)
                    button = tabTransform.gameObject.AddComponent<Button>();

                EnsureButtonRaycastTarget(button);
                return button;
            }

            Button foundButton = FindButton(nameTokens);
            if (foundButton != null)
            {
                EnsureButtonRaycastTarget(foundButton);
                return foundButton;
            }

            return null;
        }

        private static void EnsureButtonRaycastTarget(Button button)
        {
            if (button == null)
                return;

            if (button.targetGraphic == null)
            {
                Graphic graphic = button.GetComponent<Graphic>();
                if (graphic == null)
                {
                    Image image = button.gameObject.AddComponent<Image>();
                    image.color = new Color(1f, 1f, 1f, 0f);
                    graphic = image;
                }

                button.targetGraphic = graphic;
            }

            button.targetGraphic.raycastTarget = true;
        }

        private Button FindButton(params string[] nameTokens)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && button.name == token)
                        return button;
                }

                string searchText = button.name.ToLowerInvariant();
                TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true);
                for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    if (labels[labelIndex] != null)
                        searchText += " " + labels[labelIndex].text.ToLowerInvariant();
                }

                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && searchText.Contains(token.ToLowerInvariant()))
                        return button;
                }
            }

            return null;
        }

        private Transform FindTransformByExactOrToken(params string[] nameTokens)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && child.name == token)
                        return child;
                }
            }

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                string lowerName = child.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return child;
                }
            }

            return null;
        }

        private static bool IsTabTransform(Transform target)
        {
            return target != null && target.name.StartsWith("Tab_");
        }

        private static bool IsNamedButton(Button button, string objectName)
        {
            return button != null && button.name == objectName;
        }

        private static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null)
                target.SetActive(active);
        }

        private static void SetTabUnderline(Button tabButton, bool selected)
        {
            if (tabButton == null)
                return;

            Transform[] children = tabButton.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && child != tabButton.transform && child.name.Contains("Underline"))
                    child.gameObject.SetActive(selected);
            }
        }

        private static void ClearContent(Transform content)
        {
            if (content == null || !Application.isPlaying)
                return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }

        private static void ClearContentExceptTemplate(Transform content, TraderItemEntryUI template)
        {
            if (content == null || !Application.isPlaying)
                return;

            Transform templateTransform = template != null ? template.transform : null;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Transform child = content.GetChild(i);
                if (child == templateTransform)
                    continue;

                Destroy(child.gameObject);
            }
        }

        private static string GetObjectName(Object target)
        {
            return target != null ? target.name : "None";
        }

        private static int GetStockCount(TraderDataSO traderData)
        {
            return traderData != null && traderData.stock != null ? traderData.stock.Count : -1;
        }
    }
}

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

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            currentCommLevel = Mathf.Max(0, currentCommLevel);
            AutoBindReferences();
        }

        private void Awake()
        {
            AutoBindReferences();
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
            currentTraderId = traderId;
            TraderDataSO traderData = GetTraderData(traderId);

            if (traderNameText != null)
                traderNameText.text = GetTraderName(traderId, traderData);

            if (selectedTraderPortraitImage != null)
            {
                Sprite portrait = GetTraderPortrait(traderId);
                selectedTraderPortraitImage.sprite = portrait;
                selectedTraderPortraitImage.enabled = portrait != null;
            }

            RebuildBuyList(traderData);
            RebuildSellList();
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
            ClearContent(buyContent);

            if (buyContent == null)
            {
                Debug.LogWarning("[TraderPageView] buyContent가 연결되지 않았습니다. Content_BuyList를 연결해야 구매 리스트를 생성할 수 있습니다.", this);
                return;
            }

            if (itemEntryPrefab == null)
            {
                Debug.LogWarning("[TraderPageView] itemEntryPrefab이 연결되지 않았습니다. ItemEntry 프리팹을 연결해야 구매 리스트를 생성할 수 있습니다.", this);
                return;
            }

            if (traderData == null || traderData.stock == null)
            {
                Debug.LogWarning($"[TraderPageView] {currentTraderId} TraderDataSO 또는 stock 목록이 비어 있습니다.", this);
                return;
            }

            for (int i = 0; i < traderData.stock.Count; i++)
            {
                TraderItemEntryUI entryUI = Instantiate(itemEntryPrefab, buyContent);
                entryUI.SetupBuyEntry(traderData.stock[i], currentCommLevel, HandleBuyClicked);
            }
        }

        private void RebuildSellList()
        {
            ClearContent(sellContent);

            if (sellContent == null)
                return;

            Debug.LogWarning(
                "[TraderPageView] 판매 리스트를 생성하려면 현재 플레이어 인벤토리/보관함의 판매 가능 아이템 조회 API와 선택 아이템 판매 API가 필요합니다. 예: Stash/Inventory GetSellableItems, ConsumeItem 또는 RemoveItem, WalletSystem.Earn.",
                this);
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

            Debug.LogWarning(
                "[TraderPageView] 실제 구매 확정을 위해 Spawn된 TraderManager.BuyItemServerRpc(string itemID)와 서버 내부 WalletSystem.TryPay, Stash/Inventory TryAddItem API 연동이 필요합니다.",
                this);
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

            if (buyContent == null)
                buyContent = FindTransform("Content_BuyList", "BuyList", "Buy");

            if (sellContent == null)
                sellContent = FindTransform("Content_SellList", "SellList", "Sell");
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

        private static void ClearContent(Transform content)
        {
            if (content == null || !Application.isPlaying)
                return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }
    }
}

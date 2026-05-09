using DeadZone.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public sealed class TraderItemEntryUI : MonoBehaviour
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("아이템")]
#else
        [Header("아이템")]
#endif
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text priceText;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("버튼")]
#else
        [Header("버튼")]
#endif
        [FormerlySerializedAs("buyButton")]
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionButtonText;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Title("잠금")]
#else
        [Header("잠금")]
#endif
        [SerializeField] private GameObject lockRoot;
        [SerializeField] private GameObject lockIcon;
        [SerializeField] private TMP_Text lockReasonText;
        [SerializeField] private float fallbackEntryHeight = 72f;

        private TraderEntry currentEntry;
        private UnityAction<TraderEntry> actionClicked;

        private void Reset()
        {
            AutoBindReferences();
        }

        private void OnValidate()
        {
            AutoBindReferences();
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        public void SetupBuyEntry(TraderEntry entry, int currentCommLevel, UnityAction<TraderEntry> onBuyClicked)
        {
            AutoBindReferences();
            ApplyStableEntryLayout();
            ConfigureNonInteractiveRaycasts();

            currentEntry = entry;
            actionClicked = onBuyClicked;

            ItemDataSO item = entry.item;
            bool hasItem = item != null;
            bool isLocked = entry.requiredCommLevel > currentCommLevel;

            ApplyItemView(item, entry.basePrice);
            ApplyLockState(isLocked, currentCommLevel, entry.requiredCommLevel);
            ConfigureActionButton("구매", hasItem && !isLocked);

            gameObject.SetActive(true);
        }

        public void SetupSellEntry(TraderEntry entry, UnityAction<TraderEntry> onSellClicked)
        {
            AutoBindReferences();
            ApplyStableEntryLayout();
            ConfigureNonInteractiveRaycasts();

            currentEntry = entry;
            actionClicked = onSellClicked;

            ItemDataSO item = entry.item;
            bool hasItem = item != null;

            ApplyItemView(item, entry.basePrice);
            ApplyLockState(false, 0, 0);
            ConfigureActionButton("판매", hasItem);

            gameObject.SetActive(true);
        }

        private void ApplyItemView(ItemDataSO item, int price)
        {
            bool hasItem = item != null;

            if (itemIcon != null)
            {
                itemIcon.sprite = hasItem ? item.icon : null;
                itemIcon.enabled = hasItem && item.icon != null;
            }

            if (itemNameText != null)
                itemNameText.text = hasItem ? GetItemDisplayName(item) : "Unknown Item";

            if (priceText != null)
                priceText.text = price.ToString();
        }

        private void ApplyLockState(bool isLocked, int currentCommLevel, int requiredCommLevel)
        {
            if (lockRoot != null)
                lockRoot.SetActive(isLocked);

            if (lockIcon != null)
                lockIcon.SetActive(isLocked);

            if (lockReasonText == null)
                return;

            lockReasonText.gameObject.SetActive(isLocked);
            lockReasonText.text = isLocked
                ? $"거래 권한 부족\n현재 통신장비 Lv.{currentCommLevel}\n통신장비를 Lv.{requiredCommLevel}까지 업그레이드하면 구매할 수 있습니다."
                : string.Empty;
        }

        private void ConfigureActionButton(string buttonText, bool interactable)
        {
            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(true);
                actionButton.interactable = interactable;
                EnsureButtonRaycastTarget(actionButton);
                actionButton.onClick.RemoveAllListeners();
                actionButton.onClick.AddListener(HandleActionClicked);
            }

            if (actionButtonText != null)
                actionButtonText.text = buttonText;
        }

        private void HandleActionClicked()
        {
            actionClicked?.Invoke(currentEntry);
        }

        private void UnbindButtons()
        {
            if (actionButton != null)
                actionButton.onClick.RemoveAllListeners();
        }

        private void AutoBindReferences()
        {
            if (itemIcon == null)
                itemIcon = FindImage("Icon", "Item");

            if (itemNameText == null)
                itemNameText = FindText("Name", "Item");

            if (priceText == null)
                priceText = FindText("Price", "Cost");

            if (actionButton == null)
                actionButton = FindButton("Action", "Buy", "Sell", "구매", "판매");

            if (actionButtonText == null && actionButton != null)
                actionButtonText = actionButton.GetComponentInChildren<TMP_Text>(true);

            if (lockRoot == null)
                lockRoot = FindChildObject("Lock");

            if (lockIcon == null && lockRoot != null)
                lockIcon = FindChildObject(lockRoot.transform, "Icon", "Lock");

            if (lockReasonText == null && lockRoot != null)
                lockReasonText = lockRoot.GetComponentInChildren<TMP_Text>(true);
        }

        private void ApplyStableEntryLayout()
        {
            if (transform is RectTransform rectTransform)
            {
                LayoutElement layoutElement = GetComponent<LayoutElement>();
                if (layoutElement == null)
                    layoutElement = gameObject.AddComponent<LayoutElement>();

                float preferredHeight = rectTransform.rect.height > 0f ? rectTransform.rect.height : fallbackEntryHeight;
                layoutElement.minHeight = preferredHeight;
                layoutElement.preferredHeight = preferredHeight;
                layoutElement.flexibleHeight = 0f;
                layoutElement.flexibleWidth = 0f;
            }

            if (lockRoot != null && lockRoot.transform is RectTransform lockRect)
            {
                lockRect.anchorMin = Vector2.zero;
                lockRect.anchorMax = Vector2.one;
                lockRect.offsetMin = Vector2.zero;
                lockRect.offsetMax = Vector2.zero;
            }
        }

        private void ConfigureNonInteractiveRaycasts()
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                    continue;

                Button parentButton = image.GetComponentInParent<Button>(true);
                if (parentButton != null && parentButton.targetGraphic == image)
                    continue;

                image.raycastTarget = false;
            }

            if (lockRoot == null)
                return;

            Graphic[] lockGraphics = lockRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < lockGraphics.Length; i++)
            {
                if (lockGraphics[i] != null)
                    lockGraphics[i].raycastTarget = false;
            }
        }

        private static void EnsureButtonRaycastTarget(Button button)
        {
            if (button == null)
                return;

            Graphic graphic = button.targetGraphic != null ? button.targetGraphic : button.GetComponent<Graphic>();
            if (graphic == null)
            {
                Image image = button.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
                graphic = image;
            }

            graphic.raycastTarget = true;
            button.targetGraphic = graphic;
        }

        private Image FindImage(params string[] nameTokens)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.gameObject == gameObject)
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

        private Button FindButton(params string[] nameTokens)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                    continue;

                string lowerName = button.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return button;
                }
            }

            return null;
        }

        private GameObject FindChildObject(string nameToken)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                if (child.name.ToLowerInvariant().Contains(nameToken.ToLowerInvariant()))
                    return child.gameObject;
            }

            return null;
        }

        private static GameObject FindChildObject(Transform root, params string[] nameTokens)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == root)
                    continue;

                string lowerName = child.name.ToLowerInvariant();
                for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
                {
                    string token = nameTokens[tokenIndex];
                    if (!string.IsNullOrWhiteSpace(token) && lowerName.Contains(token.ToLowerInvariant()))
                        return child.gameObject;
                }
            }

            return null;
        }

        private static string GetItemDisplayName(ItemDataSO item)
        {
            if (item == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            return !string.IsNullOrWhiteSpace(item.itemID) ? item.itemID : item.name;
        }
    }
}

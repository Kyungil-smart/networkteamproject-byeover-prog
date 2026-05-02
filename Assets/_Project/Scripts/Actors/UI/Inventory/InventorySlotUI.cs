using DeadZone.Core;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public enum InventoryItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    public enum InventorySlotKind
    {
        Bag,
        EquipmentHead,
        EquipmentArmor,
        EquipmentBackpack,
        EquipmentPrimaryWeapon,
        EquipmentSecondaryWeapon,
        EquipmentMeleeWeapon,
        QuickSlot
    }

    public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [BoxGroup("슬롯 상태")]
        [ReadOnly]
        [Tooltip("인벤토리 그리드의 슬롯 인덱스입니다.")]
        [SerializeField] private int slotIndex;

        [BoxGroup("슬롯 상태")]
        [Tooltip("이 슬롯의 용도입니다. 자동 판별이 켜져 있으면 오브젝트 이름과 부모 패널명으로 설정됩니다.")]
        [SerializeField] private InventorySlotKind slotKind = InventorySlotKind.Bag;

        [BoxGroup("슬롯 상태")]
        [Tooltip("Awake/OnValidate에서 오브젝트 이름과 부모 패널명을 기준으로 슬롯 용도를 자동 판별합니다.")]
        [SerializeField] private bool autoDetectSlotKind = true;

        [BoxGroup("UI 연결")]
        [Tooltip("RarityMask 아래의 이미지입니다. 슬롯 마스크에 의해 잘리는 유일한 그래픽입니다.")]
        [SerializeField] private Image rarityBackground;

        [BoxGroup("UI 연결")]
        [Tooltip("희귀도 배경 위에 표시되는 아이템 아이콘 이미지입니다.")]
        [SerializeField] private Image iconImage;

        [BoxGroup("UI 연결")]
        [Tooltip("아이템 아이콘 위에 표시되는 중첩 개수 텍스트입니다.")]
        [SerializeField] private TMP_Text stackCountText;

        [BoxGroup("UI 연결")]
        [Tooltip("장비 슬롯이 비어 있을 때만 표시되는 기본 슬롯 아이콘입니다.")]
        [SerializeField] private GameObject emptySlotIcon;

        [BoxGroup("UI 연결")]
        [Tooltip("슬롯이 잠겨 있을 때 슬롯 콘텐츠 위에 표시되는 오버레이입니다.")]
        [SerializeField] private GameObject lockOverlay;

        [BoxGroup("희귀도 배경")]
        [SerializeField] private Sprite commonBackground;

        [BoxGroup("희귀도 배경")]
        [SerializeField] private Sprite uncommonBackground;

        [BoxGroup("희귀도 배경")]
        [SerializeField] private Sprite rareBackground;

        [BoxGroup("희귀도 배경")]
        [SerializeField] private Sprite epicBackground;

        [BoxGroup("희귀도 배경")]
        [SerializeField] private Sprite legendaryBackground;

        [BoxGroup("희귀도 색상")]
        [SerializeField] private Color commonColor = new Color(0.42f, 0.42f, 0.42f, 0.75f);

        [BoxGroup("희귀도 색상")]
        [SerializeField] private Color uncommonColor = new Color(0.16f, 0.62f, 0.26f, 0.8f);

        [BoxGroup("희귀도 색상")]
        [SerializeField] private Color rareColor = new Color(0.16f, 0.38f, 0.88f, 0.8f);

        [BoxGroup("희귀도 색상")]
        [SerializeField] private Color epicColor = new Color(0.62f, 0.25f, 0.86f, 0.8f);

        [BoxGroup("희귀도 색상")]
        [SerializeField] private Color legendaryColor = new Color(0.95f, 0.55f, 0.12f, 0.85f);

        [BoxGroup("툴팁")]
        [Tooltip("아이템이 들어있는 슬롯에 마우스를 올렸을 때 표시할 툴팁 UI입니다.")]
        [SerializeField] private ItemTooltipUI tooltipUI;

        [BoxGroup("툴팁")]
        [ReadOnly]
        [Tooltip("현재 슬롯에 들어있는 아이템 ScriptableObject입니다.")]
        [SerializeField] private ScriptableObject currentItemData;

        [BoxGroup("툴팁")]
        [ReadOnly]
        [Tooltip("현재 슬롯에 들어있는 아이템 중첩 개수입니다.")]
        [SerializeField] private int currentStackCount;

        [BoxGroup("디버그")]
        [Tooltip("툴팁 문제를 확인하기 위해 포인터와 툴팁 상태를 로그로 출력합니다.")]
        [SerializeField] private bool debugTooltipEvents = true;

        private static GameObject dragIconObject;
        private static RectTransform dragIconRect;
        private static Canvas dragCanvas;
        private static InventorySlotUI draggingSlot;

        private bool isLocked;

        public int SlotIndex => slotIndex;
        public InventorySlotKind SlotKind => slotKind;
        public ItemDataSO CurrentItemData => currentItemData as ItemDataSO;
        public int CurrentStackCount => currentStackCount;
        public bool HasItem => CurrentItemData != null && currentStackCount > 0;

        private void Awake()
        {
            AutoBindReferences();
            ConfigureSlotKind();
            EnsureRaycastTarget();
        }

        private void OnValidate()
        {
            ConfigureSlotKind();
        }

        public void Initialize(int index)
        {
            slotIndex = index;
            AutoBindReferences();
            ClearItem();
            ConfigureSlotKind();
            EnsureRaycastTarget();
        }

        public void PrepareDropSlot(ItemTooltipUI tooltip, int index = -1)
        {
            if (index >= 0)
                slotIndex = index;

            tooltipUI = tooltip;
            AutoBindReferences();
            ConfigureSlotKind();
            EnsureRaycastTarget();
        }

        public void SetTooltip(ItemTooltipUI tooltip)
        {
            tooltipUI = tooltip;
        }

        public void SetItem(ItemDataSO itemData, int stackCount)
        {
            if (itemData == null || stackCount <= 0)
            {
                ClearItem();
                return;
            }

            currentItemData = itemData;
            currentStackCount = Mathf.Clamp(stackCount, 1, GetMaxStack(itemData));

            SetItem(itemData.icon, ToInventoryRarity(itemData.rarity), currentStackCount);
            SetEmptySlotIconVisible(false);
        }

        public void SetItem(Sprite icon, InventoryItemRarity rarity, int stackCount)
        {
            SetRarityBackground(rarity);
            SetIcon(icon);
            SetStackCount(stackCount);
            SetEmptySlotIconVisible(icon == null || stackCount <= 0);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (debugTooltipEvents)
                Debug.Log($"[InventorySlotUI] Pointer Enter: {name}, Item={currentItemData}, Stack={currentStackCount}, Tooltip={tooltipUI}", this);

            if (tooltipUI == null)
            {
                Debug.LogWarning("[InventorySlotUI] Tooltip UI가 연결되지 않았습니다.", this);
                return;
            }

            if (currentItemData == null)
            {
                if (debugTooltipEvents)
                    Debug.LogWarning("[InventorySlotUI] currentItemData가 null입니다.", this);

                return;
            }

            tooltipUI.Show(currentItemData, currentStackCount, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (debugTooltipEvents)
                Debug.Log($"[InventorySlotUI] Pointer Exit: {name}, Tooltip={tooltipUI}", this);

            if (tooltipUI == null)
                return;

            tooltipUI.Hide();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!HasItem || isLocked)
                return;

            draggingSlot = this;
            if (tooltipUI != null)
                tooltipUI.Hide();

            CreateDragIcon(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (draggingSlot != this || dragIconRect == null)
                return;

            MoveDragIcon(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (draggingSlot == this)
            {
                draggingSlot = null;
                DestroyDragIcon();
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (draggingSlot == null || draggingSlot == this)
                return;

            TryReceiveDrop(draggingSlot);
        }

        public void ClearItem()
        {
            currentItemData = null;
            currentStackCount = 0;

            if (rarityBackground != null)
            {
                rarityBackground.sprite = null;
                rarityBackground.color = Color.clear;
                rarityBackground.enabled = false;
                rarityBackground.gameObject.SetActive(false);
            }

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
                iconImage.gameObject.SetActive(false);
            }

            if (stackCountText != null)
            {
                stackCountText.text = string.Empty;
                stackCountText.gameObject.SetActive(false);
            }

            SetEmptySlotIconVisible(true);
        }

        public void SetLocked(bool locked)
        {
            isLocked = locked;

            if (lockOverlay == gameObject)
            {
                Debug.LogError($"[InventorySlotUI] {name}의 LockOverlay에 슬롯 루트가 연결되어 있습니다.", this);
                return;
            }

            if (lockOverlay != null)
                lockOverlay.SetActive(locked);
        }

        private bool TryReceiveDrop(InventorySlotUI source)
        {
            if (source == null || source == this || source.isLocked || isLocked || !source.HasItem)
                return false;

            ItemDataSO sourceItem = source.CurrentItemData;
            int sourceCount = source.CurrentStackCount;

            if (!CanAccept(sourceItem))
            {
                Debug.LogWarning($"[InventorySlotUI] {name} 슬롯에는 {sourceItem.displayName} 아이템을 넣을 수 없습니다.", this);
                return false;
            }

            if (!HasItem)
            {
                SetItem(sourceItem, sourceCount);
                source.ClearItem();
                return true;
            }

            ItemDataSO targetItem = CurrentItemData;
            int targetCount = CurrentStackCount;

            if (IsSameStackableItem(targetItem, sourceItem))
            {
                int maxStack = GetMaxStack(targetItem);
                int available = maxStack - targetCount;

                if (available <= 0)
                    return false;

                int moved = Mathf.Min(available, sourceCount);
                SetItem(targetItem, targetCount + moved);

                int remaining = sourceCount - moved;
                if (remaining > 0)
                    source.SetItem(sourceItem, remaining);
                else
                    source.ClearItem();

                return true;
            }

            if (!source.CanAccept(targetItem))
            {
                Debug.LogWarning($"[InventorySlotUI] {source.name} 슬롯에는 {targetItem.displayName} 아이템을 넣을 수 없습니다.", source);
                return false;
            }

            SetItem(sourceItem, sourceCount);
            source.SetItem(targetItem, targetCount);
            return true;
        }

        private bool CanAccept(ItemDataSO itemData)
        {
            if (itemData == null)
                return false;

            return slotKind switch
            {
                InventorySlotKind.Bag => true,
                InventorySlotKind.QuickSlot => itemData.category != ItemCategory.Weapon && itemData is not WeaponDataSO,
                InventorySlotKind.EquipmentHead => itemData.category == ItemCategory.Helmet || itemData is HelmetDataSO,
                InventorySlotKind.EquipmentArmor => itemData.category == ItemCategory.Armor || itemData is ArmorDataSO,
                InventorySlotKind.EquipmentBackpack => itemData.category == ItemCategory.Backpack || itemData is BackpackDataSO,
                InventorySlotKind.EquipmentPrimaryWeapon => IsPrimaryWeapon(itemData),
                InventorySlotKind.EquipmentSecondaryWeapon => IsSecondaryWeapon(itemData),
                InventorySlotKind.EquipmentMeleeWeapon => IsMeleeWeapon(itemData),
                _ => false
            };
        }

        private void ConfigureSlotKind()
        {
            if (!autoDetectSlotKind)
                return;

            string path = GetHierarchyPath(transform).ToLowerInvariant();
            string objectName = name.ToLowerInvariant();

            if (path.Contains("quickslotpanel"))
            {
                slotKind = InventorySlotKind.QuickSlot;
                return;
            }

            if (path.Contains("equipmentpanel"))
            {
                if (objectName.Contains("head") || objectName.Contains("helmet"))
                    slotKind = InventorySlotKind.EquipmentHead;
                else if (objectName.Contains("armor") || objectName.Contains("torso"))
                    slotKind = InventorySlotKind.EquipmentArmor;
                else if (objectName.Contains("backpack") || objectName.Contains("bag"))
                    slotKind = InventorySlotKind.EquipmentBackpack;
                else if (objectName.Contains("secondary"))
                    slotKind = InventorySlotKind.EquipmentSecondaryWeapon;
                else if (objectName.Contains("melee"))
                    slotKind = InventorySlotKind.EquipmentMeleeWeapon;
                else if (objectName.Contains("primary") || objectName.Contains("weapon"))
                    slotKind = InventorySlotKind.EquipmentPrimaryWeapon;
                return;
            }

            slotKind = InventorySlotKind.Bag;
        }

        private void AutoBindReferences()
        {
            if (rarityBackground == null)
                rarityBackground = FindImageReference("rarity", "grade", "background", "bg");

            if (iconImage == null)
                iconImage = FindImageReference("icon", "item");

            if (stackCountText == null)
                stackCountText = GetComponentInChildren<TMP_Text>(true);

            if (emptySlotIcon == null)
                emptySlotIcon = FindEmptySlotIcon();
        }

        private Image FindImageReference(params string[] nameTokens)
        {
            Image[] images = GetComponentsInChildren<Image>(true);

            foreach (Image image in images)
            {
                if (image == null || image.gameObject == gameObject)
                    continue;

                string lowerName = image.name.ToLowerInvariant();
                foreach (string token in nameTokens)
                {
                    if (lowerName.Contains(token))
                        return image;
                }
            }

            foreach (Image image in images)
            {
                if (image != null && image.gameObject != gameObject)
                    return image;
            }

            return null;
        }

        private GameObject FindEmptySlotIcon()
        {
            Image[] images = GetComponentsInChildren<Image>(true);

            foreach (Image image in images)
            {
                if (image == null || image == iconImage || image == rarityBackground)
                    continue;

                GameObject imageObject = image.gameObject;
                if (imageObject == gameObject || imageObject == lockOverlay)
                    continue;

                if (lockOverlay != null && imageObject.transform.IsChildOf(lockOverlay.transform))
                    continue;

                string lowerName = imageObject.name.ToLowerInvariant();
                if (lowerName.Contains("lock") || lowerName.Contains("background") || lowerName.Contains("rarity"))
                    continue;

                if (lowerName.StartsWith("icon_") && !lowerName.Contains("item"))
                    return imageObject;
            }

            return null;
        }

        private void CreateDragIcon(PointerEventData eventData)
        {
            DestroyDragIcon();

            if (iconImage == null || iconImage.sprite == null)
                return;

            dragCanvas = GetComponentInParent<Canvas>();
            if (dragCanvas == null)
                return;

            dragIconObject = new GameObject("Drag_ItemIcon", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            dragIconObject.transform.SetParent(dragCanvas.transform, false);
            dragIconObject.transform.SetAsLastSibling();

            dragIconRect = dragIconObject.GetComponent<RectTransform>();
            dragIconRect.sizeDelta = (transform as RectTransform)?.rect.size ?? new Vector2(64f, 64f);

            CanvasGroup canvasGroup = dragIconObject.GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            canvasGroup.alpha = 0.8f;

            Image dragImage = dragIconObject.GetComponent<Image>();
            dragImage.sprite = iconImage.sprite;
            dragImage.preserveAspect = true;
            dragImage.raycastTarget = false;

            MoveDragIcon(eventData.position);
        }

        private void MoveDragIcon(Vector2 screenPosition)
        {
            if (dragIconRect == null || dragCanvas == null)
                return;

            RectTransform canvasRect = dragCanvas.transform as RectTransform;
            Camera eventCamera = dragCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : dragCanvas.worldCamera;

            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
                dragIconRect.anchoredPosition = localPoint;
        }

        private static void DestroyDragIcon()
        {
            if (dragIconObject != null)
                Destroy(dragIconObject);

            dragIconObject = null;
            dragIconRect = null;
            dragCanvas = null;
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null)
                return;

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            iconImage.gameObject.SetActive(icon != null);
        }

        private void SetEmptySlotIconVisible(bool visible)
        {
            if (emptySlotIcon == null || emptySlotIcon == gameObject)
                return;

            emptySlotIcon.SetActive(visible);
        }

        private void SetStackCount(int stackCount)
        {
            if (stackCountText == null)
                return;

            bool showStackCount = stackCount >= 2;

            stackCountText.text = showStackCount
                ? Mathf.Clamp(stackCount, 2, 99).ToString()
                : string.Empty;

            stackCountText.gameObject.SetActive(showStackCount);
        }

        private void SetRarityBackground(InventoryItemRarity rarity)
        {
            if (rarityBackground == null)
                return;

            Sprite backgroundSprite = GetRarityBackgroundSprite(rarity);

            rarityBackground.sprite = backgroundSprite;
            rarityBackground.color = GetRarityBackgroundColor(rarity);
            rarityBackground.enabled = true;
            rarityBackground.raycastTarget = false;
            rarityBackground.gameObject.SetActive(true);
        }

        private Sprite GetRarityBackgroundSprite(InventoryItemRarity rarity)
        {
            return rarity switch
            {
                InventoryItemRarity.Common => commonBackground,
                InventoryItemRarity.Uncommon => uncommonBackground,
                InventoryItemRarity.Rare => rareBackground,
                InventoryItemRarity.Epic => epicBackground,
                InventoryItemRarity.Legendary => legendaryBackground,
                _ => commonBackground
            };
        }

        private Color GetRarityBackgroundColor(InventoryItemRarity rarity)
        {
            return rarity switch
            {
                InventoryItemRarity.Common => commonColor,
                InventoryItemRarity.Uncommon => uncommonColor,
                InventoryItemRarity.Rare => rareColor,
                InventoryItemRarity.Epic => epicColor,
                InventoryItemRarity.Legendary => legendaryColor,
                _ => commonColor
            };
        }

        private void EnsureRaycastTarget()
        {
            Image slotImage = GetComponent<Image>();
            if (slotImage == null)
            {
                slotImage = gameObject.AddComponent<Image>();
                slotImage.color = new Color(1f, 1f, 1f, 0f);
            }

            slotImage.raycastTarget = true;

            if (iconImage != null)
                iconImage.raycastTarget = false;

            if (rarityBackground != null)
                rarityBackground.raycastTarget = false;

            if (stackCountText != null)
                stackCountText.raycastTarget = false;

            if (lockOverlay != null)
            {
                foreach (Graphic graphic in lockOverlay.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }
        }

        private static bool IsSameStackableItem(ItemDataSO a, ItemDataSO b)
        {
            if (a == null || b == null || a != b)
                return false;

            return GetMaxStack(a) > 1;
        }

        private static int GetMaxStack(ItemDataSO itemData)
        {
            if (itemData == null)
                return 1;

            return Mathf.Max(1, itemData.maxStackSize);
        }

        private static bool IsPrimaryWeapon(ItemDataSO itemData)
        {
            if (itemData is not WeaponDataSO weaponData)
                return false;

            return weaponData.weaponCategory != WeaponCategory.Handgun && weaponData.weaponCategory != WeaponCategory.Melee;
        }

        private static bool IsSecondaryWeapon(ItemDataSO itemData)
        {
            return itemData is WeaponDataSO weaponData && weaponData.weaponCategory == WeaponCategory.Handgun;
        }

        private static bool IsMeleeWeapon(ItemDataSO itemData)
        {
            return itemData is WeaponDataSO weaponData && weaponData.weaponCategory == WeaponCategory.Melee;
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            string path = target.name;
            Transform parent = target.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static InventoryItemRarity ToInventoryRarity(RarityTier rarity)
        {
            return rarity switch
            {
                RarityTier.Common => InventoryItemRarity.Common,
                RarityTier.Uncommon => InventoryItemRarity.Uncommon,
                RarityTier.Rare => InventoryItemRarity.Rare,
                RarityTier.Epic => InventoryItemRarity.Epic,
                RarityTier.Legendary => InventoryItemRarity.Legendary,
                _ => InventoryItemRarity.Common
            };
        }

#if UNITY_EDITOR
        [BoxGroup("디버그")]
        [Button("잠금")]
        private void TestLock()
        {
            SetLocked(true);
        }

        [BoxGroup("디버그")]
        [Button("잠금 해제")]
        private void TestUnlock()
        {
            SetLocked(false);
        }
#endif
    }
}

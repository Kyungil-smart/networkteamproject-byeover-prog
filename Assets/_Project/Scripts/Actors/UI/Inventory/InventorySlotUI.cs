using DeadZone.Core;
using DeadZone.Actors;
using DeadZone.Systems.Save;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
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

    public interface IInventorySlotDropHandler
    {
        bool TryHandleDrop(InventorySlotUI source, InventorySlotUI target);
    }

    public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [BoxGroup("슬롯 상태")]
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

        [BoxGroup("툴팁")]
        [Tooltip("아이템이 들어있는 슬롯에 마우스를 올렸을 때 표시할 툴팁 UI입니다.")]
        [SerializeField] private ItemTooltipUI tooltipUI;

        [BoxGroup("툴팁")]
        [Tooltip("현재 슬롯에 들어있는 아이템 ScriptableObject입니다.")]
        [SerializeField] private ScriptableObject currentItemData;

        [BoxGroup("툴팁")]
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
        private InventoryUI ownerInventoryUI;

        public int SlotIndex => slotIndex;
        public InventorySlotKind SlotKind => slotKind;
        public ItemDataSO CurrentItemData => currentItemData as ItemDataSO;
        public int CurrentStackCount => currentStackCount;
        public bool HasItem => CurrentItemData != null && currentStackCount > 0;

        public void PrepareForSaveSnapshot()
        {
            AutoBindReferences();
            ConfigureSlotKind();
        }

        public string GetEquipmentSaveSlotId()
        {
            ConfigureSlotKind();

            return slotKind switch
            {
                InventorySlotKind.EquipmentHead => "EquipmentHead",
                InventorySlotKind.EquipmentArmor => "EquipmentArmor",
                InventorySlotKind.EquipmentBackpack => "EquipmentBackpack",
                InventorySlotKind.EquipmentSecondaryWeapon => "EquipmentSecondaryWeapon",
                InventorySlotKind.EquipmentMeleeWeapon => "EquipmentMeleeWeapon",
                InventorySlotKind.EquipmentPrimaryWeapon => IsPrimary2Slot() ? "Primary2" : "EquipmentPrimaryWeapon",
                _ => string.Empty
            };
        }

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

        public void PrepareDropSlot(ItemTooltipUI tooltip, int index = -1, InventoryUI inventoryUI = null)
        {
            if (index >= 0)
                slotIndex = index;

            tooltipUI = tooltip;
            if (inventoryUI != null)
                ownerInventoryUI = inventoryUI;

            AutoBindReferences();
            ConfigureSlotKind();
            EnsureRaycastTarget();
        }

        public void PrepareDropSlotAsKind(
            ItemTooltipUI tooltip,
            InventorySlotKind kind,
            int index = -1,
            InventoryUI inventoryUI = null)
        {
            if (index >= 0)
                slotIndex = index;

            slotKind = kind;
            tooltipUI = tooltip;
            if (inventoryUI != null)
                ownerInventoryUI = inventoryUI;

            AutoBindReferences();
            EnsureRaycastTarget();
        }

        public void SetTooltip(ItemTooltipUI tooltip)
        {
            tooltipUI = tooltip;
            EnsureRaycastTarget();
        }

        public void CopyRarityBackgroundSpritesFrom(InventorySlotUI source)
        {
            if (source == null || source == this)
                return;

            commonBackground = source.commonBackground;
            uncommonBackground = source.uncommonBackground;
            rareBackground = source.rareBackground;
            epicBackground = source.epicBackground;
            legendaryBackground = source.legendaryBackground;
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
            if (debugTooltipEvents && HasItem)
                Debug.Log($"[InventorySlotUI] Pointer Enter: {name}, Item={currentItemData}, Stack={currentStackCount}, Tooltip={tooltipUI}", this);

            if (!HasItem)
            {
                WarnIfVisibleContentHasNoItemData("툴팁 표시");
                return;
            }

            if (tooltipUI == null)
            {
                Debug.LogWarning("[InventorySlotUI] Tooltip UI가 연결되지 않았습니다.", this);
                return;
            }

            tooltipUI.Show(currentItemData, currentStackCount, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltipUI == null)
                return;

            tooltipUI.Hide();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right)
                return;

            if (!TryUseCurrentItem())
                return;

            eventData.Use();
        }

        public bool TryUseCurrentItem()
        {
            return TryUseMedicalItem();
        }

        private bool TryUseMedicalItem()
        {
            if (!HasItem || CurrentItemData.category != ItemCategory.Med)
                return false;

            LootContainerSlotUI containerSlot = GetComponent<LootContainerSlotUI>();
            if (containerSlot != null && (containerSlot.Container != null || containerSlot.DroppedItem != null || containerSlot.CorpseInventory != null))
                return false;

            GridInventory inventory = ResolveLocalPlayerInventory();
            if (inventory == null)
            {
                Debug.LogWarning("[InventorySlotUI] Medical item use failed. Local player GridInventory was not found.", this);
                return false;
            }

            // 퀵슬롯은 아이템 바로가기만 들고 있으므로, 실제 인벤토리에 아이템이 남아 있는지 먼저 확인한다.
            if (!inventory.HasItem(CurrentItemData.itemID, 1))
            {
                Debug.LogWarning($"[InventorySlotUI] Medical item use failed. Item is not in the local inventory. itemId={CurrentItemData.itemID}", this);
                return false;
            }

            inventory.RequestUseMedicalItem(CurrentItemData.itemID);
            ApplyQuickSlotUsePreview();
            return true;
        }

        private void ApplyQuickSlotUsePreview()
        {
            if (slotKind != InventorySlotKind.QuickSlot)
                return;

            // 서버 인벤토리 변경 이벤트가 도착하기 전에도 사용한 퀵슬롯이 즉시 줄어든 것처럼 보이게 한다.
            // 최종 수량은 GridInventory 동기화가 다시 맞춘다.
            if (currentStackCount <= 1)
            {
                ClearItem();
                return;
            }

            SetItem(CurrentItemData, currentStackCount - 1);
        }

        private static GridInventory ResolveLocalPlayerInventory()
        {
            NetworkObject playerObject = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClient?.PlayerObject
                : null;

            if (playerObject != null)
            {
                GridInventory inventory = playerObject.GetComponent<GridInventory>();
                if (inventory != null)
                    return inventory;
            }

            return FindFirstObjectByType<GridInventory>(FindObjectsInactive.Include);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (isLocked)
            {
                if (debugTooltipEvents)
                    Debug.LogWarning($"[InventorySlotUI] {name} 슬롯은 잠겨 있어서 드래그할 수 없습니다.", this);

                return;
            }

            if (!HasItem)
            {
                WarnIfVisibleContentHasNoItemData("드래그 시작");
                return;
            }

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
                if (HasItem && !IsPointerOverValidDropTarget(eventData))
                    TryRequestWorldDrop();

                draggingSlot = null;
                DestroyDragIcon();
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (draggingSlot == null || draggingSlot == this)
                return;

            if (TryHandleExternalDrop(draggingSlot, this))
                return;

            bool received = TryReceiveDrop(draggingSlot);
            if (debugTooltipEvents)
            {
                string sourceItemId = draggingSlot.CurrentItemData != null ? draggingSlot.CurrentItemData.itemID : "null";
                Debug.Log($"[InventorySlotUI] Drop Result: source={draggingSlot.name}, target={name}, sourceItem={sourceItemId}, targetKind={slotKind}, success={received}", this);
            }
        }

        private static bool TryHandleExternalDrop(InventorySlotUI source, InventorySlotUI target)
        {
            if (source == null || target == null)
                return false;

            foreach (IInventorySlotDropHandler handler in target.GetComponents<IInventorySlotDropHandler>())
            {
                if (handler != null && handler.TryHandleDrop(source, target))
                    return true;
            }

            foreach (IInventorySlotDropHandler handler in source.GetComponents<IInventorySlotDropHandler>())
            {
                if (handler != null && handler.TryHandleDrop(source, target))
                    return true;
            }

            return false;
        }

        private bool IsPointerOverValidDropTarget(PointerEventData eventData)
        {
            if (eventData == null)
                return false;

            if (IsPointerOverThisSlot(eventData))
                return true;

            if (IsValidDropTargetObject(eventData.pointerCurrentRaycast.gameObject))
                return true;

            if (IsValidDropTargetObject(eventData.pointerEnter))
                return true;

            if (EventSystem.current != null)
            {
                List<RaycastResult> raycastResults = new();
                EventSystem.current.RaycastAll(eventData, raycastResults);
                for (int i = 0; i < raycastResults.Count; i++)
                {
                    if (IsValidDropTargetObject(raycastResults[i].gameObject))
                        return true;
                }
            }

            return false;
        }

        private bool IsValidDropTargetObject(GameObject targetObject)
        {
            if (targetObject == null)
                return false;

            InventorySlotUI targetSlot = targetObject.GetComponentInParent<InventorySlotUI>();
            if (targetSlot != null)
                return targetSlot != this;

            return targetObject.GetComponentInParent<IInventorySlotDropHandler>() != null;
        }

        private bool IsPointerOverThisSlot(PointerEventData eventData)
        {
            RectTransform rectTransform = transform as RectTransform;
            return rectTransform != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(
                       rectTransform,
                       eventData.position,
                       eventData.pressEventCamera);
        }

        private void TryRequestWorldDrop()
        {
            if (!HasItem || IsContainerSourceSlot() || IsLobbyInventorySlot())
                return;

            GridInventory inventory = ResolveOwnerGridInventory();
            if (inventory == null || !inventory.IsSpawned || !inventory.IsOwner)
                return;

            if (slotKind == InventorySlotKind.Bag)
            {
                int x = Mathf.Max(0, slotIndex) % GridInventory.BASE_WIDTH;
                int y = Mathf.Max(0, slotIndex) / GridInventory.BASE_WIDTH;
                inventory.DropInventorySlotServerRpc((byte)x, (byte)y);
                return;
            }

            if (TryGetEquipmentTargetSlot(out EquipmentTargetSlot equipmentTargetSlot))
                inventory.DropEquipmentSlotServerRpc(equipmentTargetSlot);
        }

        private bool IsContainerSourceSlot()
        {
            return GetComponent<LootContainerSlotUI>() != null;
        }

        private bool IsLobbyInventorySlot()
        {
            return GetComponentInParent<LobbyPlayerInventoryUI>(true) != null ||
                   GetComponentInParent<StashGridUI>(true) != null ||
                   GetComponentInParent<LobbyInventoryStateUiBridge>(true) != null;
        }

        private bool TryGetEquipmentTargetSlot(out EquipmentTargetSlot targetSlot)
        {
            targetSlot = EquipmentTargetSlot.None;

            switch (slotKind)
            {
                case InventorySlotKind.EquipmentHead:
                    targetSlot = EquipmentTargetSlot.Head;
                    return true;

                case InventorySlotKind.EquipmentBackpack:
                    targetSlot = EquipmentTargetSlot.Backpack;
                    return true;

                case InventorySlotKind.EquipmentArmor:
                    targetSlot = EquipmentTargetSlot.Armor;
                    return true;

                case InventorySlotKind.EquipmentPrimaryWeapon:
                    targetSlot = IsPrimary2Slot() ? EquipmentTargetSlot.Primary2 : EquipmentTargetSlot.Primary1;
                    return true;

                case InventorySlotKind.EquipmentSecondaryWeapon:
                    targetSlot = EquipmentTargetSlot.Secondary;
                    return true;

                case InventorySlotKind.EquipmentMeleeWeapon:
                    targetSlot = EquipmentTargetSlot.Melee;
                    return true;

                default:
                    return false;
            }
        }

        private static GridInventory ResolveOwnerGridInventory()
        {
            GridInventory[] candidates = FindObjectsByType<GridInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GridInventory candidate in candidates)
            {
                if (candidate != null && candidate.IsSpawned && candidate.IsOwner)
                    return candidate;
            }

            foreach (GridInventory candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                    return candidate;
            }

            return null;
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
            {
                if (debugTooltipEvents)
                {
                    Debug.LogWarning($"[InventorySlotUI] Drop blocked: source={source}, sameSlot={source == this}, sourceLocked={source != null && source.isLocked}, targetLocked={isLocked}, sourceHasItem={source != null && source.HasItem}", this);
                }

                return false;
            }

            ItemDataSO sourceItem = source.CurrentItemData;
            int sourceCount = source.CurrentStackCount;

            if (slotKind == InventorySlotKind.QuickSlot)
                return TryAssignQuickSlotShortcut(source, sourceItem, sourceCount);

            if (!CanAccept(sourceItem))
            {
                Debug.LogWarning($"[InventorySlotUI] {name} 슬롯에는 {sourceItem.displayName} 아이템을 넣을 수 없습니다. slotKind={slotKind}, itemType={sourceItem.GetType().Name}", this);
                return false;
            }

            if (!HasItem)
            {
                if (slotKind == InventorySlotKind.Bag &&
                    source.TryGetEquipmentTargetSlot(out EquipmentTargetSlot sourceEquipmentSlot) &&
                    TryRequestEquipmentMoveToInventory(sourceEquipmentSlot, slotIndex))
                {
                    return true;
                }

                if (!TrySyncEquipmentSlotAfterDrop(this, sourceItem) ||
                    !TrySyncEquipmentSlotAfterDrop(source, null))
                {
                    return false;
                }

                SetItem(sourceItem, sourceCount);
                source.ClearItem();
                CaptureLobbyInventoryStateIfPresent(this, source);
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

                CaptureLobbyInventoryStateIfPresent(this, source);
                return true;
            }

            if (!source.CanAccept(targetItem))
            {
                Debug.LogWarning($"[InventorySlotUI] {source.name} 슬롯에는 {targetItem.displayName} 아이템을 넣을 수 없습니다.", source);
                return false;
            }

            if (!TrySyncEquipmentSlotAfterDrop(this, sourceItem) ||
                !TrySyncEquipmentSlotAfterDrop(source, targetItem))
            {
                return false;
            }

            SetItem(sourceItem, sourceCount);
            source.SetItem(targetItem, targetCount);
            CaptureLobbyInventoryStateIfPresent(this, source);
            return true;
        }

        private static bool TryRequestEquipmentMoveToInventory(EquipmentTargetSlot sourceEquipmentSlot, int targetSlotIndex)
        {
            GridInventory inventory = ResolveOwnerGridInventory();
            if (inventory == null || !inventory.IsSpawned || !inventory.IsOwner)
                return false;

            byte gridX = (byte)(Mathf.Max(0, targetSlotIndex) % GridInventory.BASE_WIDTH);
            byte gridY = (byte)(Mathf.Max(0, targetSlotIndex) / GridInventory.BASE_WIDTH);
            inventory.RequestMoveEquipmentSlotToInventory(sourceEquipmentSlot, gridX, gridY);
            return true;
        }

        private bool TryAssignQuickSlotShortcut(InventorySlotUI source, ItemDataSO sourceItem, int sourceCount)
        {
            if (source == null || sourceItem == null || sourceCount <= 0)
                return false;

            if (!CanAccept(sourceItem))
                return false;

            if (source.slotKind == InventorySlotKind.QuickSlot)
            {
                if (!HasItem)
                {
                    SetItem(sourceItem, sourceCount);
                    source.ClearItem();
                    CaptureLobbyInventoryStateIfPresent(this, source);
                    return true;
                }

                ItemDataSO targetItem = CurrentItemData;
                int targetCount = CurrentStackCount;
                SetItem(sourceItem, sourceCount);
                source.SetItem(targetItem, targetCount);
                CaptureLobbyInventoryStateIfPresent(this, source);
                return true;
            }

            if (HasItem)
            {
                ItemDataSO targetItem = CurrentItemData;
                int targetCount = CurrentStackCount;

                if (!source.CanAccept(targetItem))
                    return false;

                SetItem(sourceItem, sourceCount);
                source.SetItem(targetItem, targetCount);
                CaptureLobbyInventoryStateIfPresent(this, source);
                return true;
            }

            SetItem(sourceItem, sourceCount);
            source.ClearItem();
            CaptureLobbyInventoryStateIfPresent(this, source);
            return true;
        }

        private bool IsStashLikeSlot()
        {
            string path = GetHierarchyPath(transform).ToLowerInvariant();
            string objectName = name.ToLowerInvariant();

            return path.Contains("stashpanel") ||
                   path.Contains("stashslot") ||
                   objectName.Contains("stashslot");
        }

        private static void CaptureLobbyInventoryStateIfPresent(params InventorySlotUI[] changedSlots)
        {
            LobbyInventoryStateUiBridge bridge =
                FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);

            if (bridge == null)
                return;

            bridge.CaptureChangedItemSlots(changedSlots);
            bridge.CaptureChangedEquipmentSlots(changedSlots);

            LobbySaveService saveService =
                FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);

            saveService?.SaveCurrentStateToLocalJson("Inventory UI drop snapshot");
        }

        private static bool TrySyncEquipmentSlotAfterDrop(InventorySlotUI slot, ItemDataSO nextItem)
        {
            if (slot == null)
                return true;

            if (slot.slotKind == InventorySlotKind.EquipmentBackpack)
                return TrySyncBackpackSlotAfterDrop(slot, nextItem);

            if (!slot.TryGetWeaponSlot(out WeaponSlot weaponSlot))
                return true;

            if (nextItem == null)
            {
                Debug.Log($"[InventorySlotUI] 장비 슬롯 해제 요청: slot={slot.name}, weaponSlot={weaponSlot}", slot);
                return TryClearWeaponSlot(weaponSlot, slot);
            }

            if (nextItem is not WeaponDataSO weaponData)
            {
                Debug.LogWarning($"[InventorySlotUI] {slot.name} 장비 슬롯에는 WeaponDataSO만 서버 장착할 수 있습니다.", slot);
                return false;
            }

            Debug.Log($"[InventorySlotUI] 장비 슬롯 장착 요청: slot={slot.name}, weaponSlot={weaponSlot}, itemID={weaponData.itemID}, category={weaponData.weaponCategory}", slot);
            return TryEquipWeaponSlot(weaponSlot, weaponData, slot);
        }

        private static bool TrySyncBackpackSlotAfterDrop(InventorySlotUI slot, ItemDataSO nextItem)
        {
            if (slot == null)
                return true;

            InventoryUI inventoryUI = ResolveInventoryUI(slot);
            LobbyPlayerInventoryUI lobbyPlayerInventoryUI = ResolveLobbyPlayerInventoryUI();

            if (nextItem == null)
            {
                inventoryUI?.SetBagLevel(0);
                lobbyPlayerInventoryUI?.SetBagLevel(0);
                return TryEquipBackpackSlot(string.Empty, slot);
            }

            if (nextItem is not BackpackDataSO backpackData)
            {
                Debug.LogWarning($"[InventorySlotUI] {slot.name} 장비 슬롯에는 BackpackDataSO만 넣을 수 있습니다.", slot);
                return false;
            }

            inventoryUI?.SetBagLevel(backpackData.backpackLevel);
            lobbyPlayerInventoryUI?.SetBagLevel(backpackData.backpackLevel);
            return TryEquipBackpackSlot(backpackData.itemID, slot);
        }

        private static bool TryEquipBackpackSlot(string backpackId, UnityEngine.Object context)
        {
            FixedString64Bytes id = new(backpackId ?? string.Empty);

            EquipmentSlotsBridge bridge = ResolveAnyEquipmentSlotsBridge();
            if (!string.IsNullOrEmpty(backpackId) && bridge != null && bridge.IsSpawned)
            {
                bridge.EquipItemServerRpc(id, WeaponSlot.None, default, 0);
                return true;
            }

            EquipmentSlots equipmentSlots = ResolveEquipmentSlots();
            if (equipmentSlots != null && equipmentSlots.IsServer)
            {
                equipmentSlots.EquipBackpack(id);
                return true;
            }

            if (equipmentSlots != null && equipmentSlots.IsSpawned)
            {
                equipmentSlots.EquipBackpackServerRpc(id);
                return true;
            }

            Debug.Log($"[InventorySlotUI] No spawned equipment sync target in this scene. The UI move will be captured by the lobby save snapshot. backpackId={backpackId}", context);
            return true;
        }

        private static bool TryEquipWeaponSlot(WeaponSlot weaponSlot, WeaponDataSO weaponData, UnityEngine.Object context)
        {
            if (weaponData == null)
                return TryClearWeaponSlot(weaponSlot, context);

            // 무기 장착은 탄창을 채우는 행위가 아니다.
            // 실제 탄약 장전은 ReloadSystem이 GridInventory의 AmmoDataSO를 소비해 처리한다.
            FixedString64Bytes ammoId = "";
            int ammoCount = 0;

            EquipmentSlotsBridge bridge = ResolveEquipmentSlotsBridge(weaponData.itemID);
            if (bridge != null && bridge.IsSpawned)
            {
                bridge.EquipItemServerRpc(
                    new FixedString64Bytes(weaponData.itemID),
                    weaponSlot,
                    ammoId,
                    (ushort)Mathf.Clamp(ammoCount, 0, ushort.MaxValue));

                return true;
            }

            EquipmentSlots equipmentSlots = ResolveEquipmentSlots();
            if (equipmentSlots != null && equipmentSlots.IsServer)
            {
                WeaponState state = new()
                {
                    loadedAmmoId = ammoId,
                    currentAmmo = ammoCount
                };

                equipmentSlots.UpdateSlot(weaponSlot, weaponData.itemID, state);
                return true;
            }

            if (equipmentSlots != null && equipmentSlots.IsSpawned)
            {
                equipmentSlots.EquipWeaponSlotServerRpc(
                    new FixedString64Bytes(weaponData.itemID),
                    weaponSlot,
                    ammoId,
                    (ushort)Mathf.Clamp(ammoCount, 0, ushort.MaxValue));

                return true;
            }

            Debug.Log($"[InventorySlotUI] No spawned equipment sync target in this scene. The UI move will be captured by the lobby save snapshot. slot={weaponSlot}, itemID={weaponData.itemID}", context);
            return true;
        }

        private static bool TryClearWeaponSlot(WeaponSlot weaponSlot, UnityEngine.Object context)
        {
            EquipmentSlots equipmentSlots = ResolveEquipmentSlots();
            if (equipmentSlots != null && equipmentSlots.IsServer)
            {
                equipmentSlots.UpdateSlot(weaponSlot, string.Empty, default);
                return true;
            }

            if (equipmentSlots != null && equipmentSlots.IsSpawned)
            {
                equipmentSlots.ClearWeaponSlotServerRpc(weaponSlot);
                return true;
            }

            Debug.Log($"[InventorySlotUI] No spawned equipment sync target in this scene. The UI clear will be captured by the lobby save snapshot. slot={weaponSlot}", context);
            return true;
        }

        private static EquipmentSlotsBridge ResolveEquipmentSlotsBridge(string itemId)
        {
            EquipmentSlotsBridge[] candidates = FindObjectsByType<EquipmentSlotsBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner && candidate.CanEquipItem(itemId))
                    return candidate;
            }

            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null && candidate.CanEquipItem(itemId))
                    return candidate;
            }

            return null;
        }

        private static EquipmentSlotsBridge ResolveAnyEquipmentSlotsBridge()
        {
            EquipmentSlotsBridge[] candidates = FindObjectsByType<EquipmentSlotsBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                    return candidate;
            }

            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private static LobbyPlayerInventoryUI ResolveLobbyPlayerInventoryUI()
        {
            LobbyPlayerInventoryUI[] candidates = FindObjectsByType<LobbyPlayerInventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return candidates.Length > 0 ? candidates[0] : null;
        }

        private static EquipmentSlots ResolveEquipmentSlots()
        {
            EquipmentSlots[] candidates = FindObjectsByType<EquipmentSlots>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                    return candidate;
            }

            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private static InventoryUI ResolveInventoryUI(InventorySlotUI slot)
        {
            if (slot == null)
                return null;

            if (slot.ownerInventoryUI != null)
                return slot.ownerInventoryUI;

            InventoryUI parentInventoryUI = slot.GetComponentInParent<InventoryUI>(true);
            if (parentInventoryUI != null)
                return parentInventoryUI;

            if (InventoryUI.ActiveInstance != null)
                return InventoryUI.ActiveInstance;

            InventoryUI[] candidates = FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (InventoryUI candidate in candidates)
            {
                if (candidate != null && candidate.OwnsSlot(slot))
                    return candidate;
            }

            if (candidates.Length > 0)
                return candidates[0];

            InventoryUI[] allInventoryUIs = Resources.FindObjectsOfTypeAll<InventoryUI>();
            foreach (InventoryUI candidate in allInventoryUIs)
            {
                if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.OwnsSlot(slot))
                    return candidate;
            }

            foreach (InventoryUI candidate in allInventoryUIs)
            {
                if (candidate != null && candidate.gameObject.scene.IsValid())
                    return candidate;
            }

            return null;
        }

        private bool TryGetWeaponSlot(out WeaponSlot weaponSlot)
        {
            string path = GetHierarchyPath(transform).ToLowerInvariant();
            string objectName = name.ToLowerInvariant();

            if (slotKind == InventorySlotKind.EquipmentSecondaryWeapon)
            {
                weaponSlot = WeaponSlot.Secondary;
                return true;
            }

            if (slotKind == InventorySlotKind.EquipmentMeleeWeapon)
            {
                weaponSlot = WeaponSlot.Melee;
                return true;
            }

            if (slotKind == InventorySlotKind.EquipmentPrimaryWeapon)
            {
                weaponSlot = path.Contains("primary2") || objectName.Contains("primary2") || objectName.Contains("_2")
                    ? WeaponSlot.Primary2
                    : WeaponSlot.Primary1;
                return true;
            }

            weaponSlot = WeaponSlot.None;
            return false;
        }

        private bool IsPrimary2Slot()
        {
            string path = GetHierarchyPath(transform).ToLowerInvariant();
            string objectName = name.ToLowerInvariant();
            return path.Contains("primary2") || objectName.Contains("primary2") || objectName.Contains("_2");
        }

        private bool CanAccept(ItemDataSO itemData)
        {
            if (itemData == null)
                return false;

            return slotKind switch
            {
                InventorySlotKind.Bag => true,
                InventorySlotKind.QuickSlot => IsQuickSlotItem(itemData),
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
            string path = GetHierarchyPath(transform).ToLowerInvariant();

            if (path.Contains("quickslotpanel"))
            {
                slotKind = InventorySlotKind.QuickSlot;
                return;
            }

            if (!autoDetectSlotKind)
                return;

            string objectName = name.ToLowerInvariant();

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

            if (iconImage == null || IsEmptySlotIconName(iconImage.name.ToLowerInvariant()))
                iconImage = FindItemIconReference();

            if (iconImage == null)
                iconImage = FindImageReference("icon", "item");

            if (stackCountText == null)
                stackCountText = GetComponentInChildren<TMP_Text>(true);

            if (emptySlotIcon == null)
                emptySlotIcon = FindEmptySlotIcon();
        }

        private Image FindItemIconReference()
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image == null || image.gameObject == gameObject)
                    continue;

                string lowerName = image.name.ToLowerInvariant();
                if (lowerName.Contains("item") && !IsEmptySlotIconName(lowerName))
                    return image;
            }

            return null;
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
            return FindEmptySlotIconIn(transform);
        }

        private GameObject FindEmptySlotIconIn(Transform root)
        {
            if (root == null)
                return null;

            Image[] images = root.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (IsEmptySlotIconCandidate(image))
                    return image.gameObject;
            }

            return null;
        }

        private void CreateDragIcon(PointerEventData eventData)
        {
            DestroyDragIcon();

            if (iconImage == null || iconImage.sprite == null)
            {
                if (debugTooltipEvents)
                    Debug.LogWarning($"[InventorySlotUI] {name} 슬롯의 아이콘 이미지가 비어 있어 드래그 아이콘을 만들 수 없습니다.", this);

                return;
            }

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

        private void WarnIfVisibleContentHasNoItemData(string actionName)
        {
            if (!debugTooltipEvents || !HasVisibleItemContent())
                return;

            Debug.LogWarning($"[InventorySlotUI] {name} 슬롯은 UI 이미지/텍스트가 보이지만 currentItemData 또는 stackCount가 비어 있어 {actionName}을 할 수 없습니다. 아이콘을 직접 넣지 말고 SetItem(ItemDataSO, count) 또는 InventoryUI testItemPool로 아이템 데이터를 넣어야 합니다.", this);
        }

        private bool HasVisibleItemContent()
        {
            bool hasIcon = iconImage != null &&
                           iconImage.enabled &&
                           iconImage.gameObject.activeInHierarchy &&
                           iconImage.sprite != null;

            bool hasRarityBackground = rarityBackground != null &&
                                       rarityBackground.enabled &&
                                       rarityBackground.gameObject.activeInHierarchy &&
                                       rarityBackground.sprite != null;

            bool hasStackText = stackCountText != null &&
                                stackCountText.enabled &&
                                stackCountText.gameObject.activeInHierarchy &&
                                !string.IsNullOrWhiteSpace(stackCountText.text);

            return hasIcon || hasRarityBackground || hasStackText;
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
            if (emptySlotIcon == gameObject)
                emptySlotIcon = null;

            if (emptySlotIcon == null)
                emptySlotIcon = FindEmptySlotIcon();

            if (!visible)
            {
                HideEmptySlotIconCandidates();
                return;
            }

            if (emptySlotIcon == null)
                return;

            emptySlotIcon.SetActive(visible);
        }

        private void HideEmptySlotIconCandidates()
        {
            HideEmptySlotIconCandidatesIn(transform);
        }

        private void HideEmptySlotIconCandidatesIn(Transform root)
        {
            if (root == null)
                return;

            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (IsEmptySlotIconCandidate(image))
                    image.gameObject.SetActive(false);
            }
        }

        private bool IsEmptySlotIconCandidate(Image image)
        {
            if (image == null || image == iconImage || image == rarityBackground)
                return false;

            GameObject imageObject = image.gameObject;
            if (imageObject == null || imageObject == gameObject || imageObject == lockOverlay)
                return false;

            if (lockOverlay != null && imageObject.transform.IsChildOf(lockOverlay.transform))
                return false;

            string lowerName = imageObject.name.ToLowerInvariant();
            if (lowerName.Contains("lock") || lowerName.Contains("background") || lowerName.Contains("rarity"))
                return false;

            return IsEmptySlotIconName(lowerName);
        }

        private static bool IsEmptySlotIconName(string lowerName)
        {
            return !string.IsNullOrEmpty(lowerName) &&
                   lowerName.StartsWith("icon_") &&
                   !lowerName.Contains("item");
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
            rarityBackground.color = Color.white;
            rarityBackground.enabled = backgroundSprite != null;
            rarityBackground.raycastTarget = false;
            rarityBackground.gameObject.SetActive(backgroundSprite != null);
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

        private static bool IsQuickSlotItem(ItemDataSO itemData)
        {
            if (itemData == null)
                return false;

            if (itemData is WeaponDataSO or ArmorDataSO or HelmetDataSO or BackpackDataSO)
                return false;

            return itemData.category is not ItemCategory.Weapon
                and not ItemCategory.Armor
                and not ItemCategory.Helmet
                and not ItemCategory.Backpack;
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

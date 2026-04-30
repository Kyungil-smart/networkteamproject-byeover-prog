using DeadZone.Core;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
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

    public class InventorySlotUI : MonoBehaviour
    {
        [BoxGroup("Slot State")]
        [ReadOnly]
        [Tooltip("Slot index in the inventory grid.")]
        [SerializeField] private int slotIndex;

        [BoxGroup("UI References")]
        [Tooltip("Image under RarityMask. This is the only graphic clipped by the slot mask.")]
        [SerializeField] private Image rarityBackground;

        [BoxGroup("UI References")]
        [Tooltip("Item icon image rendered above the rarity background.")]
        [SerializeField] private Image iconImage;

        [BoxGroup("UI References")]
        [Tooltip("Stack count text rendered above the item icon.")]
        [SerializeField] private TMP_Text stackCountText;

        [BoxGroup("UI References")]
        [Tooltip("Overlay rendered above all slot content when the slot is locked.")]
        [SerializeField] private GameObject lockOverlay;

        [BoxGroup("Rarity Backgrounds")]
        [SerializeField] private Sprite commonBackground;

        [BoxGroup("Rarity Backgrounds")]
        [SerializeField] private Sprite uncommonBackground;

        [BoxGroup("Rarity Backgrounds")]
        [SerializeField] private Sprite rareBackground;

        [BoxGroup("Rarity Backgrounds")]
        [SerializeField] private Sprite epicBackground;

        [BoxGroup("Rarity Backgrounds")]
        [SerializeField] private Sprite legendaryBackground;

        public int SlotIndex => slotIndex;

        public void Initialize(int index)
        {
            slotIndex = index;
            ClearItem();
        }

        public void SetItem(ItemDataSO itemData, int stackCount)
        {
            if (itemData == null)
            {
                ClearItem();
                return;
            }

            SetItem(itemData.icon, ToInventoryRarity(itemData.rarity), stackCount);
        }

        public void SetItem(Sprite icon, InventoryItemRarity rarity, int stackCount)
        {
            SetRarityBackground(rarity);
            SetIcon(icon);
            SetStackCount(stackCount);
        }

        public void ClearItem()
        {
            if (rarityBackground != null)
            {
                rarityBackground.sprite = null;
                rarityBackground.color = Color.white;
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
        }

        public void SetLocked(bool locked)
        {
            if (lockOverlay == gameObject)
            {
                Debug.LogError($"[InventorySlotUI] {name} has the slot root assigned as LockOverlay.", this);
                return;
            }

            if (lockOverlay != null)
                lockOverlay.SetActive(locked);
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null)
                return;

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            iconImage.gameObject.SetActive(icon != null);
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
        [BoxGroup("Debug")]
        [Button("Lock")]
        private void TestLock()
        {
            SetLocked(true);
        }

        [BoxGroup("Debug")]
        [Button("Unlock")]
        private void TestUnlock()
        {
            SetLocked(false);
        }
#endif
    }
}

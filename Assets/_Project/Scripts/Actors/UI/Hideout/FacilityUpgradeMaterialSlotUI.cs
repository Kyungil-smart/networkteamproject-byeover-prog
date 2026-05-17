using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors.UI.Hideout
{
    // 업그레이드에 필요한 재료 하나의 아이콘, 이름, 보유/필요 수량을 표시합니다.
    [DisallowMultipleComponent]
    public sealed class FacilityUpgradeMaterialSlotUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField]
        private Image iconImage;

        [SerializeField]
        private TMP_Text itemNameText;

        [SerializeField]
        private TMP_Text amountText;

        [Header("색상")]
        [SerializeField]
        private Color enoughColor = Color.white;

        [SerializeField]
        private Color shortageColor = new Color(1f, 0.25f, 0.25f);

        [SerializeField]
        private Color inactiveColor = new Color(0.55f, 0.65f, 0.75f);

        public void Set(ItemDataSO item, int ownedAmount, int requiredAmount, bool forceInactive = false)
        {
            if (item == null)
            {
                Clear();
                return;
            }

            bool enough = ownedAmount >= requiredAmount;
            bool emphasize = enough && !forceInactive;

            if (iconImage != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = item.icon != null;
                iconImage.color = forceInactive ? inactiveColor : Color.white;
            }

            if (itemNameText != null)
            {
                itemNameText.text = !string.IsNullOrWhiteSpace(item.displayName)
                    ? item.displayName
                    : item.itemID;
                itemNameText.color = forceInactive ? inactiveColor : enoughColor;
                itemNameText.fontStyle = emphasize ? FontStyles.Bold : FontStyles.Normal;
            }

            if (amountText != null)
            {
                amountText.text = $"{ownedAmount}/{requiredAmount}";
                amountText.color = forceInactive
                    ? inactiveColor
                    : enough
                        ? enoughColor
                        : shortageColor;
                amountText.fontStyle = emphasize ? FontStyles.Bold : FontStyles.Normal;
            }

            gameObject.SetActive(true);
        }

        public void Clear()
        {
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
                iconImage.color = Color.white;
            }

            if (itemNameText != null)
            {
                itemNameText.text = string.Empty;
                itemNameText.fontStyle = FontStyles.Normal;
            }

            if (amountText != null)
            {
                amountText.text = string.Empty;
                amountText.fontStyle = FontStyles.Normal;
            }

            gameObject.SetActive(false);
        }
    }
}

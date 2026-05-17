using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors.UI.Hideout
{
    // 업그레이드에 필요한 재료 하나의 아이콘, 이름, 보유/필요 수량을 표시
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

        public void Set(ItemDataSO item, int ownedAmount, int requiredAmount)
        {
            if (item == null)
            {
                Clear();
                return;
            }

            bool enough = ownedAmount >= requiredAmount;

            if (iconImage != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = item.icon != null;
            }

            if (itemNameText != null)
                itemNameText.text = !string.IsNullOrWhiteSpace(item.displayName)
                    ? item.displayName
                    : item.itemID;

            if (amountText != null)
            {
                amountText.text = $"{ownedAmount}/{requiredAmount}";
                amountText.color = enough ? enoughColor : shortageColor;
            }

            gameObject.SetActive(true);
        }

        public void Clear()
        {
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            if (itemNameText != null)
                itemNameText.text = string.Empty;

            if (amountText != null)
                amountText.text = string.Empty;

            gameObject.SetActive(false);
        }
    }
}

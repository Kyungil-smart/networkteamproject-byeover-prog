using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors.UI.Hideout
{
    // 제작 UI에서 재료 아이템 또는 결과 아이템 하나를 표시하는 슬롯
    // 아이콘, 아이템 이름, 보유/필요 수량을 표시
    [DisallowMultipleComponent]
    public sealed class FacilityCraftItemSlotUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;

        [Header("색상")]
        [SerializeField] private Color enoughColor = Color.white;
        [SerializeField] private Color lackColor = Color.red;

        public void SetIngredient(ItemRequirement requirement, int ownedCount)
        {
            if (requirement.item == null)
            {
                Clear();
                return;
            }

            int requiredCount = Mathf.Max(1, requirement.amount);
            bool hasEnough = ownedCount >= requiredCount;

            SetIcon(requirement.item.icon);
            SetName(GetItemName(requirement.item));
            SetCount($"{ownedCount}/{requiredCount}", hasEnough ? enoughColor : lackColor);
        }

        public void SetResult(ItemDataSO resultItem, int resultCount)
        {
            if (resultItem == null)
            {
                Clear();
                return;
            }

            int count = Mathf.Max(1, resultCount);

            SetIcon(resultItem.icon);
            SetName(GetItemName(resultItem));
            SetCount($"x{count}", enoughColor);
        }

        public void Clear()
        {
            SetIcon(null);
            SetName(string.Empty);
            SetCount(string.Empty, enoughColor);
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null)
                return;

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        private void SetName(string itemName)
        {
            if (nameText != null)
                nameText.text = itemName;
        }

        private void SetCount(string count, Color color)
        {
            if (countText == null)
                return;

            countText.text = count;
            countText.color = color;
        }

        private string GetItemName(ItemDataSO item)
        {
            if (item == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            return item.itemID;
        }
    }
}
using TMPro;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class RaidLootItemRowUI : MonoBehaviour
    {
        [Header("획득 아이템")]
        [SerializeField] private TMP_Text textItemName;
        [SerializeField] private TMP_Text textItemCount;

        public void Set(string itemName, int count)
        {
            if (textItemName != null)
                textItemName.text = itemName;

            if (textItemCount != null)
                textItemCount.text = $"X {count:N0}";
        }
    }
}
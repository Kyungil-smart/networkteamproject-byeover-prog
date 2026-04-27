using UnityEngine;


namespace DeadZone.Core
{
    public enum ItemCategory : byte
    {
        Weapon, Ammo, Armor, Helmet, Med, Food, Valuable, Material, Key, QuestItem
    }

    public enum RarityTier : byte
    {
        Common, Rare, VeryRare, Epic
    }

    /// <summary>
    /// 모든 아이템의 베이스 ScriptableObject. 서브클래스가 특정 필드를 추가한다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Items/Item Data", fileName = "Item_New")]
    public class ItemDataSO : ScriptableObject
    {
        [Header("Identity")]
        public string itemID;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;

        [Header("Classification")]
        public ItemCategory category;
        public RarityTier rarity;

        [Header("Grid")]
        public Vector2Int gridSize = Vector2Int.one;
        public int maxStackSize = 1;
        public float weightKg = 0.1f;

        [Header("Economy")]
        public int baseSellPrice;

        [Header("World")]
        public GameObject worldPrefab;
    }
}

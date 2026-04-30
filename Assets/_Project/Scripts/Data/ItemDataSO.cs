using UnityEngine;


namespace DeadZone.Core
{
    /// <summary>
    /// 아이템 카테고리. 그리드 인벤토리/장비 슬롯 검증/루팅 풀 분류에 사용.
    /// </summary>
    public enum ItemCategory : byte
    {
        Weapon,
        Ammo,
        Armor,
        Helmet,
        Backpack,   // 신규 — 장착 시 GridInventory가 확장됨
        Med,
        Food,
        Valuable,
        Material,
        Tool,
        Key,
        QuestItem
    }
    
    public enum RarityTier : byte
    {
        Common,      // 일반 60%
        Uncommon,    // 희귀 30%
        Rare,        // 레어 8.9%
        Epic,        // 에픽 1%
        Legendary    // 전설 0.1%
    }

    /// <summary>
    /// 모든 아이템의 베이스 ScriptableObject. 서브클래스가 특정 필드를 추가한다.
    /// (WeaponDataSO, AmmoDataSO, ArmorDataSO, HelmetDataSO, BackpackDataSO 등)
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

        [Header("이중 분류")]
        [Tooltip("true면 '귀중품' — 작업대 제작 불가, DocCase 풀에 포함. " +
                 "GameSystem §3.6 기준 14종: 부서진 LCD, 하드디스크, 시계, CPU, 통기타, " +
                 "램, VR기기, AR기기, 의존성 주입, 그래픽 카드, JADE 노트북, 볼킴 마이크, " +
                 "학습 안경, JADE쨩 피규어")]
        public bool isValuable;

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
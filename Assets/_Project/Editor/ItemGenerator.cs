#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.EditorTools
{
    
    public static class ItemGenerator
    {
        private const string ROOT_FOLDER = "Assets/_Project/Data/Items";

        // 카테고리별 전용 폴더 — 팀원 Common/ 과 분리
        private static readonly Dictionary<ItemCategory, string> CATEGORY_FOLDERS = new()
        {
            { ItemCategory.Material, "Materials" },
            { ItemCategory.Med,      "Medical" },
            { ItemCategory.Valuable, "Valuables" },
            { ItemCategory.Tool,     "Tools" },
            { ItemCategory.Armor,    "Armor" },
            { ItemCategory.Helmet,   "Armor" },   // 헬멧도 같은 폴더 (방어구 계열)
        };

        private const string MISC_FOLDER = "Misc";  // 위 매핑에 없는 카테고리

        // ----------- 메뉴 진입점 -----------

        [MenuItem("Tools/DeadZone/Generate Farming Items (v3)")]
        public static void GenerateAll()
        {
            int created = 0, skipped = 0;
            var seen = new HashSet<string>();

            EnsureRootFolders();

            foreach (var def in ALL_ITEMS)
            {
                if (!seen.Add(def.id))
                {
                    Debug.LogWarning($"[ItemGenerator v3] 중복 itemID '{def.id}' 스킵");
                    continue;
                }

                if (TryCreateItem(def, out bool wasSkipped))
                {
                    if (wasSkipped) skipped++;
                    else created++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "DeadZone Item Generator v3",
                $"파밍팀 자산 생성 완료 (총 55종)\n\n" +
                $"신규 생성: {created}개\n" +
                $"기존 스킵: {skipped}개\n\n" +
                $"제외 항목 (무기팀 담당):\n" +
                $"  - 무기 9종 (Weapon_*.asset)\n" +
                $"  - 탄약 3종 (Ammo_*.asset)\n\n" +
                $"다음:\n" +
                $"1. Tools/DeadZone/Refresh ItemDatabase\n" +
                $"2. Tools/DeadZone/Generate All LootTables (v2)",
                "OK");
        }

        // ----------- 단일 아이템 생성 -----------

        private static bool TryCreateItem(ItemDef def, out bool skipped)
        {
            skipped = false;
            string folder = GetFolderForCategory(def.category);
            string path = $"{ROOT_FOLDER}/{folder}/ITM_{def.id}.asset";

            if (AssetDatabase.LoadAssetAtPath<ItemDataSO>(path) != null)
            {
                skipped = true;
                return true;
            }

            var so = ScriptableObject.CreateInstance<ItemDataSO>();
            so.itemID = def.id;
            so.displayName = def.displayName;
            so.description = def.description;
            so.category = def.category;
            so.rarity = def.rarity;
            so.isValuable = def.isValuable;
            so.gridSize = def.gridSize;
            so.maxStackSize = def.maxStackSize;
            so.weightKg = def.weightKg;
            so.baseSellPrice = def.baseSellPrice;

            AssetDatabase.CreateAsset(so, path);
            return true;
        }

        // ----------- 폴더 -----------

        private static void EnsureRootFolders()
        {
            EnsureFolder("Assets/_Project");
            EnsureFolder("Assets/_Project/Data");
            EnsureFolder("Assets/_Project/Data/Items");

            // 카테고리 폴더 (중복 제거)
            var uniqueFolders = new HashSet<string>(CATEGORY_FOLDERS.Values) { MISC_FOLDER };
            foreach (var folder in uniqueFolders)
            {
                EnsureFolder($"{ROOT_FOLDER}/{folder}");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string GetFolderForCategory(ItemCategory cat)
            => CATEGORY_FOLDERS.TryGetValue(cat, out var f) ? f : MISC_FOLDER;

        // ----------- 정의 구조체 -----------

        private struct ItemDef
        {
            public string id;
            public string displayName;
            public string description;
            public ItemCategory category;
            public RarityTier rarity;
            public bool isValuable;
            public Vector2Int gridSize;
            public int maxStackSize;
            public float weightKg;
            public int baseSellPrice;
        }

        private static ItemDef Mk(string id, string name, ItemCategory cat, RarityTier rar,
                                  Vector2Int size, int stack, float weight, int sell,
                                  bool valuable = false)
        {
            return new ItemDef
            {
                id = id, displayName = name, description = "",
                category = cat, rarity = rar, isValuable = valuable,
                gridSize = size, maxStackSize = stack, weightKg = weight, baseSellPrice = sell,
            };
        }

        // ----------- 55종 정의 (ItemIconPrompts v1.0 - 무기 9 - 탄약 3 - 현금 제외) -----------

        private static readonly ItemDef[] ALL_ITEMS = new[]
        {
            // ========== Common (15개 = 20 - 무기 2 - 탄약 1 - 현금 제외) ==========
            Mk("Bolt",             "볼트",          ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 30, 0.05f, 30),
            Mk("MetalScrap",       "금속 조각",      ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 20, 0.3f,  50),
            Mk("WoodScrap",        "나무 조각",      ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 20, 0.2f,  40),
            Mk("JunkPile",         "잡동사니",       ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 20, 0.1f,  20),
            Mk("PaperStack",       "종이",          ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 30, 0.02f, 10),
            Mk("PlasticScrap",     "플라스틱 조각",   ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 20, 0.1f,  25),
            Mk("Gunpowder",        "화약",          ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 15, 0.15f, 80),
            Mk("MedicalSupplies",  "의료용품",      ItemCategory.Med,      RarityTier.Common, new Vector2Int(1,1), 10, 0.1f,  100),
            Mk("Bandage",          "붕대",          ItemCategory.Med,      RarityTier.Common, new Vector2Int(1,1), 5,  0.1f,  150),
            Mk("DuctTape",         "덕 테이프",      ItemCategory.Material, RarityTier.Common, new Vector2Int(1,1), 10, 0.2f,  120),
            Mk("BrokenLCD",        "부서진 LCD",    ItemCategory.Valuable, RarityTier.Common, new Vector2Int(1,2), 1,  1.5f,  600,  valuable:true),
            Mk("HardDiskDrive",    "하드디스크",     ItemCategory.Valuable, RarityTier.Common, new Vector2Int(1,1), 1,  0.4f,  800,  valuable:true),
            Mk("Armor_C1",         "1 클래스 아머",  ItemCategory.Armor,    RarityTier.Common, new Vector2Int(2,3), 1,  1.5f,  1000),
            Mk("Armor_C2",         "2 클래스 아머",  ItemCategory.Armor,    RarityTier.Common, new Vector2Int(2,3), 1,  2.5f,  2500),
            Mk("Helmet_C1",        "1 클래스 헬멧",  ItemCategory.Helmet,   RarityTier.Common, new Vector2Int(2,2), 1,  0.8f,  800),

            // 추가 — Helmet C2도 Common
            Mk("Helmet_C2",        "2 클래스 헬멧",  ItemCategory.Helmet,   RarityTier.Common, new Vector2Int(2,2), 1,  1.2f,  1800),

            // ========== Uncommon (19개 = 22 - 무기 2 - 탄약 1) ==========
            Mk("GunParts",            "총기 부품",      ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,1), 5,  0.4f, 600),
            Mk("SSD",                 "SSD",          ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,1), 1,  0.1f, 500),
            Mk("WoodPlank",           "나무 판자",     ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,3), 5,  1.0f, 200),
            Mk("CPU",                 "CPU",          ItemCategory.Valuable, RarityTier.Uncommon, new Vector2Int(1,1), 1,  0.05f, 2500, valuable:true),
            Mk("IronBar",             "철 봉",        ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,3), 3,  1.5f, 400),
            Mk("Battery",             "건전지",       ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,1), 10, 0.2f, 300),
            Mk("PhillipsScrewdriver", "십자 드라이버", ItemCategory.Tool,     RarityTier.Uncommon, new Vector2Int(1,2), 1,  0.2f, 800),
            Mk("FlatheadScrewdriver", "일자 드라이버", ItemCategory.Tool,     RarityTier.Uncommon, new Vector2Int(1,2), 1,  0.2f, 800),
            Mk("Hammer",              "망치",         ItemCategory.Tool,     RarityTier.Uncommon, new Vector2Int(1,2), 1,  0.8f, 1000),
            Mk("Shovel",              "삽",          ItemCategory.Tool,     RarityTier.Uncommon, new Vector2Int(1,4), 1,  1.5f, 700),
            Mk("Trowel",              "모종삽",       ItemCategory.Tool,     RarityTier.Uncommon, new Vector2Int(1,2), 1,  0.4f, 400),
            Mk("FirstAidKit",         "구급상자",     ItemCategory.Med,      RarityTier.Uncommon, new Vector2Int(2,2), 1,  0.6f, 2500),
            Mk("SlowHealSyringe",     "지속 회복 주사기", ItemCategory.Med,   RarityTier.Uncommon, new Vector2Int(1,1), 3,  0.1f, 1500),
            Mk("Watch",               "시계",         ItemCategory.Valuable, RarityTier.Uncommon, new Vector2Int(1,1), 1,  0.1f, 800,  valuable:true),
            Mk("AcousticGuitar",      "통기타",       ItemCategory.Valuable, RarityTier.Uncommon, new Vector2Int(2,4), 1,  2.5f, 1500, valuable:true),
            Mk("Armor_C3",            "3 클래스 아머", ItemCategory.Armor,    RarityTier.Uncommon, new Vector2Int(2,3), 1,  4.0f, 5500),
            Mk("Armor_C4",            "4 클래스 아머", ItemCategory.Armor,    RarityTier.Uncommon, new Vector2Int(2,3), 1,  6.0f, 12000),
            Mk("Helmet_C3",           "3 클래스 헬멧", ItemCategory.Helmet,   RarityTier.Uncommon, new Vector2Int(2,2), 1,  2.0f, 5000),

            // ========== Rare (14개 = 18 - 무기 3 - 탄약 1) ==========
            Mk("FastHealSyringe",       "급속 회복 주사기",   ItemCategory.Med,      RarityTier.Rare, new Vector2Int(1,1), 3, 0.1f,  4000),
            Mk("WeightCapacitySyringe", "무게 증가 주사기",   ItemCategory.Med,      RarityTier.Rare, new Vector2Int(1,1), 3, 0.1f,  3500),
            Mk("AdvancedFirstAidKit",   "고급 구급상자",     ItemCategory.Med,      RarityTier.Rare, new Vector2Int(2,2), 1, 0.7f,  6000),
            Mk("AdvancedGunParts",      "고급 총기 부품",    ItemCategory.Material, RarityTier.Rare, new Vector2Int(1,1), 5, 0.4f,  2500),
            Mk("RAM",                   "램",             ItemCategory.Valuable, RarityTier.Rare, new Vector2Int(1,1), 1, 0.05f, 4500,  valuable:true),
            Mk("CarBattery",            "자동차 배터리",     ItemCategory.Material, RarityTier.Rare, new Vector2Int(2,2), 1, 5.0f,  3000),
            Mk("VRHeadset",             "VR기기",         ItemCategory.Valuable, RarityTier.Rare, new Vector2Int(2,2), 1, 0.6f,  6000,  valuable:true),
            Mk("ARGlasses",             "AR기기",         ItemCategory.Valuable, RarityTier.Rare, new Vector2Int(1,2), 1, 0.4f,  7000,  valuable:true),
            Mk("PowerDrill",            "전동 드릴",       ItemCategory.Tool,     RarityTier.Rare, new Vector2Int(2,2), 1, 1.5f,  3000),
            Mk("Sledgehammer",          "대형 해머",       ItemCategory.Tool,     RarityTier.Rare, new Vector2Int(1,4), 1, 4.0f,  4000),
            Mk("Defibrillator",         "제세동기",       ItemCategory.Med,      RarityTier.Rare, new Vector2Int(2,2), 1, 2.5f,  8000),
            Mk("DokiDokiDIBook",        "두근두근 의존성 주입", ItemCategory.Valuable, RarityTier.Rare, new Vector2Int(1,2), 1, 0.3f,  5000,  valuable:true),
            Mk("Armor_C5",              "5 클래스 아머",    ItemCategory.Armor,    RarityTier.Rare, new Vector2Int(2,3), 1, 9.0f,  25000),
            Mk("Helmet_C4",             "4 클래스 헬멧",    ItemCategory.Helmet,   RarityTier.Rare, new Vector2Int(2,2), 1, 2.5f,  12000),

            // ========== Epic (5개 = 7 - 무기 2) ==========
            Mk("Armor_C6",          "6 클래스 아머",    ItemCategory.Armor,    RarityTier.Epic, new Vector2Int(2,3), 1, 12.0f, 60000),
            Mk("GraphicsCard",      "그래픽 카드",      ItemCategory.Valuable, RarityTier.Epic, new Vector2Int(2,2), 1, 0.5f,  20000, valuable:true),
            Mk("JadeChanLaptop",    "JADE쨩의 노트북",  ItemCategory.Valuable, RarityTier.Epic, new Vector2Int(2,3), 1, 1.5f,  25000, valuable:true),
            Mk("VolkimMicrophone",  "볼킴의 마이크",    ItemCategory.Valuable, RarityTier.Epic, new Vector2Int(1,2), 1, 0.5f,  18000, valuable:true),
            Mk("GlassesOfLearning", "학습의 안경",      ItemCategory.Valuable, RarityTier.Epic, new Vector2Int(1,2), 1, 0.1f,  15000, valuable:true),

            // ========== Legendary (1개) — 0.1% ==========
            Mk("JadeChanFigure",    "JADE쨩 피규어",   ItemCategory.Valuable, RarityTier.Legendary, new Vector2Int(2,3), 1, 0.8f, 100000, valuable:true),
        };
    }
}
#endif

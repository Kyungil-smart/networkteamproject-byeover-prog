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

        // ----------- 메뉴 진입점 -----------

        [MenuItem("Tools/DeadZone/Generate All Items")]
        public static void GenerateAll()
        {
            int created = 0, skipped = 0;
            var seen = new HashSet<string>();

            EnsureRootFolders();

            foreach (var def in ALL_ITEMS)
            {
                if (!seen.Add(def.id))
                {
                    Debug.LogWarning($"[ItemGenerator] 중복 itemID '{def.id}' 스킵");
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
                "DeadZone Item Generator v2",
                $"완료 (마스터: ItemIconPrompts v1.0 / 총 68종)\n\n" +
                $"신규 생성: {created}개\n" +
                $"기존 스킵: {skipped}개\n\n" +
                $"다음 단계:\n" +
                $"1. Tools/DeadZone/Refresh ItemDatabase\n" +
                $"2. Tools/DeadZone/Generate All LootTables",
                "OK");
        }

        // ----------- 단일 아이템 생성 -----------

        private static bool TryCreateItem(ItemDef def, out bool skipped)
        {
            skipped = false;
            string folder = GetFolderForRarity(def.rarity);
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

        // ----------- 폴더 (등급별 분류 — 마스터 §4.1) -----------

        private static void EnsureRootFolders()
        {
            EnsureFolder("Assets/_Project");
            EnsureFolder("Assets/_Project/Data");
            EnsureFolder("Assets/_Project/Data/Items");
            foreach (var folder in RARITY_FOLDERS.Values)
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

        private static string GetFolderForRarity(RarityTier r)
            => RARITY_FOLDERS.TryGetValue(r, out var f) ? f : "Misc";

        private static readonly Dictionary<RarityTier, string> RARITY_FOLDERS = new()
        {
            { RarityTier.Common,    "Common" },
            { RarityTier.Uncommon,  "Uncommon" },
            { RarityTier.Rare,      "Rare" },
            { RarityTier.Epic,      "Epic" },
            { RarityTier.Legendary, "Legendary" },
        };

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

        // ----------- 68종 정의 (ItemIconPrompts v1.0 §3 1:1 매칭) -----------

        private static readonly ItemDef[] ALL_ITEMS = new[]
        {
            // ========== Common (20개) — 60% ==========
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
            Mk("USDollars",        "현금/달러",      ItemCategory.Valuable, RarityTier.Common, new Vector2Int(1,1), 50, 0.01f, 100),
            Mk("HardDiskDrive",    "하드디스크",     ItemCategory.Valuable, RarityTier.Common, new Vector2Int(1,1), 1,  0.4f,  800,  valuable:true),
            Mk("Ammo_LP",          "LP 탄약",       ItemCategory.Ammo,     RarityTier.Common, new Vector2Int(1,1), 60, 0.01f, 10),
            Mk("Glock17",          "글락-17",       ItemCategory.Weapon,   RarityTier.Common, new Vector2Int(2,2), 1,  0.7f,  1500),
            Mk("DefensePistol",    "호신용 권총",    ItemCategory.Weapon,   RarityTier.Common, new Vector2Int(2,2), 1,  0.6f,  1200),
            Mk("Armor_C1",         "1 클래스 아머",  ItemCategory.Armor,    RarityTier.Common, new Vector2Int(2,3), 1,  1.5f,  1000),
            Mk("Armor_C2",         "2 클래스 아머",  ItemCategory.Armor,    RarityTier.Common, new Vector2Int(2,3), 1,  2.5f,  2500),
            Mk("Helmet_C1",        "1 클래스 헬멧",  ItemCategory.Helmet,   RarityTier.Common, new Vector2Int(2,2), 1,  0.8f,  800),
            Mk("Helmet_C2",        "2 클래스 헬멧",  ItemCategory.Helmet,   RarityTier.Common, new Vector2Int(2,2), 1,  1.2f,  1800),

            // ========== Uncommon (22개) — 30% ==========
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
            Mk("Grenade",             "수류탄",       ItemCategory.Material, RarityTier.Uncommon, new Vector2Int(1,1), 1,  0.5f, 3000),
            Mk("Ammo_BP",             "BP 탄약",      ItemCategory.Ammo,     RarityTier.Uncommon, new Vector2Int(1,1), 60, 0.015f, 25),
            Mk("Revolver",            "리볼버",       ItemCategory.Weapon,   RarityTier.Uncommon, new Vector2Int(2,2), 1,  1.0f, 5000),
            Mk("PumpShotgun",         "펌프샷건",     ItemCategory.Weapon,   RarityTier.Uncommon, new Vector2Int(2,4), 1,  3.5f, 6500),
            Mk("Armor_C3",            "3 클래스 아머", ItemCategory.Armor,    RarityTier.Uncommon, new Vector2Int(2,3), 1,  4.0f, 5500),
            Mk("Armor_C4",            "4 클래스 아머", ItemCategory.Armor,    RarityTier.Uncommon, new Vector2Int(2,3), 1,  6.0f, 12000),
            Mk("Helmet_C3",           "3 클래스 헬멧", ItemCategory.Helmet,   RarityTier.Uncommon, new Vector2Int(2,2), 1,  2.0f, 5000),

            // ========== Rare (18개) — 8.9% ==========
            Mk("SK74",                  "SK-74",          ItemCategory.Weapon,   RarityTier.Rare, new Vector2Int(2,4), 1, 3.5f,  12000),
            Mk("B90",                   "B90",            ItemCategory.Weapon,   RarityTier.Rare, new Vector2Int(2,3), 1, 2.8f,  10000),
            Mk("MB7",                   "MB-7",           ItemCategory.Weapon,   RarityTier.Rare, new Vector2Int(2,3), 1, 4.0f,  15000),
            Mk("Ammo_AP",               "AP 탄약",         ItemCategory.Ammo,     RarityTier.Rare, new Vector2Int(1,1), 60, 0.02f, 60),
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

            // ========== Epic (7개) — 1% ==========
            Mk("F2",                "F2 소총",         ItemCategory.Weapon,   RarityTier.Epic, new Vector2Int(2,4), 1, 4.5f,  35000),
            Mk("DragSniper",        "드라그 소총",      ItemCategory.Weapon,   RarityTier.Epic, new Vector2Int(2,4), 1, 5.0f,  40000),
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
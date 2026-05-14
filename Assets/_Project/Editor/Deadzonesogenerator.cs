#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DeadZone.Core;

namespace DeadZone.Editor
{
    public static class DeadZoneSOGenerator
    {
        // ─── 기존 에셋 경로 ───
        private const string DATA_ROOT  = "Assets/_Project/Data";
        private const string WEAPONS    = DATA_ROOT + "/Weapons";
        private const string ITEMS      = DATA_ROOT + "/Items";
        private const string BACKPACKS  = DATA_ROOT + "/Backpacks";

        // ─── 신규 생성 경로 ───
        private const string RECIPES_WB = DATA_ROOT + "/Recipes/Workbench";
        private const string RECIPES_MD = DATA_ROOT + "/Recipes/Medical";
        private const string TRADERS    = DATA_ROOT + "/Traders";

        [MenuItem("DeadZone/레시피 + 트레이더 SO 생성")]
        public static void GenerateAll()
        {
            EnsureFolder(RECIPES_WB);
            EnsureFolder(RECIPES_MD);
            EnsureFolder(TRADERS);

            // 기존 에셋 존재 확인
            if (Load<ItemDataSO>(ITEMS + "/Materials/ITM_GunParts.asset") == null)
            {
                EditorUtility.DisplayDialog("오류",
                    "기존 에셋을 찾을 수 없습니다.\n" +
                    "Data 폴더가 Assets/_Project/Data/ 에 있는지 확인하세요.",
                    "확인");
                return;
            }

            CreateWorkbenchRecipes();
            CreateMedicalRecipes();
            CreateTraders();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SOGenerator] 완료! 레시피 13개 + 트레이더 4개 생성");
            EditorUtility.DisplayDialog("완료",
                "레시피 13개 + 트레이더 4개 생성 완료!\n\n" +
                "Recipes/Workbench (7개)\n" +
                "Recipes/Medical (6개)\n" +
                "Traders (Igor, Vera, Doc, Shade)", "확인");
        }

        // ═══════════════════════════════════════
        // 총기 작업대 레시피 (7개)
        // ═══════════════════════════════════════
        private static void CreateWorkbenchRecipes()
        {
            var gunParts    = Load<ItemDataSO>(ITEMS + "/Materials/ITM_GunParts.asset");
            var gunPartsAdv = Load<ItemDataSO>(ITEMS + "/Materials/ITM_AdvancedGunParts.asset");
            var bolt        = Load<ItemDataSO>(ITEMS + "/Materials/ITM_Bolt.asset");

            // 리볼버 — Lv1
            MakeRecipe(RECIPES_WB, "Recipe_Revolver", "recipe_revolver",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_Revolver.asset"), 1, 1, RarityTier.Uncommon,
                (gunParts, 1));

            // 펌프샷건 — Lv2
            MakeRecipe(RECIPES_WB, "Recipe_PumpSG", "recipe_pump_sg",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_PumpSG.asset"), 1, 2, RarityTier.Uncommon,
                (gunParts, 1), (bolt, 2));

            // SK-74 — Lv3
            MakeRecipe(RECIPES_WB, "Recipe_SK74", "recipe_sk74",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_SK74.asset"), 1, 3, RarityTier.Rare,
                (gunParts, 3));

            // B90 — Lv3
            MakeRecipe(RECIPES_WB, "Recipe_B90", "recipe_b90",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_B90.asset"), 1, 3, RarityTier.Rare,
                (gunParts, 3));

            // MB-7 — Lv3
            MakeRecipe(RECIPES_WB, "Recipe_MB7", "recipe_mb7",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_MB7.asset"), 1, 3, RarityTier.Rare,
                (gunParts, 4));

            // F2 — Lv4
            MakeRecipe(RECIPES_WB, "Recipe_F2", "recipe_f2",
                Load<WeaponDataSO>(WEAPONS + "/Weapon_F2.asset"), 1, 4, RarityTier.Epic,
                (gunPartsAdv, 2), (gunParts, 2));

            // 드라그 소총 — Lv4
            MakeRecipe(RECIPES_WB, "Recipe_DragSR", "recipe_drag_sr",
                Load<ItemDataSO>(ITEMS + "/Epic/ITM_DragSniper.asset"), 1, 4, RarityTier.Epic,
                (gunPartsAdv, 2), (gunParts, 2));
        }

        // ═══════════════════════════════════════
        // 의료시설 레시피 (4개)
        // ═══════════════════════════════════════
        private static void CreateMedicalRecipes()
        {
            var medSupplies = Load<ItemDataSO>(ITEMS + "/Medical/ITM_MedicalSupplies.asset");
            var bandage     = Load<ItemDataSO>(ITEMS + "/Medical/ITM_Bandage.asset");

            // 붕대 — Lv1 (의료용품 1 → 붕대 3)
            MakeRecipe(RECIPES_MD, "Recipe_Bandage", "recipe_bandage",
                bandage, 3, 1, RarityTier.Common,
                (medSupplies, 1));

            // 구급상자 — Lv2
            MakeRecipe(RECIPES_MD, "Recipe_FirstAid", "recipe_firstaid",
                Load<ItemDataSO>(ITEMS + "/Medical/ITM_FirstAidKit.asset"), 1, 2, RarityTier.Uncommon,
                (medSupplies, 2));

            // 고급 구급상자 — Lv3
            MakeRecipe(RECIPES_MD, "Recipe_FirstAidAdv", "recipe_firstaid_adv",
                Load<ItemDataSO>(ITEMS + "/Medical/ITM_AdvancedFirstAidKit.asset"), 1, 3, RarityTier.Rare,
                (medSupplies, 3));

            // 지속 회복 주사기 — Lv4 (붕대 3 → 주사기)
            MakeRecipe(RECIPES_MD, "Recipe_RegenSyringe", "recipe_regen_syringe",
                Load<ItemDataSO>(ITEMS + "/Medical/ITM_SlowHealSyringe.asset"), 1, 4, RarityTier.Rare,
                (bandage, 3));

            MakeRecipe(RECIPES_MD, "Recipe_FastHealSyringe", "recipe_fast_heal_syringe",
                Load<ItemDataSO>(ITEMS + "/Medical/ITM_FastHealSyringe.asset"), 1, 4, RarityTier.Rare,
                (medSupplies, 3), (bandage, 1));

            MakeRecipe(RECIPES_MD, "Recipe_WeightCapacitySyringe", "recipe_weight_capacity_syringe",
                Load<ItemDataSO>(ITEMS + "/Medical/ITM_WeightCapacitySyringe.asset"), 1, 4, RarityTier.Rare,
                (medSupplies, 4), (Load<ItemDataSO>(ITEMS + "/Medical/ITM_Defibrillator.asset"), 1));
        }

        // ═══════════════════════════════════════
        // 트레이더 (4명)
        // ═══════════════════════════════════════
        private static void CreateTraders()
        {
            // ── Igor (무기상) ──
            var igor = ScriptableObject.CreateInstance<TraderDataSO>();
            igor.traderName = "Igor";
            igor.sellMultiplier = 0.5f;
            igor.stock = new List<TraderEntry>
            {
                Entry(WEAPONS + "/Weapon_Glock17.asset",       3000,  1),
                Entry(WEAPONS + "/Weapon_SelfDefense.asset",   2400,  1),
                Entry(WEAPONS + "/Weapon_Revolver.asset",      10000, 2),
                Entry(WEAPONS + "/Weapon_PumpSG.asset",        13000, 2),
                Entry(WEAPONS + "/Weapon_SK74.asset",          24000, 3),
                Entry(WEAPONS + "/Weapon_B90.asset",           20000, 3),
                Entry(WEAPONS + "/Weapon_MB7.asset",           30000, 3),
            };
            SaveAsset(igor, TRADERS, "Trader_Igor");

            // ── Vera (군수상) ──
            var vera = ScriptableObject.CreateInstance<TraderDataSO>();
            vera.traderName = "Vera";
            vera.sellMultiplier = 0.5f;
            vera.stock = new List<TraderEntry>
            {
                Entry(ITEMS + "/Armor/ITM_Armor_C1.asset",     2000,  1),
                Entry(ITEMS + "/Armor/ITM_Armor_C2.asset",     5000,  1),
                Entry(ITEMS + "/Armor/ITM_Armor_C3.asset",     11000, 2),
                Entry(ITEMS + "/Armor/ITM_Armor_C4.asset",     24000, 3),
                Entry(ITEMS + "/Armor/ITM_Armor_C5None.asset", 50000, 4),
                Entry(ITEMS + "/Armor/ITM_Helmet_C1.asset",    1600,  1),
                Entry(ITEMS + "/Armor/ITM_Helmet_C2.asset",    3600,  1),
                Entry(ITEMS + "/Armor/ITM_Helmet_C3.asset",    10000, 2),
                Entry(BACKPACKS + "/Backpack_Lv1.asset",       4000,  1),
                Entry(BACKPACKS + "/Backpack_Lv2.asset",       16000, 2),
                Entry(BACKPACKS + "/Backpack_Lv3.asset",       40000, 3),
            };
            SaveAsset(vera, TRADERS, "Trader_Vera");

            // ── Doc (의료상 — 추후 품목 추가) ──
            var doc = ScriptableObject.CreateInstance<TraderDataSO>();
            doc.traderName = "Doc";
            doc.sellMultiplier = 0.5f;
            doc.stock = new List<TraderEntry>();
            SaveAsset(doc, TRADERS, "Trader_Doc");

            // ── Shade (밀수업자 — 추후 품목 추가) ──
            var shade = ScriptableObject.CreateInstance<TraderDataSO>();
            shade.traderName = "Shade";
            shade.sellMultiplier = 0.5f;
            shade.stock = new List<TraderEntry>();
            SaveAsset(shade, TRADERS, "Trader_Shade");
        }

        // ═══════════════════════════════════════
        // 유틸리티
        // ═══════════════════════════════════════

        private static T Load<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogWarning($"[SOGenerator] 에셋 로드 실패: {path}");
            return asset;
        }

        private static void MakeRecipe(string folder, string fileName, string recipeID,
            ItemDataSO result, int count, int level, RarityTier tier,
            params (ItemDataSO item, int count)[] ingredients)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeSO>();
            recipe.recipeID = recipeID;
            recipe.result = result;
            recipe.resultCount = count;
            recipe.requiredFacilityLevel = level;
            recipe.requiredTier = tier;
            recipe.ingredients = new List<ItemRequirement>();

            foreach (var (item, qty) in ingredients)
                recipe.ingredients.Add(new ItemRequirement { item = item, amount = qty });

            if (result == null)
                Debug.LogWarning($"[SOGenerator] 레시피 '{recipeID}' result가 null — 경로 확인");

            SaveAsset(recipe, folder, fileName);
        }

        private static TraderEntry Entry(string path, int price, int commLv)
        {
            return new TraderEntry
            {
                item = Load<ItemDataSO>(path),
                basePrice = price,
                requiredCommLevel = commLv
            };
        }

        private static void SaveAsset(Object obj, string folder, string fileName)
        {
            string path = $"{folder}/{fileName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                Debug.Log($"[SOGenerator] 이미 존재 — 스킵: {path}");
                return;
            }
            AssetDatabase.CreateAsset(obj, path);
            Debug.Log($"[SOGenerator] 생성: {path}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.EditorTools
{
    
    public static class LootTableGenerator
    {
        private const string OUTPUT_FOLDER = "Assets/_Project/Data/LootTables";

        [MenuItem("Tools/DeadZone/Generate All LootTables")]
        public static void GenerateAll()
        {
            EnsureFolder(OUTPUT_FOLDER);

            var allItems = LoadAllItems();
            if (allItems.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "DeadZone LootTable Generator v2",
                    "ItemDataSO 자산이 1개도 없음.\n먼저 Tools/DeadZone/Generate All Items 실행 필요.",
                    "OK");
                return;
            }

            int created = 0;

            // ----- 등급 박스 3종 (모든 카테고리 혼합) -----
            created += BuildTable("LT_CommonBox", new[]
            {
                (RarityTier.Common, 80), (RarityTier.Uncommon, 18), (RarityTier.Rare, 2)
            }, allItems, categoryFilter: null);

            created += BuildTable("LT_UncommonBox", new[]
            {
                (RarityTier.Common, 40), (RarityTier.Uncommon, 50),
                (RarityTier.Rare, 9), (RarityTier.Epic, 1)
            }, allItems, categoryFilter: null);

            created += BuildTable("LT_RareBox", new[]
            {
                (RarityTier.Common, 10), (RarityTier.Uncommon, 30), (RarityTier.Rare, 45),
                (RarityTier.Epic, 13), (RarityTier.Legendary, 2)
            }, allItems, categoryFilter: null);

            // ----- 카테고리 케이스 4종 -----
            created += BuildTable("LT_WeaponCase", new[]
            {
                (RarityTier.Common, 35), (RarityTier.Uncommon, 45),
                (RarityTier.Rare, 18), (RarityTier.Epic, 2)
            }, allItems, categoryFilter: ItemCategory.Weapon);

            created += BuildTable("LT_MedicalCase", new[]
            {
                (RarityTier.Common, 60), (RarityTier.Uncommon, 32), (RarityTier.Rare, 8)
            }, allItems, categoryFilter: ItemCategory.Med);

            // DocCase = 귀중품(Valuable + isValuable=true) 풀. JADE 피규어 출현 가능.
            created += BuildTable("LT_DocCase", new[]
            {
                (RarityTier.Common, 45), (RarityTier.Uncommon, 35), (RarityTier.Rare, 17),
                (RarityTier.Epic, 2), (RarityTier.Legendary, 1)
            }, allItems, categoryFilter: ItemCategory.Valuable, valuableOnly: true);

            created += BuildTable("LT_AmmoCase", new[]
            {
                (RarityTier.Common, 60), (RarityTier.Uncommon, 33), (RarityTier.Rare, 7)
            }, allItems, categoryFilter: ItemCategory.Ammo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "DeadZone LootTable Generator v2",
                $"완료\n\n생성된 LootTable: {created}개 (박스 7종)\n\n" +
                "다음: 박스 7종 Inspector → Loot Table 필드에 드래그\n" +
                "  CommonBox    → LT_CommonBox\n" +
                "  UnCommonBox  → LT_UncommonBox\n" +
                "  RareBox      → LT_RareBox\n" +
                "  WaponCase    → LT_WeaponCase\n" +
                "  MedicalCase  → LT_MedicalCase\n" +
                "  DocCase      → LT_DocCase\n" +
                "  AmmoCase     → LT_AmmoCase",
                "OK");
        }

        // ----------- LootTable 빌드 -----------

        private static int BuildTable(
            string name,
            (RarityTier rarity, int weight)[] distribution,
            List<ItemDataSO> allItems,
            ItemCategory? categoryFilter,
            bool valuableOnly = false)
        {
            var entries = new List<LootEntry>();

            foreach (var (rarity, totalWeight) in distribution)
            {
                var candidates = allItems.Where(item =>
                {
                    if (item.rarity != rarity) return false;
                    if (categoryFilter.HasValue && item.category != categoryFilter.Value) return false;
                    if (valuableOnly && !item.isValuable) return false;
                    return true;
                }).ToList();

                if (candidates.Count == 0) continue;

                int weightPerItem = Mathf.Max(1, (totalWeight * 100) / candidates.Count);

                foreach (var item in candidates)
                {
                    entries.Add(new LootEntry
                    {
                        item = item,
                        weight = weightPerItem,
                        countRange = GetCountRange(item),
                    });
                }
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning($"[LootTableGenerator] '{name}' — 후보 0개. 자산 생성 스킵.");
                return 0;
            }

            string path = $"{OUTPUT_FOLDER}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LootTableSO>(path);
            LootTableSO table = existing != null ? existing : ScriptableObject.CreateInstance<LootTableSO>();

            table.entries = entries.ToArray();
            table.lockedZoneMultiplier = 1.5f;

            if (existing == null)
                AssetDatabase.CreateAsset(table, path);
            else
                EditorUtility.SetDirty(table);

            Debug.Log($"[LootTableGenerator] '{name}' — {entries.Count}개 엔트리 생성");
            return 1;
        }

        // ----------- 헬퍼 -----------

        private static Vector2Int GetCountRange(ItemDataSO item)
        {
            // 마스터 §3 기준 탄약 단일 카테고리 (LP/BP/AP 각 1종)
            return item.category switch
            {
                ItemCategory.Ammo => item.rarity switch
                {
                    RarityTier.Common   => new Vector2Int(30, 60),  // LP 30~60발
                    RarityTier.Uncommon => new Vector2Int(15, 30),  // BP 15~30발
                    RarityTier.Rare     => new Vector2Int(8, 15),   // AP 8~15발
                    _ => new Vector2Int(1, 1),
                },
                ItemCategory.Material => new Vector2Int(1, 3),
                ItemCategory.Med      => new Vector2Int(1, 2),
                _ => new Vector2Int(1, 1),
            };
        }

        private static List<ItemDataSO> LoadAllItems()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDataSO");
            return guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => AssetDatabase.LoadAssetAtPath<ItemDataSO>(p))
                .Where(so => so != null && !string.IsNullOrEmpty(so.itemID))
                .ToList();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
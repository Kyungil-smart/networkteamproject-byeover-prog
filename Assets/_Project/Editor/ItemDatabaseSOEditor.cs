#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using DeadZone.Core;
using DeadZone.Systems;
using UnityEditor;
using UnityEngine;

namespace DeadZone.Editor
{
    [CustomEditor(typeof(ItemDatabaseSO))]
    public class ItemDatabaseSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ItemDatabaseSO db = (ItemDatabaseSO)target;

            EditorGUILayout.HelpBox(
                $"Registered items: {db.allItems.Count}\n" +
                "Rebuild scans every ItemDataSO in the project and keeps specialized item data when IDs overlap.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Rebuild Item Database", GUILayout.Height(32)))
                Rebuild(db);

            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Validate Item Database"))
                Validate(db);

            EditorGUILayout.Space(12);
            DrawDefaultInspector();
        }

        private static void Rebuild(ItemDatabaseSO db)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDataSO", new[] { "Assets/_Project/Data" });

            Dictionary<string, ItemDataSO> uniqueMap = new();
            List<string> duplicates = new();
            List<string> legacyWeapons = new();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ItemDataSO item = AssetDatabase.LoadAssetAtPath<ItemDataSO>(path);
                if (item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(item.itemID))
                {
                    Debug.LogWarning($"[ItemDB Rebuild] Empty itemID: '{item.name}' ({path})", item);
                    continue;
                }

                if (IsLegacyWeaponShell(item))
                    legacyWeapons.Add($"  {item.itemID}: {path}");

                if (uniqueMap.TryGetValue(item.itemID, out ItemDataSO existing))
                {
                    duplicates.Add(
                        $"  itemID '{item.itemID}': '{existing.name}' vs '{item.name}' ({path})");

                    if (GetDataPriority(item) > GetDataPriority(existing))
                        uniqueMap[item.itemID] = item;

                    continue;
                }

                uniqueMap.Add(item.itemID, item);
            }

            Undo.RecordObject(db, "ItemDatabaseSO Rebuild");
            db.allItems = uniqueMap.Values
                .OrderByDescending(GetDataPriority)
                .ThenBy(item => item.itemID)
                .ToList();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            if (duplicates.Count > 0)
                Debug.Log(
                    $"[ItemDB Rebuild] Duplicate itemIDs found: {duplicates.Count}. " +
                    "Specialized data was kept when possible. " +
                    $"Samples:\n{string.Join("\n", duplicates.Take(10))}");

            if (legacyWeapons.Count > 0)
            {
                Debug.LogWarning(
                    "[ItemDB Rebuild] Legacy weapon shell ItemDataSO assets still exist. " +
                    "They should be removed from loot/search entries and replaced with WeaponDataSO assets. " +
                    $"Count={legacyWeapons.Count}. Samples:\n{string.Join("\n", legacyWeapons.Take(10))}");
            }

            Debug.Log(
                $"[ItemDB Rebuild] Complete. Scanned={guids.Length}, registered={db.allItems.Count}, " +
                $"duplicates={duplicates.Count}, legacyWeaponShells={legacyWeapons.Count}.",
                db);
        }

        private static void Validate(ItemDatabaseSO db)
        {
            Dictionary<string, ItemDataSO> seen = new();
            int duplicateCount = 0;
            int legacyWeaponCount = 0;

            foreach (ItemDataSO item in db.allItems)
            {
                if (item == null)
                {
                    Debug.LogWarning("[ItemDB Validate] Null entry found.", db);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.itemID))
                {
                    Debug.LogWarning($"[ItemDB Validate] Empty itemID: {item.name}", item);
                    continue;
                }

                if (seen.TryGetValue(item.itemID, out ItemDataSO existing))
                {
                    Debug.LogError(
                        $"[ItemDB Validate] Duplicate itemID '{item.itemID}': '{existing.name}' vs '{item.name}'",
                        item);
                    duplicateCount++;
                }
                else
                {
                    seen.Add(item.itemID, item);
                }

                if (IsLegacyWeaponShell(item))
                {
                    Debug.LogWarning(
                        $"[ItemDB Validate] Legacy weapon shell should be replaced with WeaponDataSO: {item.itemID}",
                        item);
                    legacyWeaponCount++;
                }
            }

            if (duplicateCount == 0 && legacyWeaponCount == 0)
                Debug.Log($"[ItemDB Validate] Passed. Entries={db.allItems.Count}.", db);
            else
                Debug.LogWarning(
                    $"[ItemDB Validate] Finished with duplicates={duplicateCount}, legacyWeaponShells={legacyWeaponCount}.",
                    db);
        }

        private static bool IsLegacyWeaponShell(ItemDataSO item)
        {
            return item != null &&
                   item.category == ItemCategory.Weapon &&
                   item is not WeaponDataSO;
        }

        private static int GetDataPriority(ItemDataSO item)
        {
            return item switch
            {
                WeaponDataSO => 100,
                ArmorDataSO => 90,
                HelmetDataSO => 90,
                BackpackDataSO => 90,
                AmmoDataSO => 80,
                _ => 0
            };
        }
    }
}
#endif

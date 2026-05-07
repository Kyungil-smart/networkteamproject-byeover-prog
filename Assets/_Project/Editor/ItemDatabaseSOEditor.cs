#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Editor
{
    [CustomEditor(typeof(ItemDatabaseSO))]
    public class ItemDatabaseSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var db = (ItemDatabaseSO)target;

            EditorGUILayout.HelpBox(
                $"현재 등록: {db.allItems.Count}개\n" +
                "아래 버튼으로 프로젝트 전체 ItemDataSO를 자동 수집합니다.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // ── Rebuild 버튼 ──
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("▶  Rebuild (전체 자동 수집)", GUILayout.Height(32)))
            {
                Rebuild(db);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);

            // ── Validate 버튼 ──
            if (GUILayout.Button("✓  Validate (중복 검사만)"))
            {
                Validate(db);
            }

            EditorGUILayout.Space(12);
            DrawDefaultInspector();
        }

        private void Rebuild(ItemDatabaseSO db)
        {
            // 프로젝트 전체에서 ItemDataSO와 모든 하위 타입 검색
            string[] guids = AssetDatabase.FindAssets("t:ItemDataSO");

            var uniqueMap = new Dictionary<string, ItemDataSO>();
            var duplicates = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDataSO>(path);
                if (item == null) continue;

                if (string.IsNullOrEmpty(item.itemID))
                {
                    Debug.LogWarning(
                        $"[ItemDB Rebuild] itemID 비어있음: '{item.name}' ({path})");
                    continue;
                }

                if (uniqueMap.ContainsKey(item.itemID))
                {
                    var existing = uniqueMap[item.itemID];
                    duplicates.Add(
                        $"  itemID '{item.itemID}': " +
                        $"'{existing.name}' vs '{item.name}' ({path})");
                    continue;
                }

                uniqueMap.Add(item.itemID, item);
            }

            // 결과 적용
            Undo.RecordObject(db, "ItemDatabaseSO Rebuild");
            db.allItems = uniqueMap.Values
                .OrderBy(i => i.itemID)
                .ToList();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            // 결과 로그
            if (duplicates.Count > 0)
            {
                Debug.LogError(
                    $"[ItemDB Rebuild] 중복 {duplicates.Count}건 발견 " +
                    $"(첫 등록만 유지):\n{string.Join("\n", duplicates)}");
            }

            Debug.Log(
                $"[ItemDB Rebuild] 완료: {guids.Length}개 스캔 → " +
                $"{db.allItems.Count}개 등록, {duplicates.Count}개 중복 제외.");
        }

        private void Validate(ItemDatabaseSO db)
        {
            var seen = new Dictionary<string, string>();
            int dupeCount = 0;

            foreach (var item in db.allItems)
            {
                if (item == null)
                {
                    Debug.LogWarning("[ItemDB Validate] null 항목 발견.");
                    continue;
                }
                if (seen.TryGetValue(item.itemID, out var existingName))
                {
                    Debug.LogError(
                        $"[ItemDB Validate] 중복: '{item.itemID}' " +
                        $"— '{existingName}' vs '{item.name}'");
                    dupeCount++;
                }
                else
                {
                    seen.Add(item.itemID, item.name);
                }
            }

            if (dupeCount == 0)
                Debug.Log($"[ItemDB Validate] 통과. {db.allItems.Count}개 모두 고유.");
            else
                Debug.LogError($"[ItemDB Validate] 중복 {dupeCount}건. Rebuild 실행 권장.");
        }
    }
}
#endif
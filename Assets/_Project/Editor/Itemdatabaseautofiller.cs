#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.EditorTools
{
    public static class ItemDatabaseAutoFiller
    {
        [MenuItem("Tools/DeadZone/Refresh ItemDatabase")]
        public static void Refresh()
        {
            // 1. 모든 ItemDataSO 검색
            string[] guids = AssetDatabase.FindAssets("t:ItemDataSO");
            var items = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => AssetDatabase.LoadAssetAtPath<ItemDataSO>(p))
                .Where(so => so != null && !string.IsNullOrEmpty(so.itemID))
                .ToArray();

            if (items.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "ItemDatabase Auto Filler",
                    "ItemDataSO 자산이 1개도 없음.\n먼저 Tools/DeadZone/Generate All Items 실행 필요.",
                    "OK");
                return;
            }

            // 중복 itemID 검사
            var duplicates = items
                .GroupBy(so => so.itemID)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
            {
                Debug.LogWarning($"[ItemDatabase] 중복 itemID 발견: {string.Join(", ", duplicates)}");
            }

            // 2. 모든 열린 씬에서 ItemDatabase 찾기
            ItemDatabase db = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    db = root.GetComponentInChildren<ItemDatabase>(includeInactive: true);
                    if (db != null) break;
                }
                if (db != null) break;
            }

            if (db == null)
            {
                EditorUtility.DisplayDialog(
                    "ItemDatabase Auto Filler",
                    "현재 열린 씬에 ItemDatabase 컴포넌트가 없음.\n\n" +
                    "해결:\n1. PersistentSystems 또는 Game_Scene 열기\n" +
                    "2. Empty GameObject 만들고 ItemDatabase 컴포넌트 추가\n" +
                    "3. 다시 이 메뉴 실행",
                    "OK");
                return;
            }

            // 3. SerializedObject로 allItems 필드 채우기
            var serialized = new SerializedObject(db);
            var arrProp = serialized.FindProperty("allItems");

            if (arrProp == null)
            {
                EditorUtility.DisplayDialog(
                    "ItemDatabase Auto Filler",
                    "ItemDatabase.allItems 필드를 찾을 수 없음.\n" +
                    "ItemDatabase.cs가 최신 패치 버전인지 확인.",
                    "OK");
                return;
            }

            arrProp.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                arrProp.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
            serialized.ApplyModifiedProperties();

            // 4. 씬 dirty 마킹
            EditorUtility.SetDirty(db);
            EditorSceneManager.MarkSceneDirty(db.gameObject.scene);

            EditorUtility.DisplayDialog(
                "ItemDatabase Auto Filler",
                $"완료\n\nItemDatabase.allItems에 {items.Length}개 ItemDataSO 등록됨\n" +
                $"중복 ID: {duplicates.Length}개\n\n" +
                "씬 저장 (Ctrl+S) 후 Play 시 정상 동작",
                "OK");

            Debug.Log($"[ItemDatabase] {items.Length}개 ItemDataSO 자동 등록 완료. " +
                      $"씬 '{db.gameObject.scene.name}' dirty.");
        }
    }
}
#endif
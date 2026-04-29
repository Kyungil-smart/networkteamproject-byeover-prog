#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.EditorTools
{
    public static class CleanupTool
    {
        [MenuItem("Tools/DeadZone/Cleanup v1 Assets")]
        public static void Cleanup()
        {
            // ----- 사전 카운트 -----

            int itemCount = AssetDatabase.FindAssets("t:ItemDataSO").Length;
            int tableCount = AssetDatabase.FindAssets("t:LootTableSO").Length;

            if (itemCount == 0 && tableCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "DeadZone Cleanup",
                    "삭제할 자산이 없음. 이미 깨끗한 상태.",
                    "OK");
                return;
            }

            // ----- 확인 다이얼로그 -----

            bool confirmed = EditorUtility.DisplayDialog(
                "v1 자산 삭제 확인",
                $"다음 자산을 영구 삭제합니다:\n\n" +
                $"  - ItemDataSO: {itemCount}개\n" +
                $"  - LootTable: {tableCount}개\n" +
                $"  - 빈 폴더: Backpacks, Food, Keys, QuestItems\n\n" +
                $"계속하시겠습니까?\n" +
                $"(다음 단계: ItemGenerator v2로 68종 재생성)",
                "삭제",
                "취소");

            if (!confirmed) return;

            // ----- 삭제 실행 -----

            int deletedItems = DeleteAllOfType<ItemDataSO>();
            int deletedTables = DeleteAllOfType<LootTableSO>();
            int deletedFolders = DeleteEmptyFolders();

            // ----- ItemDatabase.allItems 비우기 -----

            int clearedDb = ClearItemDatabase();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "DeadZone Cleanup",
                $"정리 완료\n\n" +
                $"삭제된 ItemDataSO: {deletedItems}개\n" +
                $"삭제된 LootTable: {deletedTables}개\n" +
                $"삭제된 빈 폴더: {deletedFolders}개\n" +
                $"ItemDatabase 클리어: {clearedDb}개 슬롯\n\n" +
                $"다음 단계:\n" +
                $"1. Tools/DeadZone/Generate All Items\n" +
                $"2. Tools/DeadZone/Refresh ItemDatabase\n" +
                $"3. Tools/DeadZone/Generate All LootTables",
                "OK");
        }

        // ----------- 헬퍼 -----------

        private static int DeleteAllOfType<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            int count = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.DeleteAsset(path)) count++;
            }
            return count;
        }

        private static int DeleteEmptyFolders()
        {
            // v1으로 생성된 폴더 중 v2에서 사용 안 하는 것들
            string[] obsoleteFolders = new[]
            {
                "Assets/_Project/Data/Items/Backpacks",
                "Assets/_Project/Data/Items/Food",
                "Assets/_Project/Data/Items/Keys",
                "Assets/_Project/Data/Items/QuestItems",
            };

            int count = 0;
            foreach (var folder in obsoleteFolders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    if (AssetDatabase.DeleteAsset(folder)) count++;
                }
            }
            return count;
        }

        private static int ClearItemDatabase()
        {
            var dbs = Object.FindObjectsByType<DeadZone.Systems.ItemDatabase>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int totalCleared = 0;
            foreach (var db in dbs)
            {
                var serialized = new SerializedObject(db);
                var arr = serialized.FindProperty("allItems");
                if (arr == null) continue;

                int prevSize = arr.arraySize;
                arr.arraySize = 0;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(db);
                totalCleared += prevSize;
            }
            return totalCleared;
        }
    }
}
#endif
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
        // 화이트리스트 — 이 폴더 안의 자산만 삭제 가능
        private static readonly string[] SAFE_DELETE_FOLDERS = new[]
        {
            "Assets/_Project/Data/Items/Materials",
            "Assets/_Project/Data/Items/Medical",
            "Assets/_Project/Data/Items/Valuables",
            "Assets/_Project/Data/Items/Tools",
            "Assets/_Project/Data/Items/Armor",
            "Assets/_Project/Data/Items/Misc",
            "Assets/_Project/Data/LootTables",
        };

        // 옛날 v1/v2 폴더 — 비어있으면 같이 삭제
        private static readonly string[] OBSOLETE_FOLDERS = new[]
        {
            "Assets/_Project/Data/Items/Backpacks",
            "Assets/_Project/Data/Items/Food",
            "Assets/_Project/Data/Items/Keys",
            "Assets/_Project/Data/Items/QuestItems",
            "Assets/_Project/Data/Items/Weapons",
            "Assets/_Project/Data/Items/Ammo",
            "Assets/_Project/Data/Items/Helmets",
            // 등급별 폴더 (v1.3 잔재)
            "Assets/_Project/Data/Items/Common",   // ⚠️ 팀원 폴더와 동일 — 보호됨
            "Assets/_Project/Data/Items/Uncommon",
            "Assets/_Project/Data/Items/Rare",
            "Assets/_Project/Data/Items/Epic",
            "Assets/_Project/Data/Items/Legendary",
        };

        [MenuItem("Tools/DeadZone/Cleanup Farming Items (v2)")]
        public static void Cleanup()
        {
            // 1. 안전 영역의 삭제 대상 자산 미리 수집
            var toDelete = new List<string>();
            var blocked = new List<string>();

            // ItemDataSO 검색
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDataSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsSafeToDelete(path)) toDelete.Add(path);
                else blocked.Add(path);
            }

            // LootTableSO 검색
            foreach (var guid in AssetDatabase.FindAssets("t:LootTableSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsSafeToDelete(path)) toDelete.Add(path);
                else blocked.Add(path);
            }

            if (toDelete.Count == 0 && blocked.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "DeadZone Cleanup v2",
                    "삭제할 자산이 없음. 깨끗한 상태.",
                    "OK");
                return;
            }

            // 2. 콘솔에 미리 보기 출력
            Debug.Log($"[CleanupTool v2] === 삭제 대상 ({toDelete.Count}개) ===");
            foreach (var p in toDelete) Debug.Log($"  [DELETE] {p}");

            if (blocked.Count > 0)
            {
                Debug.Log($"[CleanupTool v2] === 보호됨 - 절대 안 삭제 ({blocked.Count}개) ===");
                foreach (var p in blocked) Debug.Log($"  [PROTECTED] {p}");
            }

            // 3. 확인 다이얼로그
            string message = $"다음 자산을 영구 삭제합니다:\n\n" +
                             $"  - 삭제 대상: {toDelete.Count}개 (너 파밍팀 자산)\n";

            if (blocked.Count > 0)
            {
                message += $"  - 보호됨: {blocked.Count}개 (팀원 자산 — 절대 안 건드림)\n";
            }

            message += $"\n자세한 목록은 Console에서 확인하세요.\n계속하시겠습니까?";

            bool confirmed = EditorUtility.DisplayDialog(
                "파밍팀 자산 삭제 확인",
                message,
                "삭제 진행",
                "취소");

            if (!confirmed) return;

            // 4. 삭제 실행
            int deleted = 0;
            foreach (var path in toDelete)
            {
                if (AssetDatabase.DeleteAsset(path)) deleted++;
            }

            // 5. 옛날 폴더 정리 (비어있는 것만)
            int folderDeleted = 0;
            foreach (var folder in OBSOLETE_FOLDERS)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                // ⚠️ Common/ 폴더는 팀원 영역 — 절대 건드리지 않음
                if (folder.EndsWith("/Common")) continue;

                // 폴더가 비어있는지 확인
                var subAssets = AssetDatabase.FindAssets("", new[] { folder });
                if (subAssets.Length == 0)
                {
                    if (AssetDatabase.DeleteAsset(folder)) folderDeleted++;
                }
            }

            // 6. ItemDatabase 비우기
            int dbCleared = ClearItemDatabase();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "DeadZone Cleanup v2",
                $"정리 완료\n\n" +
                $"삭제된 자산: {deleted}개\n" +
                $"삭제된 빈 폴더: {folderDeleted}개\n" +
                $"보호된 자산 (팀원 영역): {blocked.Count}개\n" +
                $"ItemDatabase 클리어: {dbCleared}개 슬롯\n\n" +
                $"다음 단계:\n" +
                $"1. Tools/DeadZone/Generate Farming Items (v3)\n" +
                $"2. Tools/DeadZone/Refresh ItemDatabase\n" +
                $"3. Tools/DeadZone/Generate All LootTables (v2)",
                "OK");
        }

        // ----------- 헬퍼 -----------

        /// <summary>경로가 화이트리스트 안에 있는지 검사.</summary>
        private static bool IsSafeToDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // 화이트리스트의 어느 폴더에든 속하면 OK
            return SAFE_DELETE_FOLDERS.Any(safe => path.StartsWith(safe + "/"));
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
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using DeadZone.Actors.UI;
using UnityEditor;
using UnityEngine;

namespace DeadZone.EditorTools
{
    public static class LoadingTipImporter
    {
        private const string TsvPath = "Assets/_Project/Data/LoadingTips.tsv";
        private const string DatabasePath = "Assets/_Project/ScriptableObjects/UI/LoadingTipDatabase.asset";

        [MenuItem("DeadZone/UI/Import Loading Tips From TSV")]
        public static void Import()
        {
            if (!File.Exists(TsvPath))
            {
                Debug.LogError($"[LoadingTipImporter] TSV 파일을 찾을 수 없습니다: {TsvPath}");
                return;
            }

            EnsureFolder("Assets/_Project/ScriptableObjects");
            EnsureFolder("Assets/_Project/ScriptableObjects/UI");

            LoadingTipDatabase database =
                AssetDatabase.LoadAssetAtPath<LoadingTipDatabase>(DatabasePath);

            if (database == null)
            {
                database = ScriptableObject.CreateInstance<LoadingTipDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            string[] lines = File.ReadAllLines(TsvPath, Encoding.UTF8);

            database.tips.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] columns = line.Split('\t');

                if (columns.Length < 2)
                {
                    Debug.LogWarning($"[LoadingTipImporter] {i + 1}번째 줄 형식 오류: {line}");
                    continue;
                }

                string categoryText = columns[0].Trim();
                string message = columns[1].Trim();
                string imageKey = columns.Length >= 3 && !string.IsNullOrWhiteSpace(columns[2])
                    ? columns[2].Trim()
                    : "Default";

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                if (!Enum.TryParse(categoryText, out LoadingTipCategory category))
                {
                    Debug.LogWarning($"[LoadingTipImporter] 알 수 없는 카테고리: {categoryText} / 줄 {i + 1}");
                    continue;
                }

                database.tips.Add(new LoadingTipEntry
                {
                    category = category,
                    message = message,
                    imageKey = imageKey
                });
            }

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LoadingTipImporter] 로딩 팁 {database.tips.Count}개를 가져왔습니다.");
            Selection.activeObject = database;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folderName = Path.GetFileName(path);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
                return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif

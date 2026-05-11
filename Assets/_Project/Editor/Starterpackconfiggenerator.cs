#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.EditorTools
{
    /// <summary>
    /// 파산신청용 스타터팩 설정 에셋을 생성하고 기본값을 자동으로 채우는 에디터 도구입니다.
    /// </summary>
    public static class StarterPackConfigGenerator
    {
        private const string TargetFolder = "Assets/_Project/Data/StarterPacks";
        private const string TargetAssetPath = TargetFolder + "/StarterPack_Bankruptcy.asset";
        private const int StartingCredits = 50000;

        private static readonly StarterPackItemDef[] BankruptcyStarterPackItems =
        {
            new StarterPackItemDef("Assets/_Project/Data/Armor/Armor_C4.asset", 1),
            new StarterPackItemDef("Assets/_Project/Data/Armor/Armor_C3.asset", 2),
            new StarterPackItemDef("Assets/_Project/Data/Helmets/Helmet_C3.asset", 1),
            new StarterPackItemDef("Assets/_Project/Data/Helmets/Helmet_C2.asset", 2),
            new StarterPackItemDef("Assets/_Project/Data/Weapons/Weapon_PumpSG.asset", 1),
            new StarterPackItemDef("Assets/_Project/Data/Weapons/Weapon_Glock17.asset", 2),
            new StarterPackItemDef("Assets/_Project/Data/Weapons/Weapon_Revolver.asset", 1),
            new StarterPackItemDef("Assets/_Project/Data/Weapons/Weapon_SelfDefense.asset", 2),
            new StarterPackItemDef("Assets/_Project/Data/Items/Medical/ITM_FirstAidKit.asset", 3),
            new StarterPackItemDef("Assets/_Project/Data/Ammo/Ammo_SG_BP.asset", 120),
            new StarterPackItemDef("Assets/_Project/Data/Ammo/Ammo_Handgun_BP.asset", 240),
        };

        /// <summary>
        /// 파산신청용 스타터팩 설정 에셋을 생성하거나 갱신합니다.
        /// </summary>
        [MenuItem("Tools/DeadZone/Starter Pack/Create Bankruptcy Starter Pack")]
        public static void CreateBankruptcyStarterPack()
        {
            StarterPackConfigSO config = CreateOrRefreshBankruptcyStarterPack();
            if (config == null)
                return;

            SelectAndPing(config);
        }

        /// <summary>
        /// 파산신청용 스타터팩 설정 에셋을 생성한 뒤 현재 열려 있는 씬의 관련 컴포넌트에 자동 연결합니다.
        /// </summary>
        [MenuItem("Tools/DeadZone/Starter Pack/Create And Assign Bankruptcy Starter Pack")]
        public static void CreateAndAssignBankruptcyStarterPack()
        {
            StarterPackConfigSO config = CreateOrRefreshBankruptcyStarterPack();
            if (config == null)
                return;

            int assignedCount = AssignToOpenScenes(config);
            SelectAndPing(config);

            EditorUtility.DisplayDialog(
                "Bankruptcy Starter Pack",
                $"스타터팩 생성/갱신 완료\n\n경로: {TargetAssetPath}\n현재 열린 씬 자동 연결: {assignedCount}개",
                "OK");
        }

        private static StarterPackConfigSO CreateOrRefreshBankruptcyStarterPack()
        {
            EnsureFolder(TargetFolder);

            StarterPackConfigSO config = AssetDatabase.LoadAssetAtPath<StarterPackConfigSO>(TargetAssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<StarterPackConfigSO>();
                AssetDatabase.CreateAsset(config, TargetAssetPath);
            }

            if (!TryFillConfig(config, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Bankruptcy Starter Pack", errorMessage, "OK");
                return null;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StarterPackConfigGenerator] 파산신청 스타터팩 생성/갱신 완료: {TargetAssetPath}", config);
            return config;
        }

        private static bool TryFillConfig(StarterPackConfigSO config, out string errorMessage)
        {
            List<string> missingPaths = new List<string>();
            SerializedObject serializedObject = new SerializedObject(config);
            SerializedProperty creditsProperty = serializedObject.FindProperty("startingCredits");
            SerializedProperty entriesProperty = serializedObject.FindProperty("entries");

            if (creditsProperty == null || entriesProperty == null)
            {
                errorMessage = "StarterPackConfigSO의 직렬화 필드를 찾을 수 없습니다. 스크립트 필드명이 변경됐는지 확인하세요.";
                return false;
            }

            creditsProperty.intValue = StartingCredits;
            entriesProperty.arraySize = 0;

            for (int i = 0; i < BankruptcyStarterPackItems.Length; i++)
            {
                StarterPackItemDef itemDef = BankruptcyStarterPackItems[i];
                ItemDataSO item = AssetDatabase.LoadAssetAtPath<ItemDataSO>(itemDef.AssetPath);
                if (item == null)
                {
                    missingPaths.Add(itemDef.AssetPath);
                    continue;
                }

                entriesProperty.InsertArrayElementAtIndex(entriesProperty.arraySize);
                SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entriesProperty.arraySize - 1);

                entryProperty.FindPropertyRelative("item").objectReferenceValue = item;
                entryProperty.FindPropertyRelative("amount").intValue = itemDef.Amount;
                entryProperty.FindPropertyRelative("durabilityRatio").floatValue = 1f;
                entryProperty.FindPropertyRelative("currentAmmo").intValue = 0;
            }

            if (missingPaths.Count > 0)
            {
                serializedObject.Dispose();
                errorMessage = "아래 스타터팩 아이템 에셋을 찾을 수 없어 생성을 중단했습니다.\n\n" +
                               string.Join("\n", missingPaths);
                return false;
            }

            serializedObject.ApplyModifiedProperties();
            serializedObject.Dispose();

            errorMessage = string.Empty;
            return true;
        }

        private static int AssignToOpenScenes(StarterPackConfigSO config)
        {
            int assignedCount = 0;
            assignedCount += AssignConfigToComponents<CloudSaveSystem>("bankruptcyStarterPack", config);
            assignedCount += AssignConfigToComponents<SettingPopupUI>("starterPackConfig", config);
            return assignedCount;
        }

        private static int AssignConfigToComponents<T>(string propertyName, StarterPackConfigSO config)
            where T : Component
        {
            int assignedCount = 0;
            T[] components = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component == null || !IsSceneObject(component))
                    continue;

                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty property = serializedObject.FindProperty(propertyName);

                if (property == null)
                {
                    serializedObject.Dispose();
                    continue;
                }

                if (property.objectReferenceValue == config)
                {
                    serializedObject.Dispose();
                    continue;
                }

                property.objectReferenceValue = config;
                serializedObject.ApplyModifiedProperties();
                serializedObject.Dispose();

                EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                assignedCount++;
            }

            return assignedCount;
        }

        private static bool IsSceneObject(Component component)
        {
            Scene scene = component.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static void SelectAndPing(StarterPackConfigSO config)
        {
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folderName = Path.GetFileName(path);

            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(folderName))
                return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private readonly struct StarterPackItemDef
        {
            public StarterPackItemDef(string assetPath, int amount)
            {
                AssetPath = assetPath;
                Amount = amount;
            }

            public string AssetPath { get; }
            public int Amount { get; }
        }
    }
}
#endif

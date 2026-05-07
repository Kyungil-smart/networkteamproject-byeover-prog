#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace DeadZone.Core
{

    public class WeaponPrefabCreator : EditorWindow
    {
        private WeaponDataSO[] weaponSOs;
        private GameObject[] weaponModels;
        private string outputFolder = "Assets/_Project/Prefabs/Weapons";
        private Vector2 scrollPos;

        [MenuItem("Tools/DeadZone/무기 프리팹 생성기")]
        public static void ShowWindow()
        {
            var window = GetWindow<WeaponPrefabCreator>("무기 프리팹 생성기");
            window.minSize = new Vector2(500, 500);
        }

        private void OnEnable()
        {
            LoadAllWeaponSOs();
        }

        /// <summary>프로젝트 내 모든 WeaponDataSO 자동 탐색</summary>
        private void LoadAllWeaponSOs()
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponDataSO");
            weaponSOs = new WeaponDataSO[guids.Length];
            weaponModels = new GameObject[guids.Length];

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                weaponSOs[i] = AssetDatabase.LoadAssetAtPath<WeaponDataSO>(path);
            }

            TryAutoMatchModels();
        }

        /// <summary>이름 기반 모델 자동 매칭</summary>
        private void TryAutoMatchModels()
        {
            for (int i = 0; i < weaponSOs.Length; i++)
            {
                if (weaponSOs[i] == null) continue;

                string modelName = weaponSOs[i].itemID.Replace("Weapon_", "");
                string[] modelGuids = AssetDatabase.FindAssets(
                    $"{modelName} t:GameObject", new[] { "Assets" });

                if (modelGuids.Length > 0)
                {
                    string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
                    weaponModels[i] = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("무기 프리팹 생성기 (손에 드는 무기)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "3D 모델 + MuzzlePoint(총구 위치)로 구성된 무기 프리팹을 생성합니다.\n" +
                "생성 후 각 프리팹의 MuzzlePoint 위치를 총구 끝에 맞게 조정하세요.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // 출력 폴더
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("출력 폴더", outputFolder);
            if (GUILayout.Button("선택", GUILayout.Width(50)))
            {
                string selected = EditorUtility.OpenFolderPanel("프리팹 출력 폴더", "Assets", "");
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath))
                    outputFolder = "Assets" + selected.Substring(Application.dataPath.Length);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("SO 다시 검색"))
                LoadAllWeaponSOs();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField($"무기 목록 ({weaponSOs?.Length ?? 0}개)", EditorStyles.boldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            if (weaponSOs != null)
            {
                for (int i = 0; i < weaponSOs.Length; i++)
                {
                    if (weaponSOs[i] == null) continue;
                    DrawWeaponEntry(i);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // 전체 생성
            GUI.color = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("모든 무기 프리팹 일괄 생성", GUILayout.Height(35)))
                CreateAllPrefabs();
            GUI.color = Color.white;
        }

        /// <summary>무기 항목 1줄 그리기</summary>
        private void DrawWeaponEntry(int i)
        {
            EditorGUILayout.BeginVertical("box");

            string catName = GetCategoryName((int)weaponSOs[i].weaponCategory);
            bool hasPrefab = weaponSOs[i].worldPrefab != null;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"{weaponSOs[i].displayName} [{catName}]",
                EditorStyles.boldLabel, GUILayout.Width(200));

            GUI.color = hasPrefab ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(hasPrefab ? "✓ 완료" : "✗ 미생성", GUILayout.Width(80));
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            weaponModels[i] = (GameObject)EditorGUILayout.ObjectField(
                "3D 모델", weaponModels[i], typeof(GameObject), false);

            EditorGUI.BeginDisabledGroup(weaponModels[i] == null);
            if (GUILayout.Button("프리팹 생성"))
                CreateWeaponPrefab(weaponSOs[i], weaponModels[i]);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // ───────── 프리팹 생성 ─────────

        /// <summary>
        /// 손에 드는 무기 프리팹을 생성한다.
        /// 구조: Root → Model (3D 메시) + MuzzlePoint (총구 위치)
        /// </summary>
        private void CreateWeaponPrefab(WeaponDataSO so, GameObject model)
        {
            if (so == null || model == null) return;

            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            string prefabName = $"WP_{so.itemID}";
            GameObject root = new GameObject(prefabName);

            // ── 1. 모델 배치 ──
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (modelInstance == null)
                modelInstance = Instantiate(model);

            modelInstance.transform.SetParent(root.transform);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.name = "Model";

            // ── 2. MuzzlePoint 생성 (총구 끝 위치) ──
            GameObject muzzlePoint = new GameObject("MuzzlePoint");
            muzzlePoint.transform.SetParent(root.transform);

            // 모델 바운드 기준으로 총구 위치 자동 추정 (앞쪽 끝)
            Renderer rend = modelInstance.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds bounds = rend.bounds;
                // Z+ 방향이 전방이라고 가정 → 바운드 앞쪽 끝에 배치
                Vector3 muzzlePos = new Vector3(
                    0f,
                    bounds.center.y - root.transform.position.y,
                    bounds.max.z - root.transform.position.z + 0.05f
                );
                muzzlePoint.transform.localPosition = muzzlePos;
            }
            else
            {
                muzzlePoint.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            }

            // ── 3. 프리팹 저장 ──
            string prefabPath = $"{outputFolder}/{prefabName}.prefab";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            DestroyImmediate(root);

            // ── 4. SO에 할당 ──
            if (savedPrefab != null)
            {
                SerializedObject serializedSO = new SerializedObject(so);
                SerializedProperty prop = serializedSO.FindProperty("worldPrefab");
                prop.objectReferenceValue = savedPrefab;
                serializedSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(so);

                Debug.Log($"[WeaponPrefabCreator] ✓ {so.displayName} → {prefabPath}");
            }
        }

        /// <summary>전체 일괄 생성</summary>
        private void CreateAllPrefabs()
        {
            int created = 0, skipped = 0;

            for (int i = 0; i < weaponSOs.Length; i++)
            {
                if (weaponSOs[i] == null || weaponModels[i] == null) { skipped++; continue; }
                CreateWeaponPrefab(weaponSOs[i], weaponModels[i]);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("완료",
                $"생성: {created}개 / 건너뜀: {skipped}개\n" +
                "각 프리팹의 MuzzlePoint 위치를 총구 끝에 맞게 조정하세요.",
                "확인");
        }

        private string GetCategoryName(int cat)
        {
            return cat switch
            {
                0 => "AR", 1 => "SMG", 2 => "권총", 3 => "SR", 4 => "SG", _ => "기타"
            };
        }
    }
}
#endif
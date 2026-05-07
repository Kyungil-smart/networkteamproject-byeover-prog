#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace DeadZone.Core
{
    public class WeaponPrefabCreator : EditorWindow
    {
        // ───────── 입력 필드 ─────────

        private WeaponDataSO[] weaponSOs;
        private GameObject[] weaponModels;
        private string outputFolder = "Assets/_Project/Prefabs/Weapons";
        private Vector2 scrollPos;
        private bool autoMatchByName = true;

        // ───────── 프리팹 설정 ─────────

        private bool addBoxCollider = true;
        private bool addRigidbody = true;
        private LayerMask itemLayer;
        private string itemTag = "Item";

        [MenuItem("Tools/DeadZone/무기 프리팹 생성기")]
        public static void ShowWindow()
        {
            var window = GetWindow<WeaponPrefabCreator>("무기 프리팹 생성기");
            window.minSize = new Vector2(500, 600);
        }

        private void OnEnable()
        {
            LoadAllWeaponSOs();
        }

        /// <summary>프로젝트 내 모든 WeaponDataSO를 자동 탐색</summary>
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

            if (autoMatchByName)
                TryAutoMatchModels();
        }

        /// <summary>이름 기반으로 모델 자동 매칭 시도</summary>
        private void TryAutoMatchModels()
        {
            for (int i = 0; i < weaponSOs.Length; i++)
            {
                if (weaponSOs[i] == null) continue;

                string itemId = weaponSOs[i].itemID;

                // 이름 패턴으로 모델 검색: "Weapon_Glock17" → "Glock17" 검색
                string modelName = itemId.Replace("Weapon_", "");
                string[] modelGuids = AssetDatabase.FindAssets(
                    $"{modelName} t:GameObject",
                    new[] { "Assets" });

                if (modelGuids.Length > 0)
                {
                    string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
                    weaponModels[i] = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                }
            }
        }

        // ───────── UI ─────────

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("무기 프리팹 생성기", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "WeaponDataSO마다 3D 모델을 연결하고 프리팹을 자동 생성합니다.\n" +
                "worldPrefab 필드에 자동으로 할당됩니다.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // 출력 폴더
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("출력 폴더", outputFolder);
            if (GUILayout.Button("선택", GUILayout.Width(50)))
            {
                string selected = EditorUtility.OpenFolderPanel("프리팹 출력 폴더", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                        outputFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 프리팹 설정
            EditorGUILayout.LabelField("프리팹 컴포넌트 설정", EditorStyles.boldLabel);
            addBoxCollider = EditorGUILayout.Toggle("BoxCollider 추가", addBoxCollider);
            addRigidbody = EditorGUILayout.Toggle("Rigidbody 추가 (픽업용)", addRigidbody);

            EditorGUILayout.Space(5);

            // 자동 매칭
            EditorGUILayout.BeginHorizontal();
            autoMatchByName = EditorGUILayout.Toggle("이름 자동 매칭", autoMatchByName);
            if (GUILayout.Button("다시 검색", GUILayout.Width(80)))
                LoadAllWeaponSOs();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 무기 목록
            EditorGUILayout.LabelField($"무기 목록 ({weaponSOs?.Length ?? 0}개)", EditorStyles.boldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            if (weaponSOs != null)
            {
                for (int i = 0; i < weaponSOs.Length; i++)
                {
                    if (weaponSOs[i] == null) continue;

                    EditorGUILayout.BeginVertical("box");

                    // SO 이름 + 카테고리
                    string catName = GetCategoryName((int)weaponSOs[i].weaponCategory);
                    bool hasWorldPrefab = weaponSOs[i].worldPrefab != null;
                    string status = hasWorldPrefab ? "✓ 프리팹 있음" : "✗ 프리팹 없음";
                    Color statusColor = hasWorldPrefab ? Color.green : Color.yellow;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        $"{weaponSOs[i].displayName} [{catName}]",
                        EditorStyles.boldLabel, GUILayout.Width(200));

                    GUI.color = statusColor;
                    EditorGUILayout.LabelField(status, GUILayout.Width(100));
                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();

                    // 모델 슬롯
                    weaponModels[i] = (GameObject)EditorGUILayout.ObjectField(
                        "3D 모델", weaponModels[i], typeof(GameObject), false);

                    // 개별 생성 버튼
                    EditorGUI.BeginDisabledGroup(weaponModels[i] == null);
                    if (GUILayout.Button("이 무기 프리팹 생성"))
                    {
                        CreateWeaponPrefab(weaponSOs[i], weaponModels[i]);
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // 전체 생성 버튼
            GUI.color = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("모든 무기 프리팹 일괄 생성", GUILayout.Height(35)))
            {
                CreateAllPrefabs();
            }
            GUI.color = Color.white;
        }

        // ───────── 프리팹 생성 ─────────

        /// <summary>개별 무기 프리팹 생성</summary>
        private void CreateWeaponPrefab(WeaponDataSO so, GameObject model)
        {
            if (so == null || model == null)
            {
                Debug.LogError("[WeaponPrefabCreator] SO 또는 모델이 null");
                return;
            }

            // 출력 폴더 확인/생성
            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            // 루트 오브젝트 생성
            string prefabName = $"WP_{so.itemID}";
            GameObject root = new GameObject(prefabName);

            // 모델 인스턴스 생성 (자식으로)
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (modelInstance == null)
                modelInstance = Instantiate(model);

            modelInstance.transform.SetParent(root.transform);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.name = "Model";

            // 콜라이더 추가
            if (addBoxCollider)
            {
                var col = root.AddComponent<BoxCollider>();
                // 모델 바운드에 맞게 콜라이더 자동 조정
                Renderer rend = modelInstance.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    col.center = root.transform.InverseTransformPoint(rend.bounds.center);
                    col.size = rend.bounds.size;
                }
            }

            // 리지드바디 추가 (바닥에 드롭되는 픽업 아이템용)
            if (addRigidbody)
            {
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = so.weightKg > 0 ? so.weightKg : 1f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            // 프리팹 저장
            string prefabPath = $"{outputFolder}/{prefabName}.prefab";

            // 기존 프리팹 있으면 덮어쓰기
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject savedPrefab;

            if (existingPrefab != null)
            {
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            else
            {
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }

            // 임시 오브젝트 정리
            DestroyImmediate(root);

            // SO의 worldPrefab에 자동 할당
            if (savedPrefab != null)
            {
                SerializedObject serializedSO = new SerializedObject(so);
                SerializedProperty worldPrefabProp = serializedSO.FindProperty("worldPrefab");
                worldPrefabProp.objectReferenceValue = savedPrefab;
                serializedSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(so);

                Debug.Log($"[WeaponPrefabCreator] ✓ {so.displayName} 프리팹 생성 완료: {prefabPath}");
            }
        }

        /// <summary>모든 무기 프리팹 일괄 생성</summary>
        private void CreateAllPrefabs()
        {
            int created = 0;
            int skipped = 0;

            for (int i = 0; i < weaponSOs.Length; i++)
            {
                if (weaponSOs[i] == null || weaponModels[i] == null)
                {
                    skipped++;
                    continue;
                }

                CreateWeaponPrefab(weaponSOs[i], weaponModels[i]);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("완료",
                $"프리팹 생성: {created}개\n건너뜀 (모델 미할당): {skipped}개",
                "확인");
        }

        /// <summary>무기 카테고리 이름 반환</summary>
        private string GetCategoryName(int category)
        {
            return category switch
            {
                0 => "AR",
                1 => "SMG",
                2 => "권총",
                3 => "SR",
                4 => "SG",
                _ => "기타"
            };
        }
    }
}
#endif
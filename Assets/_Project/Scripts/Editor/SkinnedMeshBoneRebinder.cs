using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DeadZone.EditorTools
{
    /// <summary>
    /// T1_Enemy.prefab의 CharacterVisual 하위에 있는 BattleRoyale 계열
    /// SkinnedMeshRenderer들의 끊어진 bones/rootBone 참조를 복구하는 Editor 전용 도구.
    /// </summary>
    /// <remarks>
    /// 원리: SkinnedMeshRenderer의 m_Bones는 "본의 이름"이 아니라 Transform 인스턴스를
    /// 직접 참조한다. 다른 prefab에서 SMR 컴포넌트만 복사하면 본 참조가 끊어진다.
    /// 두 prefab의 본 이름 체계가 동일하면 "이름 기반 매핑"으로 새 armature에 재바인딩 가능.
    /// </remarks>
    public static class SkinnedMeshBoneRebinder
    {
        // ====== 매핑 테이블 ======
        // 깨진 SMR의 GameObject 이름 → 원본 BattleRoyale prefab 에셋 경로
        private const string BattleRoyaleDir =
            "Assets/Imports/Asset/BattleRoyale/PolygonBattleRoyale/Prefabs/Characters/";

        private static readonly Dictionary<string, string> SourcePrefabByName = new Dictionary<string, string>
        {
            { "Character_BusinessMale_01",     BattleRoyaleDir + "Character_BusinessMale_01.prefab" },
            { "Character_70sFemale_01",        BattleRoyaleDir + "Character_70sFemale_01.prefab" },
            { "Character_GhillieSuit_01",      BattleRoyaleDir + "Character_GhillieSuit_01.prefab" },
            { "Character_MercenaryMale_01",    BattleRoyaleDir + "Character_MercenaryMale_01.prefab" },
            { "Character_MercenaryFemale_01",  BattleRoyaleDir + "Character_MercenaryFemale_01.prefab" },
            { "Character_MilitaryMale_01",     BattleRoyaleDir + "Character_MilitaryMale_01.prefab" },
            { "Character_MilitaryFemale_01",   BattleRoyaleDir + "Character_MilitaryFemale_01.prefab" },
            { "Character_SportyMale_01",       BattleRoyaleDir + "Character_SportyMale_01.prefab" },
            { "Character_SportyMale_02",       BattleRoyaleDir + "Character_SportyMale_02.prefab" },
            { "Character_SportyFemale_01",     BattleRoyaleDir + "Character_SportyFemale_01.prefab" },
            { "Character_SportyFemale_02",     BattleRoyaleDir + "Character_SportyFemale_02.prefab" },
            { "Character_RedneckMale_01",      BattleRoyaleDir + "Character_RedneckMale_01.prefab" },
            { "Character_GothFemale_01",       BattleRoyaleDir + "Character_GothFemale_01.prefab" },
            { "Character_ToplessMale_01",      BattleRoyaleDir + "Character_ToplessMale_01.prefab" },
            { "Character_SportsBraFemale_01",  BattleRoyaleDir + "Character_SportsBraFemale_01.prefab" },
        };

        private const string LogPrefix = "[BoneRebinder]";

        // ====================================================================
        // 메뉴 진입점
        // ====================================================================

        [MenuItem("Tools/DeadZone/Bone Rebinder/(1) Inspect Selected SMR")]
        private static void Menu_InspectSelected()
        {
            RunOnSelectedSingle(dryRun: true);
        }

        [MenuItem("Tools/DeadZone/Bone Rebinder/(2) Rebind Selected SMR")]
        private static void Menu_RebindSelected()
        {
            RunOnSelectedSingle(dryRun: false);
        }

        [MenuItem("Tools/DeadZone/Bone Rebinder/(3) Rebind All Broken In Selection")]
        private static void Menu_RebindAllBroken()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning($"{LogPrefix} 선택된 GameObject가 없습니다. CharacterVisual 등 상위 노드를 선택하세요.");
                return;
            }

            SkinnedMeshRenderer[] allSmrs = selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (allSmrs.Length == 0)
            {
                Debug.LogWarning($"{LogPrefix} '{selected.name}' 하위에 SkinnedMeshRenderer가 없습니다.");
                return;
            }

            // 깨진 SMR / 매핑 가능 SMR 분류
            List<SkinnedMeshRenderer> broken = new List<SkinnedMeshRenderer>();
            int mappedCount = 0;
            int skipCount = 0;
            foreach (var smr in allSmrs)
            {
                if (!IsBroken(smr)) continue;
                broken.Add(smr);
                if (SourcePrefabByName.ContainsKey(smr.gameObject.name)) mappedCount++;
                else skipCount++;
            }

            // 사전 통계 출력 (잘못된 노드 선택 조기 발견용)
            Debug.Log(
                $"{LogPrefix} 선택='{selected.name}' | 전체 SMR={allSmrs.Length}, 깨진 SMR={broken.Count}, " +
                $"매핑 가능={mappedCount}, 매핑 없음(skip)={skipCount}");

            if (broken.Count == 0)
            {
                Debug.Log($"{LogPrefix} 처리할 깨진 SMR이 없습니다.");
                return;
            }

            Transform armatureRoot = FindArmatureRoot(broken[0]);
            if (armatureRoot == null) return;

            Debug.Log($"{LogPrefix} 일괄 처리 시작. armatureRoot='{armatureRoot.name}'");

            // Undo 그룹 시작 — 전체 변경을 Ctrl+Z 한 번으로 되돌리기 위함
            int undoGroup = Undo.GetCurrentGroup();

            int success = 0;
            int failed = 0;
            int skipped = 0;

            foreach (var smr in broken)
            {
                string name = smr.gameObject.name;
                if (!SourcePrefabByName.TryGetValue(name, out string sourcePath))
                {
                    Debug.LogWarning($"{LogPrefix} '{name}' — SourcePrefabByName에 매핑이 없어 건너뜁니다.");
                    skipped++;
                    continue;
                }

                RebindReport report = RebindBones(smr, armatureRoot, sourcePath, dryRun: false);
                PrintReport(report);

                if (report.Applied) success++;
                else failed++;
            }

            // Undo 그룹 마무리 — 묶은 단일 작업으로 만들기
            Undo.SetCurrentGroupName("Rebind All Broken SMR Bones");
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"{LogPrefix} 일괄 처리 완료. 성공 {success}, 실패 {failed}, 매핑없음 {skipped}, 총 {broken.Count}.");
        }

        // ====================================================================
        // 단일 SMR 처리 공통
        // ====================================================================

        private static void RunOnSelectedSingle(bool dryRun)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning($"{LogPrefix} 선택된 GameObject가 없습니다.");
                return;
            }

            SkinnedMeshRenderer smr = selected.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogWarning($"{LogPrefix} 선택된 '{selected.name}'에 SkinnedMeshRenderer가 없습니다.");
                return;
            }

            string name = smr.gameObject.name;
            if (!SourcePrefabByName.TryGetValue(name, out string sourcePath))
            {
                Debug.LogWarning($"{LogPrefix} '{name}' — SourcePrefabByName에 매핑이 없습니다. 매핑 테이블에 추가하세요.");
                return;
            }

            Transform armatureRoot = FindArmatureRoot(smr);
            if (armatureRoot == null) return;

            RebindReport report = RebindBones(smr, armatureRoot, sourcePath, dryRun);
            PrintReport(report);
        }

        // ====================================================================
        // 핵심 로직
        // ====================================================================

        /// <summary>
        /// 깨진 SMR 한 개의 bones/rootBone을 armatureRoot 하위 본 이름 기준으로 재바인딩.
        /// dryRun==true이거나 누락 본이 1개라도 있으면 SMR을 변경하지 않고 리포트만 반환.
        /// </summary>
        private static RebindReport RebindBones(
            SkinnedMeshRenderer targetSmr,
            Transform armatureRoot,
            string sourcePrefabPath,
            bool dryRun)
        {
            RebindReport report = new RebindReport
            {
                targetName = targetSmr != null ? targetSmr.gameObject.name : "<null>",
                Applied = false
            };

            // ----- 입력 검증 -----
            if (targetSmr == null)
            {
                Debug.LogError($"{LogPrefix} targetSmr가 null입니다.");
                return report;
            }
            if (armatureRoot == null)
            {
                Debug.LogError($"{LogPrefix} '{report.targetName}' — armatureRoot가 null입니다.");
                return report;
            }
            if (targetSmr.sharedMesh == null)
            {
                Debug.LogError($"{LogPrefix} '{report.targetName}' — targetSmr.sharedMesh가 null입니다.");
                return report;
            }
            if (string.IsNullOrEmpty(sourcePrefabPath))
            {
                Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 prefab 경로가 비어있습니다.");
                return report;
            }

            // 원본 prefab 에셋 존재 여부 사전 검증
            // (잘못된 경로로 LoadPrefabContents 호출 시 예외 가능성 회피)
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            if (sourceAsset == null)
            {
                Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 prefab 에셋을 찾을 수 없습니다: {sourcePrefabPath}");
                return report;
            }

            // ----- armature 사전 구성 -----
            Dictionary<string, Transform> boneDict = BuildBoneDictionary(armatureRoot);

            // ----- 원본 prefab 임시 로드 (반드시 try/finally) -----
            GameObject sourceRoot = null;
            try
            {
                sourceRoot = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
                if (sourceRoot == null)
                {
                    Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 prefab 로드 실패: {sourcePrefabPath}");
                    return report;
                }

                // 같은 sharedMesh를 가진 SMR을 원본에서 찾기
                SkinnedMeshRenderer sourceSmr = null;
                var allSourceSmrs = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var s in allSourceSmrs)
                {
                    if (s.sharedMesh == targetSmr.sharedMesh)
                    {
                        sourceSmr = s;
                        break;
                    }
                }

                if (sourceSmr == null)
                {
                    Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 prefab '{sourcePrefabPath}'에서 같은 sharedMesh를 가진 SMR을 찾지 못했습니다.");
                    return report;
                }
                if (sourceSmr.rootBone == null)
                {
                    Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 SMR의 rootBone이 null입니다.");
                    return report;
                }

                Transform[] sourceBones = sourceSmr.bones;
                if (sourceBones == null || sourceBones.Length == 0)
                {
                    Debug.LogError($"{LogPrefix} '{report.targetName}' — 원본 SMR의 bones가 비어있습니다. (Length=0 또는 null)");
                    return report;
                }

                report.boneCount = sourceBones.Length;

                // ----- 본 매핑 -----
                Transform[] newBones = new Transform[sourceBones.Length];
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    Transform sb = sourceBones[i];
                    if (sb == null)
                    {
                        report.missingBoneNames.Add($"<null at index {i}>");
                        continue;
                    }

                    if (boneDict.TryGetValue(sb.name, out Transform t))
                    {
                        newBones[i] = t;
                        report.mappedCount++;
                    }
                    else
                    {
                        report.missingBoneNames.Add(sb.name);
                    }
                }

                // RootBone 매핑
                string sourceRootBoneName = sourceSmr.rootBone.name;
                Transform newRootBone = null;
                if (boneDict.TryGetValue(sourceRootBoneName, out Transform rootT))
                {
                    newRootBone = rootT;
                    report.rootBoneName = rootT.name;
                }
                else
                {
                    report.missingBoneNames.Add(sourceRootBoneName + " (RootBone)");
                }

                // ----- 적용 단계 -----
                if (dryRun)
                {
                    report.Applied = false;
                    return report;
                }
                if (report.missingBoneNames.Count > 0)
                {
                    report.Applied = false;
                    return report;
                }

                Undo.RecordObject(targetSmr, "Rebind SMR Bones");
                targetSmr.bones = newBones;
                targetSmr.rootBone = newRootBone;
                EditorUtility.SetDirty(targetSmr);
                report.Applied = true;
                return report;
            }
            finally
            {
                if (sourceRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(sourceRoot);
                }
            }
        }

        // ====================================================================
        // 헬퍼
        // ====================================================================

        /// <summary>
        /// armatureRoot 하위의 모든 Transform을 이름→Transform Dictionary로 구축.
        /// 이름 중복 시 경고 로그 후 첫 번째 Transform 유지.
        /// </summary>
        private static Dictionary<string, Transform> BuildBoneDictionary(Transform armatureRoot)
        {
            Dictionary<string, Transform> dict = new Dictionary<string, Transform>();
            Transform[] all = armatureRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (dict.ContainsKey(t.name))
                {
                    Debug.LogWarning($"{LogPrefix} armature에 이름 중복: '{t.name}'. 첫 번째 Transform 유지.");
                    continue;
                }
                dict.Add(t.name, t);
            }
            return dict;
        }

        /// <summary>
        /// SMR이 속한 트리에서 armatureRoot로 쓸 "Root" Transform 탐색.
        /// 1순위: top.Find("Root") (T1_Enemy 직계 자식)
        /// 2순위: 트리 전체 검색 fallback (Warning 로그)
        /// </summary>
        private static Transform FindArmatureRoot(SkinnedMeshRenderer smr)
        {
            if (smr == null) return null;

            Transform top = smr.transform;
            while (top.parent != null) top = top.parent;

            // 1순위: 최상위 직계 자식
            Transform direct = top.Find("Root");
            if (direct != null) return direct;

            // 2순위: 트리 전체 검색
            Transform[] all = top.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t.name == "Root")
                {
                    Debug.LogWarning($"{LogPrefix} '{smr.gameObject.name}' — top 직계 자식에 'Root'가 없어 트리 전체 검색으로 발견: '{GetHierarchyPath(t)}'");
                    return t;
                }
            }

            Debug.LogError($"{LogPrefix} armatureRoot('Root')를 트리에서 찾지 못했습니다. SMR='{smr.gameObject.name}'");
            return null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }

        /// <summary>
        /// SMR의 bones에 null이 하나라도 있거나 rootBone이 null이면 깨진 것으로 판정.
        /// </summary>
        private static bool IsBroken(SkinnedMeshRenderer smr)
        {
            if (smr == null) return false;
            if (smr.rootBone == null) return true;

            Transform[] bones = smr.bones;
            if (bones == null) return true;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) return true;
            }
            return false;
        }

        /// <summary>
        /// 콘솔에 리포트 출력. 누락이 있으면 Warning, 적용 완료/dry-run 깨끗하면 Log.
        /// </summary>
        private static void PrintReport(RebindReport report)
        {
            if (report == null)
            {
                Debug.LogError($"{LogPrefix} report가 null입니다.");
                return;
            }

            string rootInfo = string.IsNullOrEmpty(report.rootBoneName) ? "<null>" : report.rootBoneName;

            if (report.missingBoneNames.Count == 0)
            {
                Debug.Log(
                    $"{LogPrefix} {report.targetName} — {report.mappedCount}/{report.boneCount} 매핑, " +
                    $"RootBone={rootInfo}, Applied={report.Applied}");
            }
            else
            {
                string missing = string.Join(", ", report.missingBoneNames);
                Debug.LogWarning(
                    $"{LogPrefix} {report.targetName} — {report.mappedCount}/{report.boneCount} 매핑, " +
                    $"누락: [{missing}], Applied={report.Applied}");
            }
        }

        // ====================================================================
        // 내부 전용 결과 리포트
        // ====================================================================

        private class RebindReport
        {
            public string targetName;
            public int boneCount;
            public int mappedCount;
            public List<string> missingBoneNames = new List<string>();
            public string rootBoneName;
            public bool Applied;
        }
    }
}
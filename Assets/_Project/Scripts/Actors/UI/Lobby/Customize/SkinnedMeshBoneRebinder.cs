using System.Collections.Generic;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadZone.Actors.Player
{
    [ExecuteAlways]
    public sealed class SkinnedMeshBoneRebinder : MonoBehaviour
    {
        [Header("기존 잘못 물린 본")]
        [SerializeField] private Transform sourceSkeletonRoot;

        [Header("새로 연결할 기존 플레이어 본")]
        [SerializeField] private Transform targetSkeletonRoot;

        [Header("SkinnedMeshRenderer 탐색 루트")]
        [SerializeField] private Transform renderersRoot;

        [Header("옵션")]
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private bool logDetails = true;

        [ContextMenu("Rebind To Target Skeleton")]
        public void RebindToTargetSkeleton()
        {
            if (sourceSkeletonRoot == null)
            {
                Debug.LogError("[SkinnedMeshBoneRebinder] Source Skeleton Root가 비어 있습니다.", this);
                return;
            }

            if (targetSkeletonRoot == null)
            {
                Debug.LogError("[SkinnedMeshBoneRebinder] Target Skeleton Root가 비어 있습니다.", this);
                return;
            }

            if (renderersRoot == null)
            {
                Debug.LogError("[SkinnedMeshBoneRebinder] Renderers Root가 비어 있습니다.", this);
                return;
            }

            Dictionary<string, Transform> targetBoneMap = BuildBoneMap(targetSkeletonRoot);
            SkinnedMeshRenderer[] renderers = renderersRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);

            int changedRendererCount = 0;
            int changedBoneCount = 0;

            foreach (SkinnedMeshRenderer smr in renderers)
            {
                bool changed = false;

                if (smr.rootBone != null && IsChildOfOrSame(smr.rootBone, sourceSkeletonRoot))
                {
                    if (targetBoneMap.TryGetValue(smr.rootBone.name, out Transform newRootBone))
                    {
                        if (logDetails)
                        {
                            Debug.Log($"[SkinnedMeshBoneRebinder] RootBone 변경: {smr.name} / {smr.rootBone.name} -> {newRootBone.name}", smr);
                        }

                        smr.rootBone = newRootBone;
                        changed = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SkinnedMeshBoneRebinder] Target에서 RootBone 이름을 찾지 못함: {smr.rootBone.name}", smr);
                    }
                }

                Transform[] bones = smr.bones;

                for (int i = 0; i < bones.Length; i++)
                {
                    Transform oldBone = bones[i];

                    if (oldBone == null)
                        continue;

                    if (!IsChildOfOrSame(oldBone, sourceSkeletonRoot))
                        continue;

                    if (targetBoneMap.TryGetValue(oldBone.name, out Transform newBone))
                    {
                        bones[i] = newBone;
                        changed = true;
                        changedBoneCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[SkinnedMeshBoneRebinder] Target에서 Bone 이름을 찾지 못함: {oldBone.name}", smr);
                    }
                }

                if (changed)
                {
                    smr.bones = bones;
                    changedRendererCount++;

#if UNITY_EDITOR
                    EditorUtility.SetDirty(smr);
#endif
                }
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(gameObject);
#endif

            Debug.Log(
                $"[SkinnedMeshBoneRebinder] 리바인딩 완료. " +
                $"변경 Renderer={changedRendererCount}, 변경 Bone={changedBoneCount}",
                this);
        }

        private static Dictionary<string, Transform> BuildBoneMap(Transform root)
        {
            Dictionary<string, Transform> map = new Dictionary<string, Transform>();
            AddBoneRecursive(root, map);
            return map;
        }

        private static void AddBoneRecursive(Transform bone, Dictionary<string, Transform> map)
        {
            if (bone == null)
                return;

            if (!map.ContainsKey(bone.name))
                map.Add(bone.name, bone);

            for (int i = 0; i < bone.childCount; i++)
                AddBoneRecursive(bone.GetChild(i), map);
        }

        private static bool IsChildOfOrSame(Transform target, Transform root)
        {
            if (target == null || root == null)
                return false;

            Transform current = target;

            while (current != null)
            {
                if (current == root)
                    return true;

                current = current.parent;
            }

            return false;
        }
    }
}
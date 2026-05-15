using UnityEngine;

namespace DeadZone.Actors.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterCustomizeView : MonoBehaviour
    {
        [Header("자동 탐색 기준")]
        [SerializeField] private Transform modelRoot;
        [SerializeField] private string bodyOptionsRootName = "Body";
        [SerializeField] private string headOptionsRootName = "Hair";
        [SerializeField] private string beardOptionsRootName = "Beard";
        [SerializeField] private string hatOptionsRootName = "Hat";

        [Header("빈 옵션 추가")]
        [SerializeField] private bool addEmptyHeadOption = true;
        [SerializeField] private bool addEmptyBeardOption = true;
        [SerializeField] private bool addEmptyHatOption = true;

        [Header("수동 지정 옵션")]
        [SerializeField] private GameObject[] bodyObjects;
        [SerializeField] private GameObject[] headObjects;
        [SerializeField] private GameObject[] beardObjects;
        [SerializeField] private GameObject[] hatObjects;

        [Header("옵션")]
        [SerializeField] private bool autoResolveOnAwake = true;
        [SerializeField] private bool showDebugLogs = false;
        
        [Header("애니메이션")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private bool rebindAnimatorAfterApply = true;

        private bool resolved;

        private void Awake()
        {
            if (modelRoot == null)
                modelRoot = transform;

            if (targetAnimator == null)
                targetAnimator = GetComponentInParent<Animator>();

            if (autoResolveOnAwake)
                ResolveOptions();
        }

        public void Apply(CharacterCustomizeNetworkData data)
        {
            if (!resolved)
                ResolveOptions();

            ApplyOption(bodyObjects, data.BodyIndex);
            ApplyOption(headObjects, data.HeadIndex);
            ApplyOption(beardObjects, data.BeardIndex);
            ApplyOption(hatObjects, data.HatIndex);

            if (showDebugLogs)
            {
                Debug.Log(
                    $"[PlayerCharacterCustomizeView] Applied. " +
                    $"Body={data.BodyIndex}, Head={data.HeadIndex}, Beard={data.BeardIndex}, Hat={data.HatIndex}",
                    this);
            }
            
            if (rebindAnimatorAfterApply && targetAnimator != null)
            {
                targetAnimator.Rebind();
                targetAnimator.Update(0f);
            }
        }

        [ContextMenu("Resolve Options")]
        public void ResolveOptions()
        {
            if (modelRoot == null)
                modelRoot = transform;

            if (bodyObjects == null || bodyObjects.Length == 0)
                bodyObjects = ResolveBodyOptions(modelRoot);

            if (headObjects == null || headObjects.Length == 0)
                headObjects = ResolveChildOptions(modelRoot, headOptionsRootName, addEmptyHeadOption);

            if (beardObjects == null || beardObjects.Length == 0)
                beardObjects = ResolveChildOptions(modelRoot, beardOptionsRootName, addEmptyBeardOption);

            if (hatObjects == null || hatObjects.Length == 0)
                hatObjects = ResolveChildOptions(modelRoot, hatOptionsRootName, addEmptyHatOption);

            resolved = true;

            if (showDebugLogs)
            {
                Debug.Log(
                    $"[PlayerCharacterCustomizeView] Resolve complete. " +
                    $"Body={GetCount(bodyObjects)}, Head={GetCount(headObjects)}, Beard={GetCount(beardObjects)}, Hat={GetCount(hatObjects)}",
                    this);
            }
        }

        private GameObject[] ResolveBodyOptions(Transform root)
        {
            GameObject[] options = ResolveChildOptions(root, bodyOptionsRootName, false);

            if (options.Length > 0)
                return options;

            return ResolveDirectChildOptionsByPrefix(root, "Character_");
        }

        private GameObject[] ResolveChildOptions(
            Transform root,
            string optionsRootName,
            bool addEmptyOption)
        {
            if (root == null || string.IsNullOrWhiteSpace(optionsRootName))
                return addEmptyOption ? new GameObject[] { null } : new GameObject[0];

            Transform optionsRoot = FindDeepChild(root, optionsRootName);

            if (optionsRoot == null)
                return addEmptyOption ? new GameObject[] { null } : new GameObject[0];

            int optionCount = optionsRoot.childCount + (addEmptyOption ? 1 : 0);
            GameObject[] options = new GameObject[optionCount];

            int targetIndex = 0;

            if (addEmptyOption)
                options[targetIndex++] = null;

            for (int i = 0; i < optionsRoot.childCount; i++)
                options[targetIndex++] = optionsRoot.GetChild(i).gameObject;

            return options;
        }

        private GameObject[] ResolveDirectChildOptionsByPrefix(Transform root, string prefix)
        {
            if (root == null)
                return new GameObject[0];

            int count = 0;

            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name.StartsWith(prefix))
                    count++;
            }

            GameObject[] options = new GameObject[count];
            int targetIndex = 0;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);

                if (child.name.StartsWith(prefix))
                    options[targetIndex++] = child.gameObject;
            }

            return options;
        }

        private Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null)
                return null;

            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeepChild(root.GetChild(i), childName);

                if (found != null)
                    return found;
            }

            return null;
        }

        private static void ApplyOption(GameObject[] objects, int selectedIndex)
        {
            if (objects == null || objects.Length == 0)
                return;

            int safeIndex = Mathf.Clamp(selectedIndex, 0, objects.Length - 1);

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                    continue;

                objects[i].SetActive(i == safeIndex);
            }
        }

        private static int GetCount(GameObject[] objects)
        {
            return objects == null ? 0 : objects.Length;
        }
    }
}
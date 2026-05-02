using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class CharacterCustomizePreview : MonoBehaviour
    {
        [Header("Character Prefab Mode")]
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private RuntimeAnimatorController previewAnimatorController;
        [SerializeField] private string bodyOptionsRootName = "Body";
        [SerializeField] private string headOptionsRootName = "Hair";
        [SerializeField] private string beardOptionsRootName = "Beard";
        [SerializeField] private string hatOptionsRootName = "Hat";
        [SerializeField] private bool addEmptyHeadOption = true;
        [SerializeField] private bool addEmptyBeardOption = true;
        [SerializeField] private bool addEmptyHatOption = true;

        [Header("Body Option Objects")]
        [SerializeField] private GameObject[] bodyObjects;

        [Header("Head Option Objects")]
        [SerializeField] private GameObject[] headObjects;

        [Header("Beard Option Objects")]
        [SerializeField] private GameObject[] beardObjects;

        [Header("Hat Option Objects")]
        [SerializeField] private GameObject[] hatObjects;

        [Header("Render Preview")]
        [SerializeField] private RawImage previewImage;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Transform previewModelRoot;
        [SerializeField] private Vector3 previewWorldPosition = new Vector3(10000f, 0f, 0f);
        [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 1.4f, -4f);
        [SerializeField] private Vector3 cameraLocalEulerAngles = new Vector3(12f, 0f, 0f);
        [SerializeField] private Vector3 characterLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 characterLocalEulerAngles = Vector3.zero;
        [SerializeField] private Vector3 characterLocalScale = Vector3.one;
        [SerializeField] private int renderTextureSize = 512;

        private GameObject[] runtimeBodyObjects;
        private GameObject[] runtimeHeadObjects;
        private GameObject[] runtimeBeardObjects;
        private GameObject[] runtimeHatObjects;
        private GameObject runtimeCharacterInstance;
        private RenderTexture runtimeRenderTexture;
        private Light runtimeLight;

        private void Awake()
        {
            EnsureRenderPreview();
            EnsureRuntimeObjects();
            ApplyPreviewTransformSettings();
        }

        private void OnValidate()
        {
            renderTextureSize = Mathf.Max(128, renderTextureSize);
            ApplyPreviewTransformSettings();
        }

        private void LateUpdate()
        {
            ApplyPreviewTransformSettings();
        }

        public int BodyOptionCount
        {
            get
            {
                EnsureRuntimeObjects();
                return GetOptionCount(runtimeBodyObjects);
            }
        }

        public int HeadOptionCount
        {
            get
            {
                EnsureRuntimeObjects();
                return GetOptionCount(runtimeHeadObjects);
            }
        }

        public int BeardOptionCount
        {
            get
            {
                EnsureRuntimeObjects();
                return GetOptionCount(runtimeBeardObjects);
            }
        }

        public int HatOptionCount
        {
            get
            {
                EnsureRuntimeObjects();
                return GetOptionCount(runtimeHatObjects);
            }
        }

        public void Apply(CharacterCustomizeData data)
        {
            EnsureRuntimeObjects();
            ApplyPreviewTransformSettings();

            ApplyOption(runtimeBodyObjects, data.bodyIndex);
            ApplyOption(runtimeHeadObjects, data.headIndex);
            ApplyOption(runtimeBeardObjects, data.beardIndex);
            ApplyOption(runtimeHatObjects, data.hatIndex);
        }

        private void EnsureRuntimeObjects()
        {
            EnsureRenderPreview();

            if (characterPrefab != null)
            {
                EnsureCharacterPrefabInstance();
                return;
            }

            runtimeBodyObjects = EnsureRuntimeObjects(bodyObjects, runtimeBodyObjects);
            runtimeHeadObjects = EnsureRuntimeObjects(headObjects, runtimeHeadObjects);
            runtimeBeardObjects = EnsureRuntimeObjects(beardObjects, runtimeBeardObjects);
            runtimeHatObjects = EnsureRuntimeObjects(hatObjects, runtimeHatObjects);
        }

        private void EnsureCharacterPrefabInstance()
        {
            if (runtimeCharacterInstance != null)
                return;

            if (characterPrefab.scene.IsValid())
            {
                runtimeCharacterInstance = characterPrefab;
                runtimeCharacterInstance.transform.SetParent(previewModelRoot, false);
            }
            else
            {
                runtimeCharacterInstance = Instantiate(characterPrefab, previewModelRoot, false);
            }

            runtimeCharacterInstance.name = characterPrefab.name;
            runtimeCharacterInstance.SetActive(true);
            ApplyPreviewTransformSettings();

            Animator animator = runtimeCharacterInstance.GetComponentInChildren<Animator>(true);

            if (animator != null && previewAnimatorController != null)
                animator.runtimeAnimatorController = previewAnimatorController;

            runtimeBodyObjects = ResolveBodyOptions(runtimeCharacterInstance.transform);
            runtimeHeadObjects = ResolveChildOptions(runtimeCharacterInstance.transform, headOptionsRootName, addEmptyHeadOption);
            runtimeBeardObjects = ResolveChildOptions(runtimeCharacterInstance.transform, beardOptionsRootName, addEmptyBeardOption);
            runtimeHatObjects = ResolveChildOptions(runtimeCharacterInstance.transform, hatOptionsRootName, addEmptyHatOption);
        }

        private void EnsureRenderPreview()
        {
            if (previewImage == null)
                previewImage = GetComponentInChildren<RawImage>(true);

            if (previewImage == null)
                previewImage = CreatePreviewImage();

            if (previewModelRoot == null)
                previewModelRoot = CreatePreviewModelRoot();

            if (previewCamera == null)
                previewCamera = CreatePreviewCamera();

            if (runtimeLight == null)
                runtimeLight = CreatePreviewLight();

            EnsureRenderTexture();
        }

        private RawImage CreatePreviewImage()
        {
            GameObject imageObject = new GameObject("Runtime_PreviewRawImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.SetParent(transform, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            RawImage rawImage = imageObject.GetComponent<RawImage>();
            rawImage.raycastTarget = false;
            return rawImage;
        }

        private Transform CreatePreviewModelRoot()
        {
            GameObject rootObject = new GameObject("Runtime_CustomizePreviewRoot");
            Transform rootTransform = rootObject.transform;
            rootTransform.position = previewWorldPosition;
            rootTransform.rotation = Quaternion.identity;
            return rootTransform;
        }

        private Camera CreatePreviewCamera()
        {
            GameObject cameraObject = new GameObject("Runtime_CustomizePreviewCamera");
            Transform cameraTransform = cameraObject.transform;
            cameraTransform.SetParent(previewModelRoot, false);
            cameraTransform.localPosition = cameraLocalPosition;
            cameraTransform.localRotation = Quaternion.Euler(cameraLocalEulerAngles);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = false;
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.depth = -10f;
            return camera;
        }

        private Light CreatePreviewLight()
        {
            GameObject lightObject = new GameObject("Runtime_CustomizePreviewLight");
            Transform lightTransform = lightObject.transform;
            lightTransform.SetParent(previewModelRoot, false);
            lightTransform.localPosition = new Vector3(0f, 3f, -2f);
            lightTransform.localRotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            return light;
        }

        private void EnsureRenderTexture()
        {
            if (runtimeRenderTexture != null)
                return;

            int safeSize = Mathf.Max(128, renderTextureSize);
            runtimeRenderTexture = new RenderTexture(safeSize, safeSize, 16, RenderTextureFormat.ARGB32);
            runtimeRenderTexture.name = "Runtime_CustomizePreviewTexture";

            if (previewCamera != null)
                previewCamera.targetTexture = runtimeRenderTexture;

            if (previewImage != null)
                previewImage.texture = runtimeRenderTexture;
        }

        private GameObject[] EnsureRuntimeObjects(GameObject[] sourceObjects, GameObject[] runtimeObjects)
        {
            if (sourceObjects == null || sourceObjects.Length == 0)
                return sourceObjects;

            if (runtimeObjects != null && runtimeObjects.Length == sourceObjects.Length)
                return runtimeObjects;

            runtimeObjects = new GameObject[sourceObjects.Length];

            for (int i = 0; i < sourceObjects.Length; i++)
            {
                GameObject sourceObject = sourceObjects[i];

                if (sourceObject == null)
                    continue;

                if (sourceObject.scene.IsValid())
                {
                    runtimeObjects[i] = sourceObject;
                    continue;
                }

                GameObject instance = Instantiate(sourceObject, previewModelRoot, false);
                instance.name = sourceObject.name;
                ApplyObjectTransformSettings(instance.transform);
                instance.SetActive(false);
                runtimeObjects[i] = instance;
            }

            return runtimeObjects;
        }

        private GameObject[] ResolveBodyOptions(Transform root)
        {
            GameObject[] options = ResolveChildOptions(root, bodyOptionsRootName, false);

            if (options.Length > 0)
                return options;

            return ResolveDirectChildOptionsByPrefix(root, "Character_");
        }

        private GameObject[] ResolveChildOptions(Transform root, string optionsRootName, bool addEmptyOption)
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

        private void ApplyPreviewTransformSettings()
        {
            if (previewModelRoot != null)
            {
                previewModelRoot.position = previewWorldPosition;
                previewModelRoot.rotation = Quaternion.identity;
            }

            if (previewCamera != null)
            {
                Transform cameraTransform = previewCamera.transform;
                cameraTransform.localPosition = cameraLocalPosition;
                cameraTransform.localRotation = Quaternion.Euler(cameraLocalEulerAngles);
            }

            if (runtimeCharacterInstance != null)
                ApplyObjectTransformSettings(runtimeCharacterInstance.transform);

            ApplyObjectTransformSettings(runtimeBodyObjects);
            ApplyObjectTransformSettings(runtimeHeadObjects);
            ApplyObjectTransformSettings(runtimeBeardObjects);
            ApplyObjectTransformSettings(runtimeHatObjects);
        }

        private void ApplyObjectTransformSettings(GameObject[] objects)
        {
            if (objects == null)
                return;

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                    continue;

                ApplyObjectTransformSettings(objects[i].transform);
            }
        }

        private void ApplyObjectTransformSettings(Transform target)
        {
            if (target == null || target.parent != previewModelRoot)
                return;

            target.localPosition = characterLocalPosition;
            target.localRotation = Quaternion.Euler(characterLocalEulerAngles);
            target.localScale = characterLocalScale;
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

        private static int GetOptionCount(GameObject[] objects)
        {
            return objects != null && objects.Length > 0 ? objects.Length : 1;
        }
    }
}

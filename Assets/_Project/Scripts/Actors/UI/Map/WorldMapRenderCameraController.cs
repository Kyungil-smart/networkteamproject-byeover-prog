using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class WorldMapRenderCameraController : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private RenderTexture targetTexture;
        [SerializeField] private LayerMask hiddenFromMapLayers = 1 << 9;

        [Header("Render Texture")]
        [SerializeField] private bool createRuntimeTextureInstance = true;
        [SerializeField] private RawImage targetRawImage;
        [SerializeField] private string targetRawImageName = "Png_WorldMap_01";

        [Header("Runtime Lock")]
        [SerializeField] private bool useCurrentScenePoseOnAwake;
        [SerializeField] private bool detachFromRectTransformParent = true;
        [SerializeField] private bool lockTransformEveryFrame = true;
        [SerializeField] private Vector3 fixedPosition = new(-13.9f, 540f, 123.9f);
        [SerializeField] private Vector3 fixedEulerAngles = new(90f, 0f, -90f);
        [SerializeField, Min(0.01f)] private float fixedOrthographicSize = 138.9f;

        private Vector3 runtimePosition;
        private Quaternion runtimeRotation;
        private float runtimeOrthographicSize;
        private RenderTexture runtimeTargetTexture;
        private RenderTexture runtimeTextureInstance;

        private void Awake()
        {
            ResolveCamera();
            ResolveRawImage();
            CaptureRuntimeSettings();
            ApplySettings(detachIfNeeded: true);
            DisableAudioListeners();
        }

        private void OnDestroy()
        {
            if (runtimeTextureInstance == null)
                return;

            runtimeTextureInstance.Release();
            Destroy(runtimeTextureInstance);
            runtimeTextureInstance = null;
        }

        private void LateUpdate()
        {
            if (!lockTransformEveryFrame)
                return;

            ApplySettings(detachIfNeeded: false);
        }

        private void OnValidate()
        {
            ResolveCamera();
            DisableAudioListeners();
        }

        private void ResolveCamera()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();

            if (targetTexture == null && targetCamera != null)
                targetTexture = targetCamera.targetTexture;
        }

        private void ResolveRawImage()
        {
            if (targetRawImage != null || string.IsNullOrWhiteSpace(targetRawImageName))
                return;

            RawImage[] rawImages = FindObjectsByType<RawImage>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < rawImages.Length; i++)
            {
                RawImage rawImage = rawImages[i];
                if (rawImage != null && rawImage.name == targetRawImageName)
                {
                    targetRawImage = rawImage;
                    return;
                }
            }
        }

        private void ApplySettings(bool detachIfNeeded)
        {
            if (targetCamera == null)
                return;

            if (detachIfNeeded && detachFromRectTransformParent && transform.parent is RectTransform)
                transform.SetParent(null, true);

            transform.SetPositionAndRotation(runtimePosition, runtimeRotation);

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = runtimeOrthographicSize;
            targetCamera.cullingMask &= ~hiddenFromMapLayers.value;

            if (runtimeTargetTexture != null)
                targetCamera.targetTexture = runtimeTargetTexture;
        }

        private void CaptureRuntimeSettings()
        {
            if (useCurrentScenePoseOnAwake)
            {
                runtimePosition = transform.position;
                runtimeRotation = transform.rotation;
                runtimeOrthographicSize = targetCamera != null ? targetCamera.orthographicSize : fixedOrthographicSize;
            }
            else
            {
                runtimePosition = fixedPosition;
                runtimeRotation = Quaternion.Euler(fixedEulerAngles);
                runtimeOrthographicSize = fixedOrthographicSize;
            }

            RenderTexture sourceTexture = targetTexture != null ? targetTexture : targetCamera != null ? targetCamera.targetTexture : null;
            runtimeTargetTexture = createRuntimeTextureInstance ? CreateRuntimeTexture(sourceTexture) : sourceTexture;

            if (targetRawImage != null && runtimeTargetTexture != null)
                targetRawImage.texture = runtimeTargetTexture;
        }

        private RenderTexture CreateRuntimeTexture(RenderTexture sourceTexture)
        {
            if (sourceTexture == null)
                return null;

            if (runtimeTextureInstance != null)
                return runtimeTextureInstance;

            runtimeTextureInstance = new RenderTexture(sourceTexture)
            {
                name = $"{sourceTexture.name}_Runtime_{GetInstanceID()}"
            };
            runtimeTextureInstance.Create();
            return runtimeTextureInstance;
        }

        private void DisableAudioListeners()
        {
            AudioListener[] audioListeners = GetComponents<AudioListener>();
            for (int i = 0; i < audioListeners.Length; i++)
                audioListeners[i].enabled = false;
        }
    }
}

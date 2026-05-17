using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace DeadZone.Actors.UI
{
    public class MinimapCameraFollower : MonoBehaviour
    {
        [Header("추적 대상")]
        [SerializeField] private Transform target;

        [Header("카메라 설정")]
        [SerializeField] private Camera minimapCamera;
        [SerializeField] private AudioListener minimapAudioListener;
        [SerializeField] private float height = 60f;
        [SerializeField] private float followSpeed = 20f;

        [Header("렌더 텍스처")]
        [SerializeField] private RawImage minimapRawImage;
        [SerializeField] private string minimapRawImageName = "RawImage_Minimap";
        [SerializeField, Min(64)] private int renderTextureSize = 256;
        [SerializeField] private bool overrideRawImageTexture = true;

        [Header("레이어")]
        [SerializeField] private string markerLayerName = "Minimap";
        [SerializeField] private LayerMask hiddenFromMapLayers = 1 << 9;
        [SerializeField] private bool hideMarkerLayerFromOtherCameras = true;

        private static readonly Quaternion TopDownRotation = Quaternion.Euler(90f, 0f, 0f);

        private RenderTexture runtimeRenderTexture;

        private Transform CameraTransform => minimapCamera != null ? minimapCamera.transform : transform;

        private void Reset()
        {
            EnsureMinimapCamera();
        }

        private void OnValidate()
        {
            EnsureMinimapCamera();
        }

        private void Awake()
        {
            EnsureMinimapCamera();
            DisableMinimapAudioListener();
            EnsureRenderTexture();
            ConfigureCameraMasks();
            CameraTransform.rotation = TopDownRotation;
        }

        private void OnDestroy()
        {
            if (runtimeRenderTexture != null)
            {
                runtimeRenderTexture.Release();
                Destroy(runtimeRenderTexture);
                runtimeRenderTexture = null;
            }
        }

        private void LateUpdate()
        {
            DisableMinimapAudioListener();
            EnsureRenderTexture();
            ConfigureCameraMasks();

            if (target == null)
            {
                TryFindLocalPlayer();

                if (target == null)
                    return;
            }

            Vector3 targetPosition = target.position;
            Vector3 cameraPosition = new Vector3(targetPosition.x, targetPosition.y + height, targetPosition.z);
            Transform cameraTransform = CameraTransform;

            cameraTransform.position = Vector3.Lerp(
                cameraTransform.position,
                cameraPosition,
                Time.deltaTime * followSpeed);

            cameraTransform.rotation = TopDownRotation;
        }

        private void EnsureMinimapCamera()
        {
            if (minimapCamera != null)
                return;

            minimapCamera = GetComponent<Camera>();

            if (minimapCamera == null)
                minimapCamera = GetComponentInChildren<Camera>(true);
        }

        private void DisableMinimapAudioListener()
        {
            if (minimapAudioListener == null)
            {
                minimapAudioListener = GetComponent<AudioListener>();

                if (minimapAudioListener == null)
                    minimapAudioListener = GetComponentInChildren<AudioListener>(true);
            }

            if (minimapAudioListener != null)
                minimapAudioListener.enabled = false;
        }

        private void EnsureRenderTexture()
        {
            if (minimapCamera == null)
                return;

            minimapCamera.orthographic = true;

            if (minimapCamera.targetTexture == null)
            {
                runtimeRenderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16)
                {
                    name = "Runtime_MinimapRenderTexture"
                };

                runtimeRenderTexture.Create();
                minimapCamera.targetTexture = runtimeRenderTexture;
            }

            if (minimapRawImage == null)
                minimapRawImage = FindMinimapRawImage();

            if (minimapRawImage != null && (overrideRawImageTexture || minimapRawImage.texture == null))
                minimapRawImage.texture = minimapCamera.targetTexture;
        }

        private RawImage FindMinimapRawImage()
        {
            RawImage[] rawImages = FindObjectsByType<RawImage>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < rawImages.Length; i++)
            {
                RawImage rawImage = rawImages[i];
                if (rawImage != null && rawImage.name == minimapRawImageName)
                    return rawImage;
            }

            return null;
        }

        private void ConfigureCameraMasks()
        {
            if (minimapCamera == null)
                return;

            int markerLayer = LayerMask.NameToLayer(markerLayerName);
            if (markerLayer < 0)
                return;

            int markerMask = 1 << markerLayer;

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                if (ShouldShowMarkerLayer(camera))
                {
                    camera.cullingMask &= ~hiddenFromMapLayers.value;
                    camera.cullingMask |= markerMask;
                }
                else if (hideMarkerLayerFromOtherCameras)
                {
                    camera.cullingMask &= ~markerMask;
                }
            }
        }

        private bool ShouldShowMarkerLayer(Camera camera)
        {
            if (camera == null)
                return false;

            if (camera == minimapCamera)
                return true;

            string cameraName = camera.name;

            return cameraName.IndexOf("Minimap", StringComparison.OrdinalIgnoreCase) >= 0
                   || cameraName.IndexOf("WorldMap", StringComparison.OrdinalIgnoreCase) >= 0
                   || cameraName.IndexOf("Worldmap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryFindLocalPlayer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return;

            NetworkClient localClient = networkManager.LocalClient;
            if (localClient == null)
                return;

            NetworkObject playerObject = localClient.PlayerObject;
            if (playerObject == null)
                return;

            target = playerObject.transform;

            Vector3 targetPosition = target.position;
            Transform cameraTransform = CameraTransform;
            cameraTransform.position = new Vector3(targetPosition.x, targetPosition.y + height, targetPosition.z);
            cameraTransform.rotation = TopDownRotation;
        }
    }
}

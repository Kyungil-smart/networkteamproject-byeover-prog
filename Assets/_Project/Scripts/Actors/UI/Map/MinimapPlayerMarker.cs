using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class MinimapPlayerMarker : NetworkBehaviour
    {
        [Header("마커")]
        [SerializeField] private SpriteRenderer markerRenderer;

        [Header("색상")]
        [SerializeField] private Color localPlayerColor = Color.green;
        [SerializeField] private Color teammateColor = Color.cyan;

        [Header("회전")]
        [SerializeField] private bool rotateWithPlayer = true;

        [Header("레이어")]
        [SerializeField] private string markerLayerName = "Minimap";

        private void Awake()
        {
            EnsureMarkerRenderer();
            ApplyMarkerLayer();
        }

        private void Reset()
        {
            EnsureMarkerRenderer();
        }

        private void OnValidate()
        {
            EnsureMarkerRenderer();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyMarkerLayer();
            ApplyColor();
        }

        private void LateUpdate()
        {
            ConfigureCameraMasks();

            if (!rotateWithPlayer)
                return;

            if (!EnsureMarkerRenderer())
                return;

            markerRenderer.transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
        }

        private bool EnsureMarkerRenderer()
        {
            if (markerRenderer != null)
                return true;

            markerRenderer = GetComponentInChildren<SpriteRenderer>();
            return markerRenderer != null;
        }

        private void ApplyMarkerLayer()
        {
            if (!EnsureMarkerRenderer())
                return;

            int markerLayer = LayerMask.NameToLayer(markerLayerName);
            if (markerLayer < 0)
                return;

            SetLayerRecursively(markerRenderer.gameObject, markerLayer);
        }

        private void ConfigureCameraMasks()
        {
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

                if (camera.name.Contains("Minimap"))
                    camera.cullingMask |= markerMask;
                else
                    camera.cullingMask &= ~markerMask;
            }
        }

        private void SetLayerRecursively(GameObject targetObject, int layer)
        {
            if (targetObject == null)
                return;

            targetObject.layer = layer;

            foreach (Transform child in targetObject.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private void ApplyColor()
        {
            if (!EnsureMarkerRenderer())
                return;

            markerRenderer.color = IsOwner ? localPlayerColor : teammateColor;
        }

        private void RefreshColor()
        {
            ApplyColor();
        }
    }
}

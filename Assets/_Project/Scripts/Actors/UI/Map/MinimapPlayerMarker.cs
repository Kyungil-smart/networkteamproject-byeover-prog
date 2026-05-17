using Unity.Netcode;
using UnityEngine;
using System;

using DeadZone.Actors.Player;

namespace DeadZone.Actors.UI
{
    public class MinimapPlayerMarker : NetworkBehaviour
    {
        [Header("마커")]
        [SerializeField] private SpriteRenderer markerRenderer;

        [Header("색상")]
        [SerializeField] private Color fallbackColor = Color.white;

        [Header("회전")]
        [SerializeField] private bool rotateWithPlayer = true;

        [Header("레이어")]
        [SerializeField] private string markerLayerName = "Minimap";
        [SerializeField] private LayerMask hiddenFromMapLayers = 1 << 9;

        private PlayerTeamIdentity teamIdentity;

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
            BindTeamIdentity();
            ApplyColor();
        }

        public override void OnNetworkDespawn()
        {
            UnbindTeamIdentity();
            base.OnNetworkDespawn();
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

                if (ShouldShowMarkerLayer(camera))
                {
                    camera.cullingMask &= ~hiddenFromMapLayers.value;
                    camera.cullingMask |= markerMask;
                }
                else
                {
                    camera.cullingMask &= ~markerMask;
                }
            }
        }

        private bool ShouldShowMarkerLayer(Camera camera)
        {
            if (camera == null)
                return false;

            string cameraName = camera.name;

            return cameraName.IndexOf("Minimap", StringComparison.OrdinalIgnoreCase) >= 0
                   || cameraName.IndexOf("WorldMap", StringComparison.OrdinalIgnoreCase) >= 0
                   || cameraName.IndexOf("Worldmap", StringComparison.OrdinalIgnoreCase) >= 0;
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

            markerRenderer.color = teamIdentity != null ? teamIdentity.CurrentColor : fallbackColor;
        }

        private void BindTeamIdentity()
        {
            UnbindTeamIdentity();

            teamIdentity = GetComponent<PlayerTeamIdentity>();
            if (teamIdentity == null)
                teamIdentity = GetComponentInParent<PlayerTeamIdentity>();
            if (teamIdentity == null)
                teamIdentity = GetComponentInChildren<PlayerTeamIdentity>(true);

            if (teamIdentity != null)
                teamIdentity.TeamColorChanged += HandleTeamColorChanged;
        }

        private void UnbindTeamIdentity()
        {
            if (teamIdentity != null)
                teamIdentity.TeamColorChanged -= HandleTeamColorChanged;

            teamIdentity = null;
        }

        private void HandleTeamColorChanged(Color32 color)
        {
            if (!EnsureMarkerRenderer())
                return;

            markerRenderer.color = color;
        }
    }
}

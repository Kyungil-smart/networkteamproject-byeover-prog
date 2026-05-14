using UnityEngine;
using UnityEngine.UI;

using DeadZone.Actors.Player;

namespace DeadZone.Actors
{
    public class MapMarkerFollower : MonoBehaviour
    {
        [SerializeField] private RectTransform mapRect;
        [SerializeField] private RectTransform markerRect;
        [SerializeField] private Transform target;
        [SerializeField] private Vector2 worldMin = new(-406.74f, -149.42f);
        [SerializeField] private Vector2 worldMax = new(6.08078f, 55.42514f);
        [SerializeField] private bool updateEveryFrame = true;
        [SerializeField] private bool clampToMap = true;
        [SerializeField] private bool invertY;

        [Header("색상")]
        [SerializeField] private Image markerImage;
        [SerializeField] private Color fallbackColor = Color.white;

        private PlayerTeamIdentity teamIdentity;

        private void Awake()
        {
            EnsureReferences();
            BindTeamIdentityFromTarget();
            ApplyTeamColor();
            UpdateMarkerPosition();
        }

        private void OnEnable()
        {
            BindTeamIdentityFromTarget();
            ApplyTeamColor();
            UpdateMarkerPosition();
        }

        private void OnDisable()
        {
            UnbindTeamIdentity();
        }

        private void LateUpdate()
        {
            if (updateEveryFrame)
                UpdateMarkerPosition();
        }

        public void Bind(Transform followTarget, RectTransform ownerMapRect = null)
        {
            target = followTarget;

            if (ownerMapRect != null)
                mapRect = ownerMapRect;

            BindTeamIdentityFromTarget();
            ApplyTeamColor();
            UpdateMarkerPosition();
        }

        private void EnsureReferences()
        {
            if (markerRect == null)
                markerRect = transform as RectTransform;

            if (markerImage == null)
                markerImage = GetComponent<Image>();
        }

        private void UpdateMarkerPosition()
        {
            EnsureReferences();

            if (mapRect == null || markerRect == null || target == null)
                return;

            float x01 = Mathf.InverseLerp(worldMin.x, worldMax.x, target.position.x);
            float y01 = Mathf.InverseLerp(worldMin.y, worldMax.y, target.position.z);

            if (invertY)
                y01 = 1f - y01;

            if (clampToMap)
            {
                x01 = Mathf.Clamp01(x01);
                y01 = Mathf.Clamp01(y01);
            }

            Rect rect = mapRect.rect;
            markerRect.anchoredPosition = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, x01),
                Mathf.Lerp(rect.yMin, rect.yMax, y01));
        }

        private void BindTeamIdentityFromTarget()
        {
            UnbindTeamIdentity();

            if (target == null)
                return;

            teamIdentity = target.GetComponent<PlayerTeamIdentity>();
            if (teamIdentity == null)
                teamIdentity = target.GetComponentInParent<PlayerTeamIdentity>();
            if (teamIdentity == null)
                teamIdentity = target.GetComponentInChildren<PlayerTeamIdentity>(true);

            if (teamIdentity != null)
                teamIdentity.TeamColorChanged += HandleTeamColorChanged;
        }

        private void UnbindTeamIdentity()
        {
            if (teamIdentity != null)
                teamIdentity.TeamColorChanged -= HandleTeamColorChanged;

            teamIdentity = null;
        }

        private void ApplyTeamColor()
        {
            EnsureReferences();

            if (markerImage == null)
                return;

            markerImage.color = teamIdentity != null ? teamIdentity.CurrentColor : fallbackColor;
        }

        private void HandleTeamColorChanged(Color32 color)
        {
            EnsureReferences();

            if (markerImage != null)
                markerImage.color = color;
        }
    }
}

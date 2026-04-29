using UnityEngine;

namespace DeadZone.Actors
{
    public static class MapCoordinateUtility
    {
        public static readonly Vector2 FullMapWorldMin = new(-406.74f, -149.42f);
        public static readonly Vector2 FullMapWorldMax = new(6.08078f, 55.42514f);
        public static readonly Vector2 FenceWorldMin = new(-285.20f, -139.10f);
        public static readonly Vector2 FenceWorldMax = new(-31.20f, 55.43f);

        public static Vector2 WorldToNormalized(
            Vector3 worldPosition,
            Vector2 worldMin,
            Vector2 worldMax,
            bool clampToMap,
            bool invertY)
        {
            float normalizedX = Mathf.InverseLerp(worldMin.x, worldMax.x, worldPosition.x);
            float normalizedY = Mathf.InverseLerp(worldMin.y, worldMax.y, worldPosition.z);

            if (clampToMap)
            {
                normalizedX = Mathf.Clamp01(normalizedX);
                normalizedY = Mathf.Clamp01(normalizedY);
            }

            if (invertY)
                normalizedY = 1f - normalizedY;

            return new Vector2(normalizedX, normalizedY);
        }

        public static Vector2 ApplyNormalizedCorrection(
            Vector2 normalized,
            Vector2 anchor,
            Vector2 scale,
            Vector2 offset,
            bool clampToMap)
        {
            normalized.x = anchor.x + (normalized.x - anchor.x) * scale.x + offset.x;
            normalized.y = anchor.y + (normalized.y - anchor.y) * scale.y + offset.y;

            if (clampToMap)
            {
                normalized.x = Mathf.Clamp01(normalized.x);
                normalized.y = Mathf.Clamp01(normalized.y);
            }

            return normalized;
        }

        public static bool ContainsWorldPosition(Vector3 worldPosition, Vector2 worldMin, Vector2 worldMax)
        {
            return worldPosition.x >= Mathf.Min(worldMin.x, worldMax.x) &&
                   worldPosition.x <= Mathf.Max(worldMin.x, worldMax.x) &&
                   worldPosition.z >= Mathf.Min(worldMin.y, worldMax.y) &&
                   worldPosition.z <= Mathf.Max(worldMin.y, worldMax.y);
        }

        public static void NormalizeBounds(ref Vector2 worldMin, ref Vector2 worldMax)
        {
            float minX = Mathf.Min(worldMin.x, worldMax.x);
            float minY = Mathf.Min(worldMin.y, worldMax.y);
            float maxX = Mathf.Max(worldMin.x, worldMax.x);
            float maxY = Mathf.Max(worldMin.y, worldMax.y);

            worldMin = new Vector2(minX, minY);
            worldMax = new Vector2(maxX, maxY);
        }

        public static Vector2 NormalizedToCenteredRectPosition(Vector2 normalized, RectTransform rectTransform)
        {
            float width = GetRectWidth(rectTransform);
            float height = GetRectHeight(rectTransform);

            float mapX = Mathf.Lerp(-width * 0.5f, width * 0.5f, normalized.x);
            float mapY = Mathf.Lerp(-height * 0.5f, height * 0.5f, normalized.y);

            return new Vector2(mapX, mapY);
        }

        public static float GetRectWidth(RectTransform rectTransform)
        {
            return rectTransform.rect.width > 0f ? rectTransform.rect.width : rectTransform.sizeDelta.x;
        }

        public static float GetRectHeight(RectTransform rectTransform)
        {
            return rectTransform.rect.height > 0f ? rectTransform.rect.height : rectTransform.sizeDelta.y;
        }
    }
}

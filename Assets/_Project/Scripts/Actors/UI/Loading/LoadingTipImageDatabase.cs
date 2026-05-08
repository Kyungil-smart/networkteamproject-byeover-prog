using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    [Serializable]
    public class LoadingTipSpecialImageEntry
    {
        public string imageKey;
        public Sprite sprite;
    }

    [CreateAssetMenu(
        fileName = "LoadingTipImageDatabase",
        menuName = "DeadZone/UI/Loading Tip Image Database")]
    public class LoadingTipImageDatabase : ScriptableObject
    {
        public List<Sprite> defaultBackgroundSprites = new();
        public List<LoadingTipSpecialImageEntry> specialImages = new();

        public bool IsSpecialImageKey(string imageKey)
        {
            return GetSpecialSprite(imageKey) != null;
        }

        public Sprite GetRandomDefaultBackground()
        {
            if (defaultBackgroundSprites == null || defaultBackgroundSprites.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, defaultBackgroundSprites.Count);
            return defaultBackgroundSprites[index];
        }

        public Sprite GetSpecialSprite(string imageKey)
        {
            imageKey = NormalizeImageKey(imageKey);

            if (imageKey == "Default")
                return null;

            if (specialImages == null)
                return null;

            for (int i = 0; i < specialImages.Count; i++)
            {
                LoadingTipSpecialImageEntry entry = specialImages[i];

                if (entry == null)
                    continue;

                if (NormalizeImageKey(entry.imageKey) == imageKey)
                    return entry.sprite;
            }

            return null;
        }

        private static string NormalizeImageKey(string imageKey)
        {
            return string.IsNullOrWhiteSpace(imageKey) ? "Default" : imageKey.Trim();
        }

    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Network
{
    public static class PartyPlayerColorCache
    {
        private static readonly Dictionary<ulong, uint> colorsByClientId = new();

        public static event Action Changed;

        public static void Set(ulong clientId, uint rgba)
        {
            colorsByClientId[clientId] = rgba;
            Changed?.Invoke();
        }

        public static void Remove(ulong clientId)
        {
            if (!colorsByClientId.Remove(clientId))
                return;

            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (colorsByClientId.Count == 0)
                return;

            colorsByClientId.Clear();
            Changed?.Invoke();
        }

        public static bool TryGetColor(ulong clientId, out Color32 color)
        {
            if (colorsByClientId.TryGetValue(clientId, out uint rgba))
            {
                color = ToColor32(rgba);
                return true;
            }

            color = Color.white;
            return false;
        }

        public static Color32 ToColor32(uint rgba)
        {
            return new Color32(
                (byte)((rgba >> 24) & 0xFF),
                (byte)((rgba >> 16) & 0xFF),
                (byte)((rgba >> 8) & 0xFF),
                (byte)(rgba & 0xFF));
        }

        public static uint ToRgba(Color32 color)
        {
            return ((uint)color.r << 24) |
                   ((uint)color.g << 16) |
                   ((uint)color.b << 8) |
                   color.a;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비에서 생성된 파티원 색상을 ClientId 기준으로 임시 저장합니다.
    /// 레이드 씬 진입 후 PlayerTeamIdentity가 이 값을 읽어 팀 색상을 적용합니다.
    /// </summary>
    public static class LobbyTeamColorCache
    {
        private static readonly Dictionary<ulong, Color32> ColorsByClientId = new();

        public static void SetColor(ulong clientId, Color32 color)
        {
            ColorsByClientId[clientId] = color;
        }

        public static bool TryGetColor(ulong clientId, out Color32 color)
        {
            return ColorsByClientId.TryGetValue(clientId, out color);
        }

        public static void Clear()
        {
            ColorsByClientId.Clear();
        }
    }
}
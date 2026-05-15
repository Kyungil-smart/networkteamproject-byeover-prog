using System.Collections.Generic;
using Unity.Collections;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비에서 확정된 플레이어 표시 이름을 레이드 씬 스폰 단계까지 임시 보관합니다.
    /// 서버가 PlayerDisplayNameIdentity에 주입하고, 실제 인게임 동기화는 NetworkVariable이 담당합니다.
    /// </summary>
    public static class LobbyPlayerDisplayNameCache
    {
        private const string DefaultFallbackName = "Player";
        private const int DefaultMaxCharacters = 20;

        private static readonly Dictionary<ulong, FixedString64Bytes> NamesByClientId = new();

        public static void SetName(ulong clientId, FixedString64Bytes displayName)
        {
            NamesByClientId[clientId] = Sanitize(displayName.ToString());
        }

        public static void SetName(ulong clientId, string displayName)
        {
            NamesByClientId[clientId] = Sanitize(displayName);
        }

        public static bool TryGetName(ulong clientId, out FixedString64Bytes displayName)
        {
            return NamesByClientId.TryGetValue(clientId, out displayName);
        }

        public static void Clear()
        {
            NamesByClientId.Clear();
        }

        private static FixedString64Bytes Sanitize(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value)
                ? DefaultFallbackName
                : value.Trim();

            if (sanitized.Length > DefaultMaxCharacters)
                sanitized = sanitized.Substring(0, DefaultMaxCharacters);

            return new FixedString64Bytes(sanitized);
        }
    }
}
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Network;

namespace DeadZone.Actors.Player
{
    /// <summary>
    /// 레이드 중 플레이어 표시 이름을 네트워크로 동기화합니다.
    /// 서버만 값을 쓰고, 모든 클라이언트가 읽어 이름표 UI에 사용합니다.
    /// </summary>
    public sealed class PlayerDisplayNameIdentity : NetworkBehaviour
    {
        [Header("==== 표시 이름 ====")]
        [SerializeField] private string fallbackDisplayName = "Player";

        [SerializeField, Range(1, 20)]
        private int maxDisplayNameCharacters = 20;

        public NetworkVariable<FixedString64Bytes> DisplayName = new(
            new FixedString64Bytes("Player"),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public event Action<string> DisplayNameChanged;

        public string CurrentDisplayName => DisplayName.Value.ToString();

        public override void OnNetworkSpawn()
        {
            DisplayName.OnValueChanged += HandleDisplayNameChanged;

            if (IsServer)
                ApplyLobbyDisplayNameServer();

            DisplayNameChanged?.Invoke(CurrentDisplayName);
        }

        public override void OnNetworkDespawn()
        {
            DisplayName.OnValueChanged -= HandleDisplayNameChanged;
        }

        public void ApplyLobbyDisplayNameServer()
        {
            if (!IsServer)
                return;

            if (LobbyPlayerDisplayNameCache.TryGetName(OwnerClientId, out FixedString64Bytes cachedName))
            {
                DisplayName.Value = Sanitize(cachedName.ToString());
                return;
            }

            DisplayName.Value = Sanitize(fallbackDisplayName);
        }

        public void SetDisplayNameServer(string displayName)
        {
            if (!IsServer)
                return;

            DisplayName.Value = Sanitize(displayName);
        }

        private void HandleDisplayNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            DisplayNameChanged?.Invoke(newValue.ToString());
        }

        private FixedString64Bytes Sanitize(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value)
                ? fallbackDisplayName
                : value.Trim();

            if (sanitized.Length > maxDisplayNameCharacters)
                sanitized = sanitized.Substring(0, maxDisplayNameCharacters);

            return new FixedString64Bytes(sanitized);
        }
    }
}

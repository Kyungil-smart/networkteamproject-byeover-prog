using System;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Network;

namespace DeadZone.Actors.Player
{
    /// <summary>
    /// 레이드 중 플레이어의 팀 색상을 네트워크로 동기화합니다.
    /// 미니맵, 파티 HUD, 팀 표시 UI에서 사용합니다.
    /// </summary>
    public class PlayerTeamIdentity : NetworkBehaviour
    {
        public struct NetworkColor32 : INetworkSerializable, IEquatable<NetworkColor32>
        {
            public byte R;
            public byte G;
            public byte B;
            public byte A;

            public NetworkColor32(Color32 color)
            {
                R = color.r;
                G = color.g;
                B = color.b;
                A = color.a;
            }

            public Color32 ToColor32()
            {
                return new Color32(R, G, B, A);
            }

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref R);
                serializer.SerializeValue(ref G);
                serializer.SerializeValue(ref B);
                serializer.SerializeValue(ref A);
            }

            public bool Equals(NetworkColor32 other)
            {
                return R == other.R
                       && G == other.G
                       && B == other.B
                       && A == other.A;
            }
        }

        [Header("==== 팀 색상 ====")]
        [Tooltip("로비 색상을 찾지 못했을 때 사용할 기본 색상입니다.")]
        [SerializeField] private Color32 fallbackColor = new Color32(255, 255, 255, 255);

        public NetworkVariable<NetworkColor32> TeamColor = new(
            new NetworkColor32(new Color32(255, 255, 255, 255)),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public event Action<Color32> TeamColorChanged;

        public Color32 CurrentColor => TeamColor.Value.ToColor32();

        public override void OnNetworkSpawn()
        {
            TeamColor.OnValueChanged += HandleTeamColorChanged;

            if (IsServer)
                ApplyLobbyColorServer();

            TeamColorChanged?.Invoke(CurrentColor);
        }

        public override void OnNetworkDespawn()
        {
            TeamColor.OnValueChanged -= HandleTeamColorChanged;
        }

        private void HandleTeamColorChanged(NetworkColor32 previousValue, NetworkColor32 newValue)
        {
            TeamColorChanged?.Invoke(newValue.ToColor32());
        }

        /// <summary>
        /// 서버에서 로비 색상 캐시를 읽어 TeamColor NetworkVariable에 반영합니다.
        /// </summary>
        public void ApplyLobbyColorServer()
        {
            if (!IsServer)
                return;

            if (LobbyTeamColorCache.TryGetColor(OwnerClientId, out Color32 cachedColor))
            {
                TeamColor.Value = new NetworkColor32(cachedColor);
                return;
            }

            TeamColor.Value = new NetworkColor32(fallbackColor);
        }
    }
}
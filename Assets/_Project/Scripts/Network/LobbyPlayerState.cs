using System;
using Unity.Collections;
using Unity.Netcode;

namespace DeadZone.Network
{
    /// <summary>
    /// 로비에 참가한 플레이어 1명의 네트워크 동기화 상태입니다.
    /// </summary>
    public struct LobbyPlayerState : INetworkSerializable, IEquatable<LobbyPlayerState>
    {
        public ulong ClientId;
        public FixedString64Bytes DisplayName;
        public bool IsHost;
        public bool IsReady;
        public bool HasEscapedMapA;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref HasEscapedMapA);
        }

        public bool Equals(LobbyPlayerState other)
        {
            return ClientId == other.ClientId
                   && DisplayName.Equals(other.DisplayName)
                   && IsHost == other.IsHost
                   && IsReady == other.IsReady
                   && HasEscapedMapA == other.HasEscapedMapA;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbyPlayerState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ClientId.GetHashCode();
                hash = (hash * 397) ^ DisplayName.GetHashCode();
                hash = (hash * 397) ^ IsHost.GetHashCode();
                hash = (hash * 397) ^ IsReady.GetHashCode();
                hash = (hash * 397) ^ HasEscapedMapA.GetHashCode();
                return hash;
            }
        }
    }
}

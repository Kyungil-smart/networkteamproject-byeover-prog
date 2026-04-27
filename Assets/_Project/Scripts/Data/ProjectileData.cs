using Unity.Netcode;

namespace DeadZone.Actors 
{
    public struct ProjectileData : INetworkSerializable
    {
        public ulong ShooterId;
        public int BaseDamage;
        public int Penetration;
        
        // 조준 의도 데이터
        public ulong TargetNetId; 
        public bool WasHeadAim;
        public float Range;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) 
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ShooterId);
            serializer.SerializeValue(ref BaseDamage);
            serializer.SerializeValue(ref Penetration);
            serializer.SerializeValue(ref TargetNetId);
            serializer.SerializeValue(ref WasHeadAim);
            serializer.SerializeValue(ref Range);
        }
    }
}

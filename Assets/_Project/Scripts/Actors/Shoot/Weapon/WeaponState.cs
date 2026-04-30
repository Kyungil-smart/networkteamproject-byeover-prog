using Unity.Collections;
using Unity.Netcode;
using System;

namespace DeadZone.Actors
{
    /// <summary>
    /// 장착된 무기의 실시간 상태(잔탄, 탄종)를 관리하는 구조체입니다.
    /// </summary>
    public struct WeaponState : INetworkSerializable, IEquatable<WeaponState>
    {
        public int currentAmmo;              // 현재 탄창 내 잔탄수
        public FixedString64Bytes loadedAmmoId; // 장전된 탄약의 아이템 ID

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) 
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref currentAmmo);
            serializer.SerializeValue(ref loadedAmmoId);
        }

        public bool Equals(WeaponState other)
        {
            return currentAmmo == other.currentAmmo && 
                   loadedAmmoId.Equals(other.loadedAmmoId);
        }

        public override bool Equals(object obj) => 
            obj is WeaponState other && Equals(other);

        public override int GetHashCode() => 
            HashCode.Combine(currentAmmo, loadedAmmoId);
    }
}
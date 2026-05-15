using System;
using Unity.Collections;
using Unity.Netcode;


namespace DeadZone.Systems
{
    /// <summary>
    /// 그리드 인벤토리의 네트워크 직렬화 슬롯 데이터.
    /// </summary>
    public struct ItemSlotData : INetworkSerializable, IEquatable<ItemSlotData>
    {
        public FixedString64Bytes itemId;
        public byte gridX;
        public byte gridY;
        public bool rotated;
        public ushort stackCount;
        public float currentDurability;
        public ushort currentAmmo;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref itemId);
            s.SerializeValue(ref gridX);
            s.SerializeValue(ref gridY);
            s.SerializeValue(ref rotated);
            s.SerializeValue(ref stackCount);
            s.SerializeValue(ref currentDurability);
            s.SerializeValue(ref currentAmmo);
        }

        public bool Equals(ItemSlotData other)
        {
            return itemId.Equals(other.itemId)
                && gridX == other.gridX
                && gridY == other.gridY
                && rotated == other.rotated
                && stackCount == other.stackCount;
        }

        public override bool Equals(object obj) => obj is ItemSlotData o && Equals(o);
        public override int GetHashCode() => itemId.GetHashCode() ^ (gridX << 8) ^ gridY;
    }

    public struct QuickSlotData : INetworkSerializable, IEquatable<QuickSlotData>
    {
        public byte slotIndex;
        public FixedString64Bytes itemId;
        public ushort stackCount;
        public float currentDurability;
        public ushort currentAmmo;

        public bool IsEmpty => itemId.Length == 0 || stackCount == 0;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref slotIndex);
            s.SerializeValue(ref itemId);
            s.SerializeValue(ref stackCount);
            s.SerializeValue(ref currentDurability);
            s.SerializeValue(ref currentAmmo);
        }

        public bool Equals(QuickSlotData other)
        {
            return slotIndex == other.slotIndex
                && itemId.Equals(other.itemId)
                && stackCount == other.stackCount
                && currentDurability.Equals(other.currentDurability)
                && currentAmmo == other.currentAmmo;
        }

        public override bool Equals(object obj) => obj is QuickSlotData o && Equals(o);
        public override int GetHashCode() => itemId.GetHashCode() ^ slotIndex;
    }
}

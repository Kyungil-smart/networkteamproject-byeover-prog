// ============================================================================
// KHWContainerSlotNetData.cs
// 목적: 파밍 상자 슬롯 1칸의 네트워크 동기화 데이터입니다.
// 패턴: 값 타입 struct + NGO INetworkSerializable + IEquatable.
// 적용: 직접 컴포넌트로 붙이지 않고 KHWLootContainer의 NetworkList 슬롯 타입으로 사용합니다.
// ============================================================================
using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// [KHW 추가 스크립트]
/// 파밍 상자 UI에 표시할 1칸 슬롯 데이터입니다.
/// NetworkList에 넣기 위해 INetworkSerializable을 구현합니다.
/// </summary>
public struct KHWContainerSlotNetData : INetworkSerializable, IEquatable<KHWContainerSlotNetData>
{
    public FixedString64Bytes itemId;
    public ushort amount;

    public bool IsEmpty
    {
        get
        {
            return string.IsNullOrEmpty(itemId.ToString()) || amount == 0;
        }
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref amount);
    }

    public bool Equals(KHWContainerSlotNetData other)
    {
        return itemId.Equals(other.itemId) && amount == other.amount;
    }

    public override bool Equals(object obj)
    {
        if (obj is KHWContainerSlotNetData)
        {
            return Equals((KHWContainerSlotNetData)obj);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return itemId.GetHashCode() ^ amount.GetHashCode();
    }
}

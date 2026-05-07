// ============================================================================
// 목적: 파밍 상자 슬롯 1칸의 네트워크 동기화 데이터입니다.
// 패턴: 값 타입 struct + NGO INetworkSerializable + IEquatable.
// 적용: 직접 컴포넌트로 붙이지 않고 LootContainer의 NetworkList 슬롯 타입으로 사용합니다.
// ============================================================================
using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// [파밍 상자 슬롯 네트워크 데이터]
/// 패턴: 값 타입 DTO + NGO INetworkSerializable.
/// 역할: 파밍 상자 내부 슬롯 1칸의 itemID와 수량을 NetworkList에 저장한다.
/// 설명: MonoBehaviour가 아니므로 오브젝트에 컴포넌트로 붙이지 않는다.
/// NetworkList에 넣기 위해 INetworkSerializable을 구현합니다.
/// </summary>
public struct ContainerSlotNetData : INetworkSerializable, IEquatable<ContainerSlotNetData>
{
    // itemId: ItemDataSO.itemID를 네트워크로 안전하게 보내기 위해 FixedString64Bytes 사용.
    public FixedString64Bytes itemId;

    // amount: 슬롯 안에 들어있는 아이템 수량. 테스트 단계에서는 ushort로 충분하다.
    public ushort amount;

    /// <summary>
    /// 슬롯이 비어 있는지 확인한다.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            return string.IsNullOrEmpty(itemId.ToString()) || amount == 0;
        }
    }

    /// <summary>
    /// NGO가 NetworkList 데이터를 동기화할 때 호출한다.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref amount);
    }

    /// <summary>
    /// NetworkList 내부 비교용 Equals 구현.
    /// </summary>
    public bool Equals(ContainerSlotNetData other)
    {
        return itemId.Equals(other.itemId) && amount == other.amount;
    }

    /// <summary>
    /// object 비교용 Equals 구현.
    /// </summary>
    public override bool Equals(object obj)
    {
        if (obj is ContainerSlotNetData)
        {
            return Equals((ContainerSlotNetData)obj);
        }

        return false;
    }

    /// <summary>
    /// Dictionary/HashSet 사용 가능성을 위한 HashCode 구현.
    /// </summary>
    public override int GetHashCode()
    {
        return itemId.GetHashCode() ^ amount.GetHashCode();
    }
}

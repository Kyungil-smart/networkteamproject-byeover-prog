// ============================================================================
// KHWLootDebugFormatter.cs
// 목적: Console에 출력할 파밍 아이템 정보를 보기 좋은 문자열로 만들어 줍니다.
// 패턴: static Utility / Formatter.
// 적용: 오브젝트에 붙이지 않습니다. KHWLootContainer에서 코드로 호출합니다.
// ============================================================================
using UnityEngine;

using DeadZone.Core;

/// <summary>
/// [KHW 추가 스크립트]
/// 파밍 상자를 열었을 때 Console 창에 아이템 정보를 보기 좋게 출력하기 위한 문자열 생성 유틸리티입니다.
/// UI 없이 Debug.Log 테스트를 하기 위해 추가했습니다.
/// </summary>
public static class KHWLootDebugFormatter
{
    public static string GetItemCategory(ItemDataSO item)
    {
        if (item == null)
        {
            return "없음";
        }

        if (item is WeaponDataSO)
        {
            return "무기";
        }

        if (item is AmmoDataSO)
        {
            return "탄약";
        }

        if (item is HelmetDataSO)
        {
            return "헬멧";
        }

        if (item is ArmorDataSO)
        {
            return "방어구";
        }

        return "일반 아이템";
    }

    public static string BuildItemLogLine(int slotIndex, ItemDataSO item, int amount)
    {
        if (item == null)
        {
            return "[" + slotIndex + "] 비어 있음";
        }

        string category = GetItemCategory(item);
        string displayName = item.displayName;
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = item.name;
        }

        string line = "[" + slotIndex + "] "
            + "분류=" + category
            + " / ID=" + item.itemID
            + " / 이름=" + displayName
            + " / 수량=" + amount
            + " / 최대스택=" + item.maxStackSize
            + " / 크기=1x1 테스트 규칙";

        return line;
    }
}

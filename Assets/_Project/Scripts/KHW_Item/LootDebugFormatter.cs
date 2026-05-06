// ============================================================================
// KHWLootDebugFormatter.cs
// 목적: Console에 출력할 파밍 아이템 정보를 보기 좋은 문자열로 만들어 줍니다.
// 패턴: static Utility / Formatter.
// 적용: 오브젝트에 붙이지 않습니다. KHWLootContainer에서 코드로 호출합니다.
// ============================================================================
using DeadZone.Core;
using System.Text;
using UnityEngine;

/// <summary>
/// [루팅 디버그 출력 포맷터]
/// 패턴: static Formatter Utility.
/// 역할: 파밍 상자 결과를 Console에서 읽기 좋은 문자열로 만든다.
/// 설명: 오브젝트에 붙이지 않고 LootContainer에서 코드로 호출한다.
/// </summary>
public static class LootDebugFormatter
{
    /// <summary>
    /// ItemDataSO 실제 타입을 한글 분류명으로 변환한다.
    /// </summary>
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

        string displayName = item.displayName;
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = item.name;
        }

        string line = "[" + slotIndex + "] "
            + "분류=" + GetItemCategory(item)
            + " / ID=" + item.itemID
            + " / 이름=" + displayName
            + " / 수량=" + amount
            + " / 최대스택=" + item.maxStackSize
            + " / 희귀도=" + item.rarity
            + " / 카테고리=" + item.category
            + " / 크기=1x1 규칙";

        return line;
    }

    public static string BuildContainerLog(
        string containerName,
        string containerGrade,
        ulong clientId,
        int slotCount,
        int rollCount,
        int totalWeight,
        ContainerSlotNetData[] slots,
        LootTableSO lootTable)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("========== [파밍 상자 오픈 로그] ==========");
        builder.AppendLine("상자 이름: " + containerName);
        builder.AppendLine("상자 등급: " + containerGrade);
        builder.AppendLine("상호작용 ClientId: " + clientId);
        builder.AppendLine("슬롯 수: " + slotCount + " / 랜덤 생성 수: " + rollCount);
        builder.AppendLine("LootTable Weight 총합: " + totalWeight + "%");
        builder.AppendLine("---------------------------------------------");

        if (slots == null || slots.Length == 0)
        {
            builder.AppendLine("슬롯 데이터가 없습니다.");
        }
        else
        {
            for (int i = 0; i < slots.Length; i++)
            {
                ContainerSlotNetData slot = slots[i];

                if (slot.IsEmpty)
                {
                    builder.AppendLine("[" + i + "] 비어 있음");
                    continue;
                }

                ItemDataSO item = FindItemInLootTable(lootTable, slot.itemId.ToString());
                builder.AppendLine(BuildItemLogLine(i, item, slot.amount));
            }
        }

        builder.AppendLine("=============================================");
        return builder.ToString();
    }

    private static ItemDataSO FindItemInLootTable(LootTableSO lootTable, string itemId)
    {
        if (lootTable == null || lootTable.entries == null || string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        for (int i = 0; i < lootTable.entries.Length; i++)
        {
            ItemDataSO item = lootTable.entries[i].item;

            if (item == null)
            {
                continue;
            }

            if (item.itemID == itemId)
            {
                return item;
            }
        }

        return null;
    }
}

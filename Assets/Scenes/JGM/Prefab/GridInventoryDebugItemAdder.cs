using System;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class GridInventoryDebugItemAdder : MonoBehaviour
    {
        [Serializable]
        private sealed class TestItemEntry
        {
            [Tooltip("추가할 아이템 SO입니다.")]
            public ItemDataSO item;

            [Tooltip("추가할 수량입니다.")]
            public int amount = 1;
        }

        [Header("대상 인벤토리")]
        [SerializeField]
        private GridInventory gridInventory;

        [Header("테스트 아이템 목록")]
        [SerializeField]
        private TestItemEntry[] testItems;

        private void Reset()
        {
            gridInventory = GetComponent<GridInventory>();
        }

        [ContextMenu("테스트 아이템 전체 추가")]
        private void AddAllTestItems()
        {
            if (gridInventory == null)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] GridInventory가 연결되지 않았습니다.", this);
                return;
            }

            if (!gridInventory.IsServer)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] 서버/Host 상태에서만 아이템을 추가할 수 있습니다.", this);
                return;
            }

            if (testItems == null || testItems.Length == 0)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] 추가할 테스트 아이템이 없습니다.", this);
                return;
            }

            int successCount = 0;

            for (int i = 0; i < testItems.Length; i++)
            {
                TestItemEntry entry = testItems[i];

                if (entry == null || entry.item == null || entry.amount <= 0)
                    continue;

                bool success = gridInventory.TryAddItem(entry.item, entry.amount);

                if (success)
                    successCount++;

                Debug.Log(
                    $"[GridInventoryDebugItemAdder] {entry.item.itemID} x{entry.amount} 추가 결과: {success}",
                    this);
            }

            Debug.Log($"[GridInventoryDebugItemAdder] 테스트 아이템 추가 완료: {successCount}/{testItems.Length}", this);
        }
    }
}
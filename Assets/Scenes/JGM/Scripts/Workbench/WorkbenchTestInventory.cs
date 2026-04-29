using System;
using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{

    // UI와 Player 인벤토리가 아직 없을 때 작업대 제작 로직을 테스트하기 위한 임시 인벤토리이다.
    // 실제 게임 최종 구조에서는 Player의 GridInventory가 IInventory 역할을 맡고,
    // 이 스크립트는 개발 테스트용으로만 사용한다.

    public class WorkbenchTestInventory : MonoBehaviour, IInventory
    {
        [Serializable]
        private class TestItemStack
        {
            [Tooltip("테스트 인벤토리에 넣을 아이템 데이터")]
            public ItemDataSO item;

            [Min(0)]
            [Tooltip("아이템 보유 개수")]
            public int amount;
        }


        [Header("시작 보유 아이템")]

        [Tooltip("테스트 시작 시 가지고 있는 재료 아이템 목록입니다.")]
        [SerializeField] private List<TestItemStack> startingItems = new List<TestItemStack>();


        [Header("현재 보유 아이템")]

        [Tooltip("플레이 중 실제로 변화하는 테스트 인벤토리 상태입니다.")]
        [SerializeField] private List<TestItemStack> currentItems = new List<TestItemStack>();


        [Header("제작 결과 기록")]

        [Tooltip("제작 성공으로 추가된 아이템을 확인하기 위한 기록입니다.")]
        [SerializeField] private List<TestItemStack> craftedItems = new List<TestItemStack>();


        private readonly Dictionary<string, TestItemStack> itemLookup = new Dictionary<string, TestItemStack>();


        private void Awake()
        {
            ResetInventory();
        }

        [ContextMenu("Reset Test Inventory")]
        public void ResetInventory()
        {
            currentItems.Clear();
            craftedItems.Clear();
            itemLookup.Clear();

            for (int i = 0; i < startingItems.Count; i++)
            {
                TestItemStack source = startingItems[i];

                if (source == null)
                    continue;

                if (source.item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(source.item.itemID))
                    continue;

                int amount = Mathf.Max(0, source.amount);

                if (amount <= 0)
                    continue;

                AddToCurrentItems(source.item, amount);
            }

            Debug.Log("[WorkbenchTestInventory] 테스트 인벤토리를 초기화했습니다.", this);
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (item == null)
                return false;

            if (string.IsNullOrWhiteSpace(item.itemID))
                return false;

            if (amount <= 0)
                return false;

            AddToCurrentItems(item, amount);
            AddToCraftedItems(item, amount);

            Debug.Log($"[WorkbenchTestInventory] 아이템 추가: {item.itemID} x{amount}", this);
            return true;
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (count <= 0)
                return false;

            if (!itemLookup.TryGetValue(itemId, out TestItemStack stack))
                return false;

            return stack.amount >= count;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (count <= 0)
                return false;

            if (!itemLookup.TryGetValue(itemId, out TestItemStack stack))
                return false;

            if (stack.amount < count)
                return false;

            stack.amount -= count;

            if (stack.amount <= 0)
            {
                currentItems.Remove(stack);
                itemLookup.Remove(itemId);
            }

            Debug.Log($"[WorkbenchTestInventory] 아이템 소모: {itemId} x{count}", this);
            return true;
        }

        private void AddToCurrentItems(ItemDataSO item, int amount)
        {
            if (itemLookup.TryGetValue(item.itemID, out TestItemStack stack))
            {
                stack.amount += amount;
                return;
            }

            TestItemStack newStack = new TestItemStack
            {
                item = item,
                amount = amount
            };

            currentItems.Add(newStack);
            itemLookup.Add(item.itemID, newStack);
        }

        private void AddToCraftedItems(ItemDataSO item, int amount)
        {
            for (int i = 0; i < craftedItems.Count; i++)
            {
                TestItemStack stack = craftedItems[i];

                if (stack == null)
                    continue;

                if (stack.item == item)
                {
                    stack.amount += amount;
                    return;
                }
            }

            craftedItems.Add(new TestItemStack
            {
                item = item,
                amount = amount
            });
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 작업대 제작 테스트용 임시 인벤토리
    // 실제 Player 인벤토리가 완성되기 전까지 IInventory 흐름을 검증하는 용도로만 사용
    [DisallowMultipleComponent]
    public class WorkbenchTestInventory : MonoBehaviour, IInventory
    {
        [Serializable]
        private class TestInventoryItem
        {
            [Tooltip("테스트 인벤토리에 넣을 아이템 데이터입니다.")]
            public ItemDataSO item;

            [Min(0)]
            [Tooltip("테스트 인벤토리에 보유한 아이템 수량입니다.")]
            public int amount;
        }

        [Header("테스트 보유 아이템")]
        [SerializeField]
        [Tooltip("플레이 시작 전에 테스트로 보유할 아이템 목록입니다.")]
        private List<TestInventoryItem> startItems = new();

        [Header("로그")]
        [SerializeField]
        [Tooltip("아이템 추가, 소모, 부족 상황을 Console에 출력할지 여부입니다.")]
        private bool logInventoryChange = true;

        private readonly Dictionary<string, InventoryItemState> itemStates = new();

        private void Awake()
        {
            RebuildInventory();
        }

        private void OnValidate()
        {
            RemoveInvalidItems();
        }

        private void RebuildInventory()
        {
            itemStates.Clear();

            for (int i = 0; i < startItems.Count; i++)
            {
                TestInventoryItem entry = startItems[i];

                if (entry == null || entry.item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.item.itemID))
                    continue;

                if (entry.amount <= 0)
                    continue;

                AddItemInternal(entry.item, entry.amount);
            }
        }

        private void RemoveInvalidItems()
        {
            if (startItems == null)
                return;

            for (int i = startItems.Count - 1; i >= 0; i--)
            {
                TestInventoryItem entry = startItems[i];

                if (entry == null)
                {
                    startItems.RemoveAt(i);
                    continue;
                }

                if (entry.amount < 0)
                    entry.amount = 0;
            }
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (item == null)
            {
                LogWarning("추가할 아이템 데이터가 없습니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.itemID))
            {
                LogWarning($"{item.name} 아이템의 itemID가 비어 있습니다.");
                return false;
            }

            if (amount <= 0)
            {
                LogWarning($"추가 수량이 올바르지 않습니다. itemID: {item.itemID}, amount: {amount}");
                return false;
            }

            AddItemInternal(item, amount);

            if (logInventoryChange)
                Debug.Log($"[WorkbenchTestInventory] 아이템 추가: {item.displayName}({item.itemID}) x{amount}", this);

            return true;
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (count <= 0)
                return true;

            return itemStates.TryGetValue(itemId, out InventoryItemState state)
                   && state.Amount >= count;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (count <= 0)
                return true;

            if (!itemStates.TryGetValue(itemId, out InventoryItemState state))
            {
                LogWarning($"소모할 아이템이 없습니다. itemID: {itemId}");
                return false;
            }

            if (state.Amount < count)
            {
                LogWarning($"아이템 수량이 부족합니다. itemID: {itemId}, 필요: {count}, 보유: {state.Amount}");
                return false;
            }

            state.Amount -= count;

            if (state.Amount <= 0)
                itemStates.Remove(itemId);
            else
                itemStates[itemId] = state;

            if (logInventoryChange)
                Debug.Log($"[WorkbenchTestInventory] 아이템 소모: {state.DisplayName}({itemId}) x{count}", this);

            return true;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            return itemStates.TryGetValue(itemId, out InventoryItemState state)
                ? state.Amount
                : 0;
        }

        [ContextMenu("테스트 인벤토리 다시 만들기")]
        public void RebuildInventoryForTest()
        {
            RebuildInventory();
            Debug.Log("[WorkbenchTestInventory] 테스트 인벤토리를 초기 설정값으로 다시 만들었습니다.", this);
        }

        [ContextMenu("테스트 인벤토리 출력")]
        public void PrintInventoryForTest()
        {
            if (itemStates.Count == 0)
            {
                Debug.Log("[WorkbenchTestInventory] 현재 보유 아이템이 없습니다.", this);
                return;
            }

            foreach (KeyValuePair<string, InventoryItemState> pair in itemStates)
            {
                InventoryItemState state = pair.Value;
                Debug.Log($"[WorkbenchTestInventory] 보유 아이템: {state.DisplayName}({pair.Key}) x{state.Amount}", this);
            }
        }

        private void AddItemInternal(ItemDataSO item, int amount)
        {
            string itemId = item.itemID;

            if (itemStates.TryGetValue(itemId, out InventoryItemState state))
            {
                state.Amount += amount;
                itemStates[itemId] = state;
                return;
            }

            itemStates.Add(itemId, new InventoryItemState
            {
                Item = item,
                DisplayName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName,
                Amount = amount
            });
        }

        private void LogWarning(string message)
        {
            if (!logInventoryChange)
                return;

            Debug.LogWarning($"[WorkbenchTestInventory] {message}", this);
        }

        private struct InventoryItemState
        {
            public ItemDataSO Item;
            public string DisplayName;
            public int Amount;
        }
    }
}
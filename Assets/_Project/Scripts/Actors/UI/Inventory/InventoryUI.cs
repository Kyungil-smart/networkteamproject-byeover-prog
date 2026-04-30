using System.Collections.Generic;
using DeadZone.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class InventoryUI : MonoBehaviour
    {
        [BoxGroup("루트")]
        [Tooltip("켜고 끌 InventoryVisibleRoot 오브젝트입니다. Inventory와 QuickSlotPanel은 이 루트 밖에서 켜진 상태를 유지해야 합니다.")]
        [SerializeField] private GameObject inventoryRoot;

        [BoxGroup("가방 설정")]
        [Tooltip("현재 가방 레벨입니다.")]
        [Range(1, 3)]
        [SerializeField] private int bagLevel = 1;

        [BoxGroup("가방 슬롯")]
        [Tooltip("가방 슬롯 20개를 순서대로 넣으세요.")]
        [SerializeField] private List<InventorySlotUI> bagSlots = new();

        [BoxGroup("ItemDataSO 테스트")]
        [Tooltip("랜덤 아이템 배치 테스트에 사용할 ItemDataSO 목록입니다.")]
        [SerializeField] private List<ItemDataSO> testItemPool = new();

        public bool IsOpen => inventoryRoot != null && inventoryRoot.activeSelf;

        private void Awake()
        {
            InitializeSlots();
            RefreshBagSlots();

            Close();
        }

        private void OnValidate()
        {
            if (bagSlots == null)
                return;

            if (!Application.isPlaying)
                return;

            RefreshBagSlots();
        }

        private void InitializeSlots()
        {
            for (int i = 0; i < bagSlots.Count; i++)
            {
                if (bagSlots[i] == null)
                    continue;

                bagSlots[i].Initialize(i);
            }
        }

        public void Open()
        {
            if (inventoryRoot == null)
            {
                Debug.LogWarning("[InventoryUI] Inventory Root가 연결되지 않았습니다.");
                return;
            }

            inventoryRoot.SetActive(true);
            RefreshBagSlots();
        }

        public void Close()
        {
            if (inventoryRoot == null)
                return;

            inventoryRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void SetBagLevel(int level)
        {
            bagLevel = Mathf.Clamp(level, 1, 3);
            RefreshBagSlots();
        }

        [BoxGroup("테스트")]
        [Button("슬롯 잠금 상태 새로고침")]
        public void RefreshBagSlots()
        {
            if (bagSlots == null)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.");
                return;
            }

            int unlockedCount = GetCapacityByBagLevel(bagLevel);

            for (int i = 0; i < bagSlots.Count; i++)
            {
                if (bagSlots[i] == null)
                    continue;

                bool locked = i >= unlockedCount;
                bagSlots[i].SetLocked(locked);
            }
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 1개 배치")]
        private void FillRandomItem1()
        {
            FillRandomItems(1);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 5개 배치")]
        private void FillRandomItems5()
        {
            FillRandomItems(5);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 10개 배치")]
        private void FillRandomItems10()
        {
            FillRandomItems(10);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 15개 배치")]
        private void FillRandomItems15()
        {
            FillRandomItems(15);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 20개 배치")]
        private void FillRandomItems20()
        {
            FillRandomItems(20);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("모든 슬롯 비우기")]
        public void ClearAllSlots()
        {
            if (bagSlots == null || bagSlots.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.");
                return;
            }

            foreach (InventorySlotUI slot in bagSlots)
            {
                if (slot == null)
                    continue;

                slot.ClearItem();
            }

            RefreshBagSlots();
        }

        public void FillRandomItems(int count)
        {
            if (bagSlots == null || bagSlots.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.");
                return;
            }

            ClearAssignedSlots();

            if (testItemPool == null || testItemPool.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Test Item Pool이 비어 있습니다.");
                RefreshBagSlots();
                return;
            }

            int unlockedCount = Mathf.Min(GetCapacityByBagLevel(bagLevel), bagSlots.Count);
            int fillCount = Mathf.Clamp(count, 0, unlockedCount);

            for (int i = 0; i < fillCount; i++)
            {
                InventorySlotUI slot = bagSlots[i];
                if (slot == null)
                    continue;

                ItemDataSO itemData = GetRandomTestItem();
                if (itemData == null)
                    continue;

                slot.SetItem(itemData, GetRandomStackCount(itemData));
            }

            RefreshBagSlots();
        }

        [BoxGroup("테스트")]
        [Button("인벤토리 열기")]
        private void TestOpen()
        {
            Open();
        }

        [BoxGroup("테스트")]
        [Button("인벤토리 닫기")]
        private void TestClose()
        {
            Close();
        }

        [BoxGroup("테스트")]
        [Button("인벤토리 토글")]
        private void TestToggle()
        {
            Toggle();
        }

        [BoxGroup("테스트")]
        [Button("가방 1레벨")]
        private void TestBagLevel1()
        {
            SetBagLevel(1);
        }

        [BoxGroup("테스트")]
        [Button("가방 2레벨")]
        private void TestBagLevel2()
        {
            SetBagLevel(2);
        }

        [BoxGroup("테스트")]
        [Button("가방 3레벨")]
        private void TestBagLevel3()
        {
            SetBagLevel(3);
        }

        private void ClearAssignedSlots()
        {
            foreach (InventorySlotUI slot in bagSlots)
            {
                if (slot == null)
                    continue;

                slot.ClearItem();
            }
        }

        private ItemDataSO GetRandomTestItem()
        {
            return testItemPool[Random.Range(0, testItemPool.Count)];
        }

        private int GetRandomStackCount(ItemDataSO itemData)
        {
            if (itemData == null || itemData.maxStackSize <= 1)
                return 1;

            int maxStackCount = Mathf.Min(itemData.maxStackSize, 99);
            return Random.Range(1, maxStackCount + 1);
        }

        private int GetCapacityByBagLevel(int level)
        {
            return level switch
            {
                1 => 10,
                2 => 15,
                3 => 20,
                _ => 10
            };
        }
    }
}

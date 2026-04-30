using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class InventoryUI : MonoBehaviour
    {
        [BoxGroup("루트")]
        [Tooltip("켜고 끌 인벤토리 전체 오브젝트")]
        [SerializeField] private GameObject inventoryRoot;

        [BoxGroup("가방 설정")]
        [Tooltip("현재 가방 레벨")]
        [Range(1, 3)]
        [SerializeField] private int bagLevel = 1;

        [BoxGroup("가방 슬롯")]
        [Tooltip("가방 슬롯 20개를 순서대로 넣으세요.")]
        [SerializeField] private List<InventorySlotUI> bagSlots = new();

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
            int unlockedCount = GetCapacityByBagLevel(bagLevel);

            for (int i = 0; i < bagSlots.Count; i++)
            {
                if (bagSlots[i] == null)
                    continue;

                bool locked = i >= unlockedCount;
                bagSlots[i].SetLocked(locked);
            }
        }
        
        [BoxGroup("아이콘 테스트")]
        [Tooltip("테스트용으로 슬롯에 표시할 임시 아이콘입니다.")]
        [SerializeField] private Sprite testIcon;

        [BoxGroup("아이콘 테스트")]
        [Button("0번 슬롯 아이템 표시")]
        private void TestShowItemAtSlot0()
        {
            if (bagSlots == null || bagSlots.Count == 0 || bagSlots[0] == null)
            {
                Debug.LogWarning("[InventoryUI] 0번 슬롯이 연결되지 않았습니다.");
                return;
            }

            bagSlots[0].SetItem(testIcon, InventoryItemRarity.Rare, 1);
        }

        [BoxGroup("아이콘 테스트")]
        [Button("0번 슬롯 아이템 5개 표시")]
        private void TestShowStackItemAtSlot0()
        {
            if (bagSlots == null || bagSlots.Count == 0 || bagSlots[0] == null)
            {
                Debug.LogWarning("[InventoryUI] 0번 슬롯이 연결되지 않았습니다.");
                return;
            }

            bagSlots[0].SetItem(testIcon, InventoryItemRarity.Rare, 5);
        }

        [BoxGroup("아이콘 테스트")]
        [Button("0번 슬롯 아이템 제거")]
        private void TestClearItemAtSlot0()
        {
            if (bagSlots == null || bagSlots.Count == 0 || bagSlots[0] == null)
            {
                Debug.LogWarning("[InventoryUI] 0번 슬롯이 연결되지 않았습니다.");
                return;
            }

            bagSlots[0].ClearItem();
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
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 4개 파티 슬롯 UI를 관리하는 표시 전용 View입니다.
    /// 네트워크 상태를 직접 알지 않고 표시 데이터와 슬롯 이벤트 중계만 담당합니다.
    /// </summary>
    public class LobbyPartyView : MonoBehaviour
    {
        [Header("==== 슬롯 연결 ====")]
        [Tooltip("PartySlot_1(host)부터 PartySlot_4까지 순서대로 연결")]
        [SerializeField] private LobbyPartySlotUI[] slots = new LobbyPartySlotUI[4];

        public int SlotCount => slots == null ? 0 : slots.Length;

        public event Action<bool> ReadyClicked;
        
        private void OnEnable() => BindSlots();
        private void OnDisable() => UnbindSlots();
        
        /// <summary>
        /// 표시용 슬롯 데이터를 받아 연결된 슬롯 UI를 갱신
        /// </summary>
        public void Render(IReadOnlyList<LobbyPartySlotViewData> slotData)
        {
            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                LobbyPartySlotUI slot = slots[i];

                if (slot == null) continue;

                if (slotData != null && i < slotData.Count)
                {
                    slot.Render(slotData[i]);
                }
                else
                {
                    slot.RenderEmpty();
                }
            }
        }
        
        /// <summary>
        /// 모든 슬롯을 빈 상태로 표시
        /// </summary>
        public void RenderEmpty()
        {
            if (slots == null) return;

            foreach (LobbyPartySlotUI slot in slots)
            {
                if (slot == null) continue;

                slot.RenderEmpty();
            }
        }
        
        private void BindSlots()
        {
            UnbindSlots();

            if (slots == null) return;

            foreach (LobbyPartySlotUI slot in slots)
            {
                if (slot == null) continue;

                slot.ReadyClicked += HandleSlotReadyClicked;
            }
        }

        private void UnbindSlots()
        {
            if (slots == null) return;

            foreach (LobbyPartySlotUI slot in slots)
            {
                if (slot == null) continue;

                slot.ReadyClicked -= HandleSlotReadyClicked;
            }
        }
        
        private void HandleSlotReadyClicked(bool desiredReady) => ReadyClicked?.Invoke(desiredReady);
    }
}
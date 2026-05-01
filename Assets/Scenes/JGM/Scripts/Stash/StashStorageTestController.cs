using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 보관함 크기 변경 이벤트가 실제 보관함 인벤토리에 어떻게 적용될지 확인하는 테스트 수신기입니다.
    /// 실제 Stash UI와 저장형 보관함 인벤토리가 완성되면 이 스크립트는 제거하고 해당 시스템에서 이벤트를 구독합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StashStorageTestController : MonoBehaviour
    {
        [Header("런타임 확인")]
        [SerializeField]
        [Tooltip("이벤트로 수신한 현재 보관함 레벨입니다.")]
        private int currentStashLevel = 1;

        [SerializeField]
        [Tooltip("현재 보관함 가로 칸 수입니다.")]
        private int currentColumns = 8;

        [SerializeField]
        [Tooltip("현재 보관함 세로 칸 수입니다.")]
        private int currentRows = 6;

        [SerializeField]
        [Tooltip("현재 보관함 전체 칸 수입니다.")]
        private int totalSlots = 48;

        [SerializeField]
        [Tooltip("테스트용으로 사용 중인 칸 수입니다.")]
        private int usedSlots;

        [SerializeField]
        [Tooltip("남은 칸 수입니다.")]
        private int freeSlots = 48;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보관함 크기 변경 결과를 Console에 출력할지 여부입니다.")]
        private bool logSizeReceived = true;

        public int CurrentStashLevel => currentStashLevel;
        public int CurrentColumns => currentColumns;
        public int CurrentRows => currentRows;
        public int TotalSlots => totalSlots;
        public int UsedSlots => usedSlots;
        public int FreeSlots => freeSlots;

        private void OnEnable()
        {
            EventBus.Subscribe<StashSizeChangedEvent>(HandleStashSizeChanged);
            RecalculateFreeSlots();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<StashSizeChangedEvent>(HandleStashSizeChanged);
        }

        private void OnValidate()
        {
            if (currentColumns < 1)
                currentColumns = 1;

            if (currentRows < 1)
                currentRows = 1;

            totalSlots = Mathf.Max(1, currentColumns * currentRows);
            usedSlots = Mathf.Clamp(usedSlots, 0, totalSlots);
            RecalculateFreeSlots();
        }

        private void HandleStashSizeChanged(StashSizeChangedEvent evt)
        {
            ApplyStashSize(evt.level, evt.columns, evt.rows, evt.totalSlots);
        }

        private void ApplyStashSize(int level, int columns, int rows, int newTotalSlots)
        {
            currentStashLevel = Mathf.Max(1, level);
            currentColumns = Mathf.Max(1, columns);
            currentRows = Mathf.Max(1, rows);
            totalSlots = Mathf.Max(1, newTotalSlots);

            if (usedSlots > totalSlots)
                usedSlots = totalSlots;

            RecalculateFreeSlots();

            if (!logSizeReceived)
                return;

            Debug.Log(
                $"[StashStorageTestController] 보관함 크기 이벤트 수신\n" +
                $"보관함 레벨: Lv.{currentStashLevel}\n" +
                $"스태쉬 크기: {currentColumns} x {currentRows}\n" +
                $"총 칸 수: {totalSlots}칸\n" +
                $"사용 중인 칸: {usedSlots}칸\n" +
                $"남은 칸: {freeSlots}칸",
                this
            );
        }

        private void RecalculateFreeSlots()
        {
            freeSlots = Mathf.Max(0, totalSlots - usedSlots);
        }

#if UNITY_EDITOR
        [ContextMenu("테스트 아이템 1칸 추가")]
        private void DebugAddOneSlot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashStorageTestController] Play Mode에서만 테스트할 수 있습니다.", this);
                return;
            }

            if (usedSlots >= totalSlots)
            {
                Debug.LogWarning("[StashStorageTestController] 보관함에 남은 칸이 없습니다.", this);
                return;
            }

            usedSlots += 1;
            RecalculateFreeSlots();
            Debug.Log($"[StashStorageTestController] 테스트 아이템 추가: 사용 {usedSlots} / 전체 {totalSlots}", this);
        }

        [ContextMenu("테스트 보관함 비우기")]
        private void DebugClearStorage()
        {
            usedSlots = 0;
            RecalculateFreeSlots();
            Debug.Log("[StashStorageTestController] 테스트 보관함을 비웠습니다.", this);
        }

        [ContextMenu("현재 보관함 상태 출력")]
        private void DebugPrintStorageState()
        {
            Debug.Log(
                $"[StashStorageTestController] 현재 보관함 상태\n" +
                $"레벨: Lv.{currentStashLevel}\n" +
                $"크기: {currentColumns} x {currentRows}\n" +
                $"사용: {usedSlots} / {totalSlots}\n" +
                $"남은 칸: {freeSlots}",
                this
            );
        }
#endif
    }
}

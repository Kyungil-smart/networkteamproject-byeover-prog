using System;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 보관함 레벨에 따른 스태쉬 크기를 계산하고 이벤트
    // 실제 보관함 UI나 플레이어 보관 인벤토리를 직접 참조하지 않고, 추후 연동은 EventBus 구독으로 처리
    [DisallowMultipleComponent]
    public sealed class StashSizeController : MonoBehaviour
    {
        [Serializable]
        private struct StashSizeRule
        {
            [Min(1)] public int level;
            [Min(1)] public int columns;
            [Min(1)] public int rows;

            public int TotalSlots => Mathf.Max(1, columns) * Mathf.Max(1, rows);
        }

        [Header("보관함 시설")]
        [SerializeField]
        [Tooltip("스태쉬 크기를 계산할 보관함 시설입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private FacilityBase stashFacility;

        [Header("레벨별 스태쉬 크기")]
        [SerializeField]
        [Tooltip("레벨별 보관함 그리드 크기입니다. 기획 기준은 Lv1 8x6, Lv2 10x8, Lv3 12x9, Lv4 14x10입니다.")]
        private StashSizeRule[] sizeRules =
        {
            new StashSizeRule { level = 1, columns = 8, rows = 6 },
            new StashSizeRule { level = 2, columns = 10, rows = 7 },
            new StashSizeRule { level = 3, columns = 10, rows = 9 },
            new StashSizeRule { level = 4, columns = 11, rows = 10 },
            new StashSizeRule { level = 5, columns = 15, rows = 10 },
            new StashSizeRule { level = 6, columns = 20, rows = 10 },
        };

        [Header("오프라인 테스트")]
        [SerializeField]
        [Tooltip("NetworkVariable을 직접 변경하지 않고 테스트용 레벨로 보관함 크기를 계산할지 여부입니다.")]
        private bool useOfflineTestLevel;

        [SerializeField]
        [Range(1, 6)]
        [Tooltip("오프라인 테스트에서 사용할 보관함 레벨입니다.")]
        private int offlineTestLevel = 1;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("현재 보관함 레벨입니다. 런타임 확인용입니다.")]
        private int currentStashLevel = 1;

        [SerializeField]
        [Tooltip("현재 보관함 가로 칸 수입니다. 런타임 확인용입니다.")]
        private int currentColumns = 8;

        [SerializeField]
        [Tooltip("현재 보관함 세로 칸 수입니다. 런타임 확인용입니다.")]
        private int currentRows = 6;

        [SerializeField]
        [Tooltip("현재 보관함 전체 칸 수입니다. 런타임 확인용입니다.")]
        private int currentTotalSlots = 48;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보관함 크기가 변경될 때 Console 로그를 출력할지 여부입니다.")]
        private bool logSizeChanged = true;

        public int CurrentStashLevel => currentStashLevel;
        public int CurrentColumns => currentColumns;
        public int CurrentRows => currentRows;
        public int CurrentTotalSlots => currentTotalSlots;

        public event Action<int, int, int, int> OnStashSizeChanged;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnEnable()
        {
            SubscribeFacilityLevelChanged();
            RefreshSize(true);
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
        }

        private void OnValidate()
        {
            ValidateSizeRules();
            offlineTestLevel = Mathf.Clamp(offlineTestLevel, 1, 6);
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (stashFacility == null)
                stashFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (stashFacility == null)
                return;

            stashFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            stashFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (stashFacility == null)
                return;

            stashFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void HandleFacilityLevelChanged(int previousLevel, int newLevel)
        {
            RefreshSize(false);
        }

        public void RefreshSize()
        {
            RefreshSize(false);
        }

        public void RefreshSize(bool forceNotify)
        {
            if (!IsValidStashFacility())
                return;

            int nextLevel = GetCurrentStashLevel();
            StashSizeRule nextRule = GetSizeRule(nextLevel);

            bool changed = currentStashLevel != nextLevel
                           || currentColumns != nextRule.columns
                           || currentRows != nextRule.rows
                           || currentTotalSlots != nextRule.TotalSlots;

            currentStashLevel = nextLevel;
            currentColumns = Mathf.Max(1, nextRule.columns);
            currentRows = Mathf.Max(1, nextRule.rows);
            currentTotalSlots = currentColumns * currentRows;

            if (!changed && !forceNotify)
                return;

            OnStashSizeChanged?.Invoke(currentStashLevel, currentColumns, currentRows, currentTotalSlots);

            EventBus.Publish(new StashSizeChangedEvent
            {
                level = currentStashLevel,
                columns = currentColumns,
                rows = currentRows,
                totalSlots = currentTotalSlots,
            });

            if (logSizeChanged)
            {
                Debug.Log(
                    $"[StashSizeController] 보관함 Lv.{currentStashLevel} 크기 적용\n" +
                    $"스태쉬 크기: {currentColumns} x {currentRows}\n" +
                    $"총 칸 수: {currentTotalSlots}칸",
                    this
                );
            }
        }

        public int GetTotalSlots()
        {
            return currentTotalSlots;
        }

        public int GetTotalSlotsForLevel(int stashLevel)
        {
            return GetSizeRule(stashLevel).TotalSlots;
        }

        public void SetOfflineTestLevel(int level)
        {
            useOfflineTestLevel = true;
            offlineTestLevel = Mathf.Clamp(level, 1, 6);
            RefreshSize(true);
        }

        public void ClearOfflineTestLevel()
        {
            useOfflineTestLevel = false;
            RefreshSize(true);
        }

        private int GetCurrentStashLevel()
        {
            if (useOfflineTestLevel)
                return Mathf.Clamp(offlineTestLevel, 1, 6);

            if (stashFacility == null)
                return 1;

            return Mathf.Max(1, stashFacility.CurrentLevel.Value);
        }

        private StashSizeRule GetSizeRule(int level)
        {
            ValidateSizeRules();

            int safeLevel = Mathf.Max(1, level);
            StashSizeRule fallback = sizeRules[0];

            for (int i = 0; i < sizeRules.Length; i++)
            {
                if (sizeRules[i].level == safeLevel)
                    return sizeRules[i];

                if (sizeRules[i].level < safeLevel)
                    fallback = sizeRules[i];
            }

            return fallback;
        }

        private void ValidateSizeRules()
        {
            if (sizeRules == null || sizeRules.Length == 0)
            {
                sizeRules = new[]
                {
                    new StashSizeRule { level = 1, columns = 8, rows = 6 },
                    new StashSizeRule { level = 2, columns = 10, rows = 7 },
                    new StashSizeRule { level = 3, columns = 10, rows = 9 },
                    new StashSizeRule { level = 4, columns = 11, rows = 10 },
                    new StashSizeRule { level = 5, columns = 15, rows = 10 },
                    new StashSizeRule { level = 6, columns = 20, rows = 10 },
                };
            }

            for (int i = 0; i < sizeRules.Length; i++)
            {
                if (sizeRules[i].level < 1)
                    sizeRules[i].level = i + 1;

                if (sizeRules[i].columns < 1)
                    sizeRules[i].columns = 1;

                if (sizeRules[i].rows < 1)
                    sizeRules[i].rows = 1;
            }
        }

        private bool IsValidStashFacility()
        {
            if (stashFacility == null)
            {
                Debug.LogWarning("[StashSizeController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (stashFacility.Type != FacilityType.Stash)
            {
                Debug.LogWarning($"[StashSizeController] 연결된 시설 타입이 Stash가 아닙니다. 현재 타입: {stashFacility.Type}", this);
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("보관함 크기 다시 계산")]
        private void DebugRefreshSize()
        {
            RefreshSize(true);
        }

        [ContextMenu("오프라인 테스트 레벨 해제")]
        private void DebugClearOfflineTestLevel()
        {
            ClearOfflineTestLevel();
        }
#endif
    }
}

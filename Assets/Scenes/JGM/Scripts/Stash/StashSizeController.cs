using System;
using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 보관함 시설 레벨에 따라 보관함 크기를 계산합니다.
    /// UI, 플레이어 인벤토리, 파밍 아이템은 직접 참조하지 않고 현재 크기 정보만 제공합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public class StashSizeController : MonoBehaviour
    {
        [Header("보관함 시설")]
        [SerializeField]
        [Tooltip("보관함 레벨을 읽을 시설입니다. 비워두면 같은 오브젝트의 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase stashFacility;

        [Header("레벨별 보관함 크기")]
        [SerializeField]
        [Min(1)]
        [Tooltip("Lv1 보관함 가로 칸 수입니다.")]
        private int level1Width = 8;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv1 보관함 세로 칸 수입니다.")]
        private int level1Height = 6;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv2 보관함 가로 칸 수입니다.")]
        private int level2Width = 10;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv2 보관함 세로 칸 수입니다.")]
        private int level2Height = 8;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv3 보관함 가로 칸 수입니다.")]
        private int level3Width = 12;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv3 보관함 세로 칸 수입니다.")]
        private int level3Height = 9;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv4 보관함 가로 칸 수입니다.")]
        private int level4Width = 14;

        [SerializeField]
        [Min(1)]
        [Tooltip("Lv4 보관함 세로 칸 수입니다.")]
        private int level4Height = 10;

        [Header("현재 보관함 크기")]
        [SerializeField]
        [Tooltip("현재 보관함 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentStashLevel = 1;

        [SerializeField]
        [Tooltip("현재 보관함 가로 칸 수입니다. 런타임 확인용 값입니다.")]
        private int currentWidth;

        [SerializeField]
        [Tooltip("현재 보관함 세로 칸 수입니다. 런타임 확인용 값입니다.")]
        private int currentHeight;

        [SerializeField]
        [Tooltip("현재 보관함 전체 칸 수입니다. 런타임 확인용 값입니다.")]
        private int currentTotalSlotCount;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보관함 크기 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logSizeChanged = true;

        public int CurrentStashLevel => currentStashLevel;
        public int CurrentWidth => currentWidth;
        public int CurrentHeight => currentHeight;
        public int CurrentTotalSlotCount => currentTotalSlotCount;

        public event Action<int, int, int, int> OnStashSizeChanged;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            RefreshSize();
        }

        private void OnEnable()
        {
            SubscribeFacilityLevelChanged();
            RefreshSize();
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
        }

        private void OnValidate()
        {
            ClampSizeValues();
            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                currentStashLevel = 1;
                currentWidth = level1Width;
                currentHeight = level1Height;
                currentTotalSlotCount = currentWidth * currentHeight;
            }
        }

        private void FindRequiredComponents()
        {
            if (stashFacility == null)
                stashFacility = GetComponent<FacilityBase>();
        }

        private void ClampSizeValues()
        {
            level1Width = Mathf.Max(1, level1Width);
            level1Height = Mathf.Max(1, level1Height);

            level2Width = Mathf.Max(1, level2Width);
            level2Height = Mathf.Max(1, level2Height);

            level3Width = Mathf.Max(1, level3Width);
            level3Height = Mathf.Max(1, level3Height);

            level4Width = Mathf.Max(1, level4Width);
            level4Height = Mathf.Max(1, level4Height);
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
            RefreshSize();
        }

        public void RefreshSize()
        {
            if (stashFacility == null)
            {
                Debug.LogWarning("[StashSizeController] FacilityBase가 연결되어 있지 않습니다.", this);
                return;
            }

            int previousWidth = currentWidth;
            int previousHeight = currentHeight;
            int previousTotalSlotCount = currentTotalSlotCount;

            currentStashLevel = Mathf.Clamp(stashFacility.CurrentLevel.Value, 1, 4);

            GetSizeByLevel(currentStashLevel, out currentWidth, out currentHeight);
            currentTotalSlotCount = currentWidth * currentHeight;

            bool changed =
                previousWidth != currentWidth ||
                previousHeight != currentHeight ||
                previousTotalSlotCount != currentTotalSlotCount;

            if (!changed)
                return;

            OnStashSizeChanged?.Invoke(
                currentStashLevel,
                currentWidth,
                currentHeight,
                currentTotalSlotCount
            );

            if (logSizeChanged)
            {
                Debug.Log(
                    $"[StashSizeController] 보관함 Lv.{currentStashLevel} / 크기 {currentWidth} x {currentHeight} / 총 {currentTotalSlotCount}칸",
                    this
                );
            }
        }

        public bool IsInsideGrid(int x, int y)
        {
            RefreshSize();

            if (x < 0)
                return false;

            if (y < 0)
                return false;

            if (x >= currentWidth)
                return false;

            if (y >= currentHeight)
                return false;

            return true;
        }

        public void GetCurrentSize(out int width, out int height, out int totalSlotCount)
        {
            RefreshSize();

            width = currentWidth;
            height = currentHeight;
            totalSlotCount = currentTotalSlotCount;
        }

        private void GetSizeByLevel(int level, out int width, out int height)
        {
            switch (level)
            {
                case 1:
                    width = level1Width;
                    height = level1Height;
                    break;

                case 2:
                    width = level2Width;
                    height = level2Height;
                    break;

                case 3:
                    width = level3Width;
                    height = level3Height;
                    break;

                case 4:
                    width = level4Width;
                    height = level4Height;
                    break;

                default:
                    width = level1Width;
                    height = level1Height;
                    break;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("보관함 크기 다시 계산")]
        private void DebugRefreshSize()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashSizeController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            RefreshSize();
        }

        [ContextMenu("보관함 현재 크기 출력")]
        private void DebugPrintCurrentSize()
        {
            RefreshSize();

            Debug.Log(
                $"[StashSizeController] 현재 보관함 Lv.{currentStashLevel} / {currentWidth} x {currentHeight} / 총 {currentTotalSlotCount}칸",
                this
            );
        }
#endif
    }
}
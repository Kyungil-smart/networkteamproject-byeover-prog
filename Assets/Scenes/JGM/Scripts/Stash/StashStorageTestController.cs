using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 보관함 크기와 슬롯 좌표 검사를 확인하는 테스트용 컨트롤러입니다.
    /// 실제 아이템 저장, UI 표시, 플레이어 인벤토리 연동은 담당하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StashSizeController))]
    public class StashStorageTestController : MonoBehaviour
    {
        [Header("보관함 크기")]
        [SerializeField]
        [Tooltip("보관함 크기를 계산하는 컨트롤러입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private StashSizeController stashSizeController;

        [Header("좌표 테스트")]
        [SerializeField]
        [Min(0)]
        [Tooltip("검사할 보관함 X 좌표입니다.")]
        private int testX;

        [SerializeField]
        [Min(0)]
        [Tooltip("검사할 보관함 Y 좌표입니다.")]
        private int testY;

        [Header("아이템 크기 테스트")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트할 아이템의 가로 칸 수입니다.")]
        private int testItemWidth = 1;

        [SerializeField]
        [Min(1)]
        [Tooltip("테스트할 아이템의 세로 칸 수입니다.")]
        private int testItemHeight = 1;

        [Header("현재 보관함 상태")]
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

        [Header("최근 테스트 결과")]
        [SerializeField]
        [Tooltip("최근 좌표 검사 결과입니다.")]
        private bool lastCoordinateInside;

        [SerializeField]
        [Tooltip("최근 아이템 크기 검사 결과입니다.")]
        private bool lastItemAreaInside;

        [Header("로그")]
        [SerializeField]
        [Tooltip("테스트 결과를 Console에 출력할지 여부입니다.")]
        private bool logTestResult = true;

        public bool LastCoordinateInside => lastCoordinateInside;
        public bool LastItemAreaInside => lastItemAreaInside;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            RefreshSnapshot();
        }

        private void OnEnable()
        {
            SubscribeStashSizeChanged();
            RefreshSnapshot();
        }

        private void OnDisable()
        {
            UnsubscribeStashSizeChanged();
        }

        private void OnValidate()
        {
            if (testX < 0)
                testX = 0;

            if (testY < 0)
                testY = 0;

            if (testItemWidth < 1)
                testItemWidth = 1;

            if (testItemHeight < 1)
                testItemHeight = 1;

            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (stashSizeController == null)
                stashSizeController = GetComponent<StashSizeController>();
        }

        private void SubscribeStashSizeChanged()
        {
            if (stashSizeController == null)
                return;

            stashSizeController.OnStashSizeChanged -= HandleStashSizeChanged;
            stashSizeController.OnStashSizeChanged += HandleStashSizeChanged;
        }

        private void UnsubscribeStashSizeChanged()
        {
            if (stashSizeController == null)
                return;

            stashSizeController.OnStashSizeChanged -= HandleStashSizeChanged;
        }

        private void HandleStashSizeChanged(int level, int width, int height, int totalSlotCount)
        {
            RefreshSnapshot();

            if (logTestResult)
            {
                Debug.Log(
                    $"[StashStorageTestController] 보관함 크기 변경 감지: Lv.{level} / {width} x {height} / 총 {totalSlotCount}칸",
                    this
                );
            }
        }

        public void RefreshSnapshot()
        {
            if (stashSizeController == null)
            {
                Debug.LogWarning("[StashStorageTestController] StashSizeController가 연결되어 있지 않습니다.", this);
                return;
            }

            stashSizeController.RefreshSize();

            currentStashLevel = stashSizeController.CurrentStashLevel;
            currentWidth = stashSizeController.CurrentWidth;
            currentHeight = stashSizeController.CurrentHeight;
            currentTotalSlotCount = stashSizeController.CurrentTotalSlotCount;
        }

        public bool CheckCoordinateInside(int x, int y)
        {
            RefreshSnapshot();

            lastCoordinateInside = stashSizeController.IsInsideGrid(x, y);

            if (logTestResult)
            {
                Debug.Log(
                    $"[StashStorageTestController] 좌표 검사: ({x}, {y}) / 결과: {lastCoordinateInside}",
                    this
                );
            }

            return lastCoordinateInside;
        }

        public bool CheckItemAreaInside(int startX, int startY, int itemWidth, int itemHeight)
        {
            RefreshSnapshot();

            itemWidth = Mathf.Max(1, itemWidth);
            itemHeight = Mathf.Max(1, itemHeight);

            int endX = startX + itemWidth - 1;
            int endY = startY + itemHeight - 1;

            bool startInside = stashSizeController.IsInsideGrid(startX, startY);
            bool endInside = stashSizeController.IsInsideGrid(endX, endY);

            lastItemAreaInside = startInside && endInside;

            if (logTestResult)
            {
                Debug.Log(
                    $"[StashStorageTestController] 아이템 영역 검사: 시작({startX}, {startY}) / 크기 {itemWidth} x {itemHeight} / 끝({endX}, {endY}) / 결과: {lastItemAreaInside}",
                    this
                );
            }

            return lastItemAreaInside;
        }

#if UNITY_EDITOR
        [ContextMenu("현재 보관함 상태 출력")]
        private void DebugPrintStashState()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashStorageTestController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            RefreshSnapshot();

            Debug.Log(
                $"[StashStorageTestController] 현재 보관함 Lv.{currentStashLevel} / {currentWidth} x {currentHeight} / 총 {currentTotalSlotCount}칸",
                this
            );
        }

        [ContextMenu("좌표 테스트 실행")]
        private void DebugCheckCoordinate()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashStorageTestController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            CheckCoordinateInside(testX, testY);
        }

        [ContextMenu("아이템 크기 테스트 실행")]
        private void DebugCheckItemArea()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StashStorageTestController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            CheckItemAreaInside(testX, testY, testItemWidth, testItemHeight);
        }
#endif
    }
}
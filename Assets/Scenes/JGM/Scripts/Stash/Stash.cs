using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// 보관함의 실제 저장 공간 크기를 관리한다.
    /// StashFacility는 시설 레벨만 관리하고,
    /// Stash는 전달받은 레벨에 따라 보관함 크기를 적용한다.
    /// </summary>
    public sealed class Stash : MonoBehaviour
    {
        private const int MinStashLevel = 1;
        private const int MaxStashLevel = 4;

        [Header("보관함 크기")]
        [SerializeField]
        [Tooltip("현재 보관함 가로 칸 수입니다.")]
        private int gridWidth = 8;

        [SerializeField]
        [Tooltip("현재 보관함 세로 칸 수입니다.")]
        private int gridHeight = 6;

        [Header("디버그")]
        [SerializeField]
        [Tooltip("보관함 크기 변경 로그를 출력할지 여부입니다.")]
        private bool logSizeChanged = true;

        public int GridWidth => gridWidth;
        public int GridHeight => gridHeight;
        public int TotalSlotCount => gridWidth * gridHeight;

        public void ApplyFacilityLevel(int facilityLevel)
        {
            int level = Mathf.Clamp(facilityLevel, MinStashLevel, MaxStashLevel);

            switch (level)
            {
                case 1:
                    SetGridSize(8, 6);
                    break;

                case 2:
                    SetGridSize(10, 8);
                    break;

                case 3:
                    SetGridSize(12, 9);
                    break;

                case 4:
                    SetGridSize(14, 10);
                    break;
            }
        }

        public Vector2Int GetGridSize()
        {
            return new Vector2Int(gridWidth, gridHeight);
        }

        public bool IsInsideGrid(int x, int y)
        {
            return x >= 0 &&
                   y >= 0 &&
                   x < gridWidth &&
                   y < gridHeight;
        }

        private void SetGridSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning("[Stash] 보관함 크기는 0 이하가 될 수 없습니다.", this);
                return;
            }

            gridWidth = width;
            gridHeight = height;

            if (!logSizeChanged)
                return;

            Debug.Log($"[Stash] 보관함 크기 적용: {gridWidth} x {gridHeight} / 총 {TotalSlotCount}칸", this);
        }
    }
}
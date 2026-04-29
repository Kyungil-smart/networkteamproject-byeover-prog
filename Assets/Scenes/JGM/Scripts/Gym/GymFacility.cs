using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// 실제 업그레이드 검증과 재료 소모는 FacilityBase가 담당하고,
    /// 이 클래스는 헬스장 레벨 변경 감지와 테스트 업그레이드 연결만 담당한다.
    /// </summary>
    public sealed class GymFacility : FacilityBase
    {
        [Header("헬스장 로그")]
        [SerializeField]
        [Tooltip("헬스장 레벨이 변경될 때 Console에 로그를 출력할지 여부입니다.")]
        private bool logLevelChanged = true;

        protected override void OnLevelChanged(int newLevel)
        {
            if (!logLevelChanged)
                return;

            Debug.Log($"[GymFacility] 헬스장 레벨 변경: Lv.{newLevel}", this);
        }

        public bool TryUpgradeForTest(IInventory inventory)
        {
            return TryUpgradeWithInventory(inventory);
        }
    }
}
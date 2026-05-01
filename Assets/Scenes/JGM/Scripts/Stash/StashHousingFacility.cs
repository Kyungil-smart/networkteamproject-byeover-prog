using UnityEngine;

using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 보관함 시설 본체입니다.
    /// 공통 레벨 상태와 업그레이드 이벤트는 FacilityBase가 처리하고,
    /// 이 클래스는 보관함 시설임을 구분하는 최소 책임만 가집니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StashHousingFacility : FacilityBase
    {
        [Header("보관함 로그")]
        [SerializeField]
        [Tooltip("보관함 레벨이 바뀔 때 Console 로그를 출력할지 여부입니다.")]
        private bool logLevelChanged = true;

        protected override void OnLevelChanged(int newLevel)
        {
            if (!logLevelChanged)
                return;

            Debug.Log($"[StashHousingFacility] 보관함 레벨 적용: Lv.{newLevel}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("현재 보관함 레벨 출력")]
        private void DebugPrintCurrentLevel()
        {
            Debug.Log($"[StashHousingFacility] 현재 보관함 레벨: Lv.{CurrentLevel.Value}", this);
        }
#endif
    }
}

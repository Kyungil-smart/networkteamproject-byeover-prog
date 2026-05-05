using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 침대 시설 본체입니다.
    /// 공통 레벨 상태와 업그레이드 기본 흐름은 FacilityBase가 담당하고,
    /// 침대 고유 효과는 BedStaminaBonusController가 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BedRoomFacility : FacilityBase
    {
        [Header("로그")]
        [SerializeField]
        [Tooltip("침대 레벨이 변경될 때 Console 로그를 출력할지 여부입니다.")]
        private bool logLevelChanged = true;

        protected override void OnLevelChanged(int newLevel)
        {
            if (!logLevelChanged)
                return;

            Debug.Log($"[BedRoomFacility] 침대 레벨 변경: Lv.{newLevel}", this);
        }
    }
}

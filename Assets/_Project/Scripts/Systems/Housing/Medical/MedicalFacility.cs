using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 의료시설 본체입
    // 시설 레벨 상태는 FacilityBase의 NetworkVariable을 사용하고, 실제 최대 체력 보너스 계산은 MedicalHealthBonusController가 담당
    [DisallowMultipleComponent]
    public class MedicalFacility : FacilityBase
    {
        protected override void OnLevelChanged(int newLevel)
        {
            Debug.Log($"[MedicalFacility] 의료시설 레벨 변경 감지: Lv.{newLevel}", this);
        }
    }
}

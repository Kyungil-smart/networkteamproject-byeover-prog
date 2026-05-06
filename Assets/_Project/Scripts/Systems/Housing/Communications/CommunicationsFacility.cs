using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 통신장비 시설 본체
    // 공통 레벨 상태는 FacilityBase가 관리하고, 이 클래스는 통신장비 시설임을 명확히 분리
    [DisallowMultipleComponent]
    public class CommunicationsFacility : FacilityBase
    {
        [Header("통신장비 로그")]
        [SerializeField]
        [Tooltip("통신장비 레벨 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logLevelChanged = true;

        protected override void OnLevelChanged(int newLevel)
        {
            if (!logLevelChanged)
                return;

            if (Type != FacilityType.CommStation)
            {
                Debug.LogWarning(
                    $"[CommunicationsFacility] FacilityDataSO 타입이 CommStation이 아닙니다. 현재 타입: {Type}",
                    this
                );
                return;
            }

            Debug.Log($"[CommunicationsFacility] 통신장비 레벨 변경 감지: Lv.{newLevel}", this);
        }
    }
}

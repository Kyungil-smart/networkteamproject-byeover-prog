using Unity.Collections;

namespace DeadZone.Core
{
    /// <summary>
    /// 의료시설 레벨에 따른 최대 체력 보너스 변경 이벤트입니다.
    /// 추후 PlayerStats, HUD, 저장 시스템이 이 이벤트를 구독해서 실제 반영합니다.
    /// </summary>
    public struct MedicalHealthBonusChangedEvent : IGameEvent
    {
        public FacilityType facilityType;
        public int level;
        public int maxHealthBonus;
        public FixedString64Bytes source;
    }
}

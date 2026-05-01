using Unity.Collections;

namespace DeadZone.Core
{
    /// <summary>
    /// 통신장비 보너스가 변경되었을 때 다른 시스템이 느슨하게 받을 수 있는 이벤트입니다.
    /// Player, UI, Trader는 이 이벤트를 구독해서 각자 필요한 값만 반영합니다.
    /// </summary>
    public struct CommunicationsBonusChangedEvent : IGameEvent
    {
        public int level;
        public FixedString64Bytes unlockedQuestStartId;
        public FixedString64Bytes unlockedQuestEndId;
        public int experienceBonusPercent;
        public int detectionResistancePercent;
        public int traderDiscountPercent;
    }
}

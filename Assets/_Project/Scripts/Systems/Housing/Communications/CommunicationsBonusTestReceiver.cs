using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    // 통신장비 보너스 EventBus 발행이 정상 동작하는지 확인하는 테스트 수신기
    // 실제 UI, PlayerStats, TraderManager가 완성되면 해당 시스템들이 같은 이벤트를 구독
    [DisallowMultipleComponent]
    public class CommunicationsBonusTestReceiver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("통신장비 보너스 이벤트 수신 로그를 Console에 출력할지 여부입니다.")]
        private bool logReceivedEvent = true;

        private void OnEnable()
        {
            EventBus.Subscribe<CommunicationsBonusChangedEvent>(HandleCommunicationsBonusChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CommunicationsBonusChangedEvent>(HandleCommunicationsBonusChanged);
        }

        private void HandleCommunicationsBonusChanged(CommunicationsBonusChangedEvent e)
        {
            if (!logReceivedEvent)
                return;

            Debug.Log(
                $"[CommunicationsBonusTestReceiver] 통신장비 보너스 이벤트 수신\n" +
                $"Lv.{e.level}\n" +
                $"해금 퀘스트: {e.unlockedQuestStartId}~{e.unlockedQuestEndId}\n" +
                $"경험치 +{e.experienceBonusPercent}% / 감지 저항 +{e.detectionResistancePercent}% / 트레이더 할인 {e.traderDiscountPercent}%",
                this
            );
        }
    }
}

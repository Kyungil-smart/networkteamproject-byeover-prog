using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    // 의료시설 보너스 이벤트 테스트 수신기
    // 실제 PlayerStats와 UI가 완성되기 전까지 Console 로그로 이벤트 흐름을 확인
    [DisallowMultipleComponent]
    public class MedicalHealthBonusTestReceiver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("의료시설 보너스 이벤트 수신 로그를 출력할지 여부입니다.")]
        private bool logReceivedEvent = true;

        private void OnEnable()
        {
            EventBus.Subscribe<MedicalHealthBonusChangedEvent>(HandleMedicalBonusChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MedicalHealthBonusChangedEvent>(HandleMedicalBonusChanged);
        }

        private void HandleMedicalBonusChanged(MedicalHealthBonusChangedEvent evt)
        {
            if (!logReceivedEvent)
                return;

            Debug.Log(
                $"[MedicalHealthBonusTestReceiver] 의료시설 보너스 이벤트 수신: Lv.{evt.level}, 최대 체력 +{evt.maxHealthBonus}",
                this
            );
        }
    }
}

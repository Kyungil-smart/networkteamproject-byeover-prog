using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    // 침대 최대 스태미너 보너스 이벤트가 정상 발행되는지 확인하는 테스트 수신기
    // 실제 PlayerStats와 UI가 완성되면 이 스크립트는 제거하거나 비활성화
    [DisallowMultipleComponent]
    public sealed class BedStaminaBonusTestReceiver : MonoBehaviour
    {
        [Header("테스트 스태미너")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트용 기본 최대 스태미너입니다.")]
        private int baseMaxStamina = 100;

        [SerializeField]
        [Tooltip("최대 스태미너가 바뀔 때 현재 스태미너도 최대치로 채울지 여부입니다.")]
        private bool fillStaminaWhenMaxStaminaChanged = true;

        [Header("런타임 상태")]
        [SerializeField]
        [Tooltip("침대 보너스가 반영된 테스트용 최대 스태미너입니다.")]
        private int currentMaxStamina = 100;

        [SerializeField]
        [Tooltip("테스트용 현재 스태미너입니다.")]
        private int currentStamina = 100;

        [Header("로그")]
        [SerializeField]
        [Tooltip("이벤트 수신 로그를 Console에 출력할지 여부입니다.")]
        private bool logBonusReceived = true;

        public int CurrentMaxStamina => currentMaxStamina;
        public int CurrentStamina => currentStamina;

        private void OnEnable()
        {
            EventBus.Subscribe<BedStaminaBonusChangedEvent>(HandleBedStaminaBonusChanged);
            ApplyBonus(1, 0);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BedStaminaBonusChangedEvent>(HandleBedStaminaBonusChanged);
        }

        private void OnValidate()
        {
            if (baseMaxStamina < 1)
                baseMaxStamina = 1;

            if (!Application.isPlaying)
            {
                currentMaxStamina = baseMaxStamina;
                currentStamina = baseMaxStamina;
            }
        }

        private void HandleBedStaminaBonusChanged(BedStaminaBonusChangedEvent evt)
        {
            ApplyBonus(evt.level, evt.maxStaminaBonus);
        }

        private void ApplyBonus(int level, int maxStaminaBonus)
        {
            currentMaxStamina = baseMaxStamina + Mathf.Max(0, maxStaminaBonus);

            if (fillStaminaWhenMaxStaminaChanged)
                currentStamina = currentMaxStamina;
            else
                currentStamina = Mathf.Clamp(currentStamina, 0, currentMaxStamina);

            if (!logBonusReceived)
                return;

            Debug.Log(
                $"[BedStaminaBonusTestReceiver] 침대 보너스 이벤트 수신\n" +
                $"침대 레벨: Lv.{level}\n" +
                $"최대 스태미너 보너스: +{maxStaminaBonus}\n" +
                $"테스트 최종 최대 스태미너: {currentMaxStamina}\n" +
                $"테스트 현재 스태미너: {currentStamina}",
                this
            );
        }
    }
}

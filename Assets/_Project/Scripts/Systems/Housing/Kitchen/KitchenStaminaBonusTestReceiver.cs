using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    // 주방 최대 스태미너 보너스가 추후 플레이어 스탯에 어떻게 적용될지 확인하는 테스트 수신기
    // 실제 PlayerStats와 UI가 완성되면 이 스크립트는 제거하고 해당 시스템에서 이벤트를 구독

    [DisallowMultipleComponent]
    public sealed class KitchenStaminaBonusTestReceiver : MonoBehaviour
    {
        [Header("테스트 기준값")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트용 기본 최대 스태미너입니다. 기획 기준 기본값은 100입니다.")]
        private int baseMaxStamina = 100;

        [SerializeField]
        [Tooltip("최대 스태미너가 변경될 때 현재 스태미너를 최대치로 채울지 여부입니다.")]
        private bool fillStaminaWhenMaxStaminaChanged = true;

        [Header("런타임 확인")]
        [SerializeField]
        [Tooltip("이벤트로 수신한 현재 주방 레벨입니다.")]
        private int currentKitchenLevel = 1;

        [SerializeField]
        [Tooltip("현재 적용된 주방 최대 스태미너 보너스입니다.")]
        private int currentStaminaBonus;

        [SerializeField]
        [Tooltip("기본 최대 스태미너와 보너스를 더한 최종 최대 스태미너입니다.")]
        private int currentMaxStamina = 100;

        [SerializeField]
        [Tooltip("테스트용 현재 스태미너입니다.")]
        private int currentStamina = 100;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스 적용 결과를 Console에 출력할지 여부입니다.")]
        private bool logBonusReceived = true;

        public int BaseMaxStamina => baseMaxStamina;
        public int CurrentKitchenLevel => currentKitchenLevel;
        public int CurrentStaminaBonus => currentStaminaBonus;
        public int CurrentMaxStamina => currentMaxStamina;
        public int CurrentStamina => currentStamina;

        private void OnEnable()
        {
            EventBus.Subscribe<KitchenStaminaBonusChangedEvent>(HandleStaminaBonusChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<KitchenStaminaBonusChangedEvent>(HandleStaminaBonusChanged);
        }

        private void OnValidate()
        {
            if (baseMaxStamina < 1)
                baseMaxStamina = 1;

            if (!Application.isPlaying)
            {
                currentKitchenLevel = 1;
                currentStaminaBonus = 0;
                currentMaxStamina = baseMaxStamina;
                currentStamina = baseMaxStamina;
            }
        }

        private void HandleStaminaBonusChanged(KitchenStaminaBonusChangedEvent evt)
        {
            ApplyStaminaBonus(evt.level, evt.maxStaminaBonus);
        }

        private void ApplyStaminaBonus(int kitchenLevel, int maxStaminaBonus)
        {
            currentKitchenLevel = Mathf.Max(1, kitchenLevel);
            currentStaminaBonus = Mathf.Max(0, maxStaminaBonus);
            currentMaxStamina = baseMaxStamina + currentStaminaBonus;

            if (fillStaminaWhenMaxStaminaChanged)
                currentStamina = currentMaxStamina;
            else
                currentStamina = Mathf.Clamp(currentStamina, 0, currentMaxStamina);

            if (!logBonusReceived)
                return;

            Debug.Log(
                $"[KitchenStaminaBonusTestReceiver] 주방 보너스 이벤트 수신\n" +
                $"주방 레벨: Lv.{currentKitchenLevel}\n" +
                $"기본 최대 스태미너: {baseMaxStamina}\n" +
                $"보너스: +{currentStaminaBonus}\n" +
                $"최종 최대 스태미너: {currentMaxStamina}\n" +
                $"현재 스태미너: {currentStamina}",
                this
            );
        }
    }
}

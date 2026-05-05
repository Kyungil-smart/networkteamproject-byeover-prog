using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 헬스장 소지 무게 보너스가 추후 플레이어 스탯에 어떻게 적용될지 확인하는 테스트 수신기입니다.
    /// 실제 PlayerStats와 Inventory가 완성되면 이 스크립트는 제거하고 해당 시스템에서 이벤트를 구독합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GymCarryWeightBonusTestReceiver : MonoBehaviour
    {
        [Header("테스트 기준값")]
        [SerializeField]
        [Min(1f)]
        [Tooltip("테스트용 기본 소지 무게입니다. 기획 기준 기본값은 60kg입니다.")]
        private float baseCarryWeightKg = 60f;

        [Header("런타임 확인")]
        [SerializeField]
        [Tooltip("이벤트로 수신한 현재 헬스장 레벨입니다.")]
        private int currentGymLevel = 1;

        [SerializeField]
        [Tooltip("현재 적용된 헬스장 소지 무게 보너스입니다.")]
        private float currentCarryWeightBonusKg;

        [SerializeField]
        [Tooltip("기본 소지 무게와 보너스를 더한 최종 소지 무게입니다.")]
        private float currentMaxCarryWeightKg = 60f;

        [Header("로그")]
        [SerializeField]
        [Tooltip("보너스 적용 결과를 Console에 출력할지 여부입니다.")]
        private bool logBonusReceived = true;

        public float BaseCarryWeightKg => baseCarryWeightKg;
        public int CurrentGymLevel => currentGymLevel;
        public float CurrentCarryWeightBonusKg => currentCarryWeightBonusKg;
        public float CurrentMaxCarryWeightKg => currentMaxCarryWeightKg;

        private void OnEnable()
        {
            EventBus.Subscribe<GymCarryWeightBonusChangedEvent>(HandleCarryWeightBonusChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GymCarryWeightBonusChangedEvent>(HandleCarryWeightBonusChanged);
        }

        private void OnValidate()
        {
            if (baseCarryWeightKg < 1f)
                baseCarryWeightKg = 1f;

            if (!Application.isPlaying)
            {
                currentGymLevel = 1;
                currentCarryWeightBonusKg = 0f;
                currentMaxCarryWeightKg = baseCarryWeightKg;
            }
        }

        private void HandleCarryWeightBonusChanged(GymCarryWeightBonusChangedEvent evt)
        {
            ApplyCarryWeightBonus(evt.level, evt.carryWeightBonusKg);
        }

        private void ApplyCarryWeightBonus(int gymLevel, float carryWeightBonusKg)
        {
            currentGymLevel = Mathf.Max(1, gymLevel);
            currentCarryWeightBonusKg = Mathf.Max(0f, carryWeightBonusKg);
            currentMaxCarryWeightKg = baseCarryWeightKg + currentCarryWeightBonusKg;

            if (!logBonusReceived)
                return;

            Debug.Log(
                $"[GymCarryWeightBonusTestReceiver] 헬스장 보너스 이벤트 수신\n" +
                $"헬스장 레벨: Lv.{currentGymLevel}\n" +
                $"기본 소지 무게: {baseCarryWeightKg:0.##}kg\n" +
                $"보너스: +{currentCarryWeightBonusKg:0.##}kg\n" +
                $"최종 소지 무게: {currentMaxCarryWeightKg:0.##}kg",
                this
            );
        }
    }
}

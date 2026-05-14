using Unity.Netcode;
using UnityEngine;
using System.Collections;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 스태미나만 담당합니다.
    /// 기본 최대 스태미너와 하우징 보너스를 분리해서 관리합니다.
    /// </summary>
    public class PlayerStaminaSystem : NetworkBehaviour
    {
        [Header("====스테미나 설정====")]
        [Tooltip("플레이어가 가질 수 있는 기본 최대 스태미너입니다.\n하우징 보너스는 이 값을 직접 바꾸지 않고 별도 보너스로 더합니다.")]
        [SerializeField, Min(0f)] private float maxStamina = 100f;

        [Header("====하우징 보너스====")]
        [Tooltip("주방, 침대 등 하우징 효과로 증가한 최대 스태미너 보너스입니다.")]
        [SerializeField, Min(0f)] private float housingMaxStaminaBonus;

        [Tooltip("최대 스태미너가 증가할 때 현재 스태미너도 증가분만큼 같이 회복할지 여부입니다.")]
        [SerializeField] private bool fillStaminaWhenMaxStaminaIncreased = true;

        [Header("====회복 설정====")]
        [Tooltip("초당 회복되는 스태미너 양")]
        [SerializeField, Min(0f)] private float regenPerSecond = 8f;

        [Tooltip("스태미너를 소비한 뒤 회복이 다시 시작되기까지의 대기 시간\n값이 클수록 달리기나 행동 후 스태미너 회복이 늦게 시작")]
        [SerializeField, Min(0f)] private float regenDelaySeconds = 1.5f;

        public readonly NetworkVariable<float> CurrentStamina = new(100f);

        [Header("소모품 보정")]
        [SerializeField, Min(0f)] private float temporaryConsumptionMultiplierBonus;

        public float BaseMaxStamina => maxStamina;
        public float HousingMaxStaminaBonus => housingMaxStaminaBonus;
        public float MaxStamina => Mathf.Max(0f, maxStamina + housingMaxStaminaBonus);

        private float lastConsumeTime;
        private Coroutine temporaryConsumptionRoutine;

        private void OnValidate()
        {
            if (maxStamina < 0f)
                maxStamina = 0f;

            if (housingMaxStaminaBonus < 0f)
                housingMaxStaminaBonus = 0f;

            if (regenPerSecond < 0f)
                regenPerSecond = 0f;

            if (regenDelaySeconds < 0f)
                regenDelaySeconds = 0f;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                CurrentStamina.Value = MaxStamina;

            CurrentStamina.OnValueChanged += BroadcastStaminaChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentStamina.OnValueChanged -= BroadcastStaminaChanged;
        }

        private void Update()
        {
            
            if (IsSpawned && !IsServer) return;

            if (CurrentStamina.Value >= MaxStamina)
                return;

            if (Time.time - lastConsumeTime < regenDelaySeconds)
                return;

            CurrentStamina.Value = Mathf.Min(MaxStamina, CurrentStamina.Value + regenPerSecond * Time.deltaTime);
        }

        public bool TryConsume(float amount)
        {
            if (IsSpawned && !IsServer) return false;

            if (amount <= 0f)
                return true;

            float adjustedAmount = amount * (1f + temporaryConsumptionMultiplierBonus);

            if (CurrentStamina.Value < adjustedAmount)
                return false;

            CurrentStamina.Value -= adjustedAmount;
            lastConsumeTime = Time.time;
            return true;
        }

        public bool TryConsumeForLocalTest(float amount)
        {
            if (IsSpawned)
                return false;

            if (amount <= 0f)
                return true;

            float adjustedAmount = amount * (1f + temporaryConsumptionMultiplierBonus);

            if (CurrentStamina.Value < adjustedAmount)
                return false;

            CurrentStamina.Value -= adjustedAmount;
            lastConsumeTime = Time.time;
            return true;
        }

        public void ApplyTemporaryConsumptionMultiplier(float multiplierBonus, float durationSeconds)
        {
            if (IsSpawned && !IsServer)
                return;

            if (temporaryConsumptionRoutine != null)
                StopCoroutine(temporaryConsumptionRoutine);

            temporaryConsumptionRoutine = StartCoroutine(TemporaryConsumptionRoutine(multiplierBonus, durationSeconds));
        }

        private IEnumerator TemporaryConsumptionRoutine(float multiplierBonus, float durationSeconds)
        {
            temporaryConsumptionMultiplierBonus = Mathf.Max(0f, multiplierBonus);

            if (durationSeconds > 0f)
                yield return new WaitForSeconds(durationSeconds);

            temporaryConsumptionMultiplierBonus = 0f;
            temporaryConsumptionRoutine = null;
        }

        /// <summary>
        /// 하우징 시설에서 계산된 최대 스태미너 보너스를 적용합니다.
        /// Kitchen + Bed 보너스 합산값이 이 메서드로 들어옵니다.
        /// </summary>
        public void ApplyHousingMaxStaminaBonus(float bonus)
        {
            ApplyHousingMaxStaminaBonus(bonus, fillStaminaWhenMaxStaminaIncreased);
        }

        /// <summary>
        /// 하우징 최대 스태미너 보너스를 적용합니다.
        /// 서버 스폰 상태에서는 서버에서만 값이 바뀌어야 합니다.
        /// </summary>
        public void ApplyHousingMaxStaminaBonus(float bonus, bool fillIncreasedAmount)
        {
            if (IsSpawned && !IsServer)
                return;

            float nextBonus = Mathf.Max(0f, bonus);

            if (Mathf.Approximately(housingMaxStaminaBonus, nextBonus))
                return;

            float previousMaxStamina = MaxStamina;
            float previousCurrentStamina = CurrentStamina.Value;

            housingMaxStaminaBonus = nextBonus;

            float increasedAmount = Mathf.Max(0f, MaxStamina - previousMaxStamina);

            if (fillIncreasedAmount && increasedAmount > 0f)
            {
                CurrentStamina.Value = Mathf.Min(MaxStamina, CurrentStamina.Value + increasedAmount);
            }
            else if (CurrentStamina.Value > MaxStamina)
            {
                CurrentStamina.Value = MaxStamina;
            }

            bool staminaValueChanged = !Mathf.Approximately(previousCurrentStamina, CurrentStamina.Value);

            if (!IsSpawned || !staminaValueChanged)
                BroadcastStaminaChanged(previousCurrentStamina, CurrentStamina.Value);

            Debug.Log(
                $"[PlayerStaminaSystem] 하우징 최대 스태미너 보너스 적용\n" +
                $"기본 최대 스태미너: {maxStamina:0.##}\n" +
                $"보너스: +{housingMaxStaminaBonus:0.##}\n" +
                $"최종 최대 스태미너: {MaxStamina:0.##}",
                this
            );
        }

        public void ResetHousingMaxStaminaBonus()
        {
            ApplyHousingMaxStaminaBonus(0f, false);
        }

        private void BroadcastStaminaChanged(float oldValue, float newValue)
        {
            EventBus.Publish(new PlayerStaminaChangedEvent
            {
                clientId = OwnerClientId,
                oldValue = oldValue,
                newValue = newValue,
            });
        }

#if UNITY_EDITOR
        [ContextMenu("테스트 하우징 스태미너 보너스 +30 적용")]
        private void DebugApplyHousingStaminaBonus()
        {
            ApplyHousingMaxStaminaBonus(30f, true);
        }

        [ContextMenu("테스트 하우징 스태미너 보너스 초기화")]
        private void DebugResetHousingStaminaBonus()
        {
            ResetHousingMaxStaminaBonus();
        }
#endif
    }
}

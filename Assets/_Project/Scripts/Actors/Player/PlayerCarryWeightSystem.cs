using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace DeadZone.Actors
{
    // 플레이어 최대 소지 무게를 관리
    // 기본 소지 무게와 하우징 보너스를 분리해서 계산

    [DisallowMultipleComponent]
    public sealed class PlayerCarryWeightSystem : NetworkBehaviour
    {
        [Header("소지 무게 설정")]
        [SerializeField]
        [Min(1f)]
        [Tooltip("기본 최대 소지 무게입니다. 기획 기준은 60kg입니다.")]
        private float baseMaxCarryWeightKg = 60f;

        [Header("하우징 보너스")]
        [SerializeField]
        [Min(0f)]
        [Tooltip("헬스장 시설 효과로 증가한 최대 소지 무게 보너스입니다.")]
        private float housingCarryWeightBonusKg;

        [Header("런타임 확인")]
        [SerializeField]
        [Min(0f)]
        [Tooltip("현재 들고 있는 아이템 무게입니다. 인벤토리 연동 후 갱신됩니다.")]
        private float currentCarryWeightKg;

        [Header("소모품 보너스")]
        [SerializeField, Min(0f)] private float temporaryCapacityMultiplierBonus;

        [Header("로그")]
        [SerializeField]
        private bool logBonusChanged = true;

        public float BaseMaxCarryWeightKg => baseMaxCarryWeightKg;
        public float HousingCarryWeightBonusKg => housingCarryWeightBonusKg;
        public float MaxCarryWeightKg => Mathf.Max(1f, (baseMaxCarryWeightKg + housingCarryWeightBonusKg) * (1f + temporaryCapacityMultiplierBonus));
        public float CurrentCarryWeightKg => currentCarryWeightKg;

        private Coroutine temporaryCapacityRoutine;

        public bool IsOverHalf => currentCarryWeightKg >= MaxCarryWeightKg * 0.5f;
        public bool IsOverLimit => currentCarryWeightKg > MaxCarryWeightKg;
        public bool IsMovementBlocked => currentCarryWeightKg >= 100f;

        private void OnValidate()
        {
            if (baseMaxCarryWeightKg < 1f)
                baseMaxCarryWeightKg = 1f;

            if (housingCarryWeightBonusKg < 0f)
                housingCarryWeightBonusKg = 0f;

            if (currentCarryWeightKg < 0f)
                currentCarryWeightKg = 0f;
        }

        // 하우징 시설에서 계산된 소지 무게 보너스를 적용
        // 서버 스폰 상태에서는 서버에서만 값이 바뀌어야 합니다.
        public void ApplyHousingCarryWeightBonus(float bonusKg)
        {
            if (IsSpawned && !IsServer)
                return;

            float nextBonus = Mathf.Max(0f, bonusKg);

            if (Mathf.Approximately(housingCarryWeightBonusKg, nextBonus))
                return;

            housingCarryWeightBonusKg = nextBonus;

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerCarryWeightSystem] 하우징 소지 무게 보너스 적용\n" +
                    $"기본 최대 소지 무게: {baseMaxCarryWeightKg:0.##}kg\n" +
                    $"보너스: +{housingCarryWeightBonusKg:0.##}kg\n" +
                    $"최종 최대 소지 무게: {MaxCarryWeightKg:0.##}kg",
                    this
                );
            }
        }

        public void ResetHousingCarryWeightBonus()
        {
            ApplyHousingCarryWeightBonus(0f);
        }

        // 추후 GridInventory의 실제 아이템 무게 합산값을 넣기 위한 메서드
        public void SetCurrentCarryWeight(float weightKg)
        {
            if (IsSpawned && !IsServer)
                return;

            currentCarryWeightKg = Mathf.Max(0f, weightKg);
        }

        public void ApplyTemporaryCapacityMultiplier(float multiplierBonus, float durationSeconds)
        {
            if (IsSpawned && !IsServer)
                return;

            if (temporaryCapacityRoutine != null)
                StopCoroutine(temporaryCapacityRoutine);

            temporaryCapacityRoutine = StartCoroutine(TemporaryCapacityRoutine(multiplierBonus, durationSeconds));
        }

        private IEnumerator TemporaryCapacityRoutine(float multiplierBonus, float durationSeconds)
        {
            temporaryCapacityMultiplierBonus = Mathf.Max(0f, multiplierBonus);

            if (durationSeconds > 0f)
                yield return new WaitForSeconds(durationSeconds);

            temporaryCapacityMultiplierBonus = 0f;
            temporaryCapacityRoutine = null;
        }

#if UNITY_EDITOR
        [ContextMenu("테스트 하우징 소지 무게 보너스 +15kg 적용")]
        private void DebugApplyCarryWeightBonus()
        {
            ApplyHousingCarryWeightBonus(15f);
        }

        [ContextMenu("테스트 하우징 소지 무게 보너스 초기화")]
        private void DebugResetCarryWeightBonus()
        {
            ResetHousingCarryWeightBonus();
        }
#endif
    }
}

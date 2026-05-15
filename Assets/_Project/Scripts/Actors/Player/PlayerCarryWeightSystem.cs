using Unity.Netcode;
using UnityEngine;
using System.Collections;

using DeadZone.Core;

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

        // 서버에서 계산한 현재 소지 무게를 오너 클라이언트 HUD/이동 판정에 전달합니다.
        public readonly NetworkVariable<float> ReplicatedCurrentCarryWeightKg = new(
            0f,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        // 헬스장 하우징 보너스가 적용된 최대 소지 무게를 클라이언트에도 같은 값으로 복제합니다.
        public readonly NetworkVariable<float> ReplicatedMaxCarryWeightKg = new(
            60f,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        public float BaseMaxCarryWeightKg => baseMaxCarryWeightKg;
        public float HousingCarryWeightBonusKg => housingCarryWeightBonusKg;
        public float MaxCarryWeightKg => ResolveVisibleMaxCarryWeightKg();
        public float CurrentCarryWeightKg => ResolveVisibleCurrentCarryWeightKg();

        private Coroutine temporaryCapacityRoutine;

        public bool IsOverHalf => CurrentCarryWeightKg >= MaxCarryWeightKg * 0.5f;
        public bool IsOverLimit => CurrentCarryWeightKg > MaxCarryWeightKg;
        public bool IsMovementBlocked => CurrentCarryWeightKg >= MaxCarryWeightKg;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                ReplicatedCurrentCarryWeightKg.Value = currentCarryWeightKg;
                ReplicatedMaxCarryWeightKg.Value = CalculateMaxCarryWeightKg();
            }

            ReplicatedCurrentCarryWeightKg.OnValueChanged += HandleReplicatedCurrentCarryWeightChanged;
            ReplicatedMaxCarryWeightKg.OnValueChanged += HandleReplicatedMaxCarryWeightChanged;
        }

        public override void OnNetworkDespawn()
        {
            ReplicatedCurrentCarryWeightKg.OnValueChanged -= HandleReplicatedCurrentCarryWeightChanged;
            ReplicatedMaxCarryWeightKg.OnValueChanged -= HandleReplicatedMaxCarryWeightChanged;

            base.OnNetworkDespawn();
        }

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

            float previousMaxCarryWeightKg = CalculateMaxCarryWeightKg();
            housingCarryWeightBonusKg = nextBonus;
            float nextMaxCarryWeightKg = CalculateMaxCarryWeightKg();

            // 최대 소지 무게가 바뀌면 클라이언트 UI와 과적 판정도 즉시 같은 값으로 갱신합니다.
            if (IsSpawned && IsServer)
                ReplicatedMaxCarryWeightKg.Value = nextMaxCarryWeightKg;

            PublishCarryWeightChanged(currentCarryWeightKg, currentCarryWeightKg, previousMaxCarryWeightKg, nextMaxCarryWeightKg);

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

            float previousCurrentWeightKg = currentCarryWeightKg;
            float nextCurrentWeightKg = Mathf.Max(0f, weightKg);

            if (Mathf.Approximately(previousCurrentWeightKg, nextCurrentWeightKg))
                return;

            currentCarryWeightKg = nextCurrentWeightKg;

            // 실제 소지 무게 변경도 서버 값을 기준으로 오너 클라이언트에 복제합니다.
            if (IsSpawned && IsServer)
                ReplicatedCurrentCarryWeightKg.Value = currentCarryWeightKg;

            PublishCarryWeightChanged(previousCurrentWeightKg, currentCarryWeightKg, CalculateMaxCarryWeightKg(), CalculateMaxCarryWeightKg());
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
            float previousMaxCarryWeightKg = CalculateMaxCarryWeightKg();
            temporaryCapacityMultiplierBonus = Mathf.Max(0f, multiplierBonus);
            float nextMaxCarryWeightKg = CalculateMaxCarryWeightKg();

            if (IsSpawned && IsServer)
                ReplicatedMaxCarryWeightKg.Value = nextMaxCarryWeightKg;

            PublishCarryWeightChanged(currentCarryWeightKg, currentCarryWeightKg, previousMaxCarryWeightKg, nextMaxCarryWeightKg);

            if (durationSeconds > 0f)
                yield return new WaitForSeconds(durationSeconds);

            previousMaxCarryWeightKg = CalculateMaxCarryWeightKg();
            temporaryCapacityMultiplierBonus = 0f;
            nextMaxCarryWeightKg = CalculateMaxCarryWeightKg();

            if (IsSpawned && IsServer)
                ReplicatedMaxCarryWeightKg.Value = nextMaxCarryWeightKg;

            PublishCarryWeightChanged(currentCarryWeightKg, currentCarryWeightKg, previousMaxCarryWeightKg, nextMaxCarryWeightKg);
            temporaryCapacityRoutine = null;
        }

        private void HandleReplicatedCurrentCarryWeightChanged(float oldValue, float newValue)
        {
            PublishCarryWeightChanged(oldValue, newValue, ResolveVisibleMaxCarryWeightKg(), ResolveVisibleMaxCarryWeightKg());
        }

        private void HandleReplicatedMaxCarryWeightChanged(float oldValue, float newValue)
        {
            PublishCarryWeightChanged(ResolveVisibleCurrentCarryWeightKg(), ResolveVisibleCurrentCarryWeightKg(), oldValue, newValue);
        }

        private float CalculateMaxCarryWeightKg()
        {
            return Mathf.Max(1f, (baseMaxCarryWeightKg + housingCarryWeightBonusKg) * (1f + temporaryCapacityMultiplierBonus));
        }

        private float ResolveVisibleMaxCarryWeightKg()
        {
            // 클라이언트에서는 로컬 계산값보다 서버가 복제한 최대 소지 무게를 우선 사용합니다.
            if (IsSpawned && !IsServer && ReplicatedMaxCarryWeightKg.Value > 0f)
                return ReplicatedMaxCarryWeightKg.Value;

            return CalculateMaxCarryWeightKg();
        }

        private float ResolveVisibleCurrentCarryWeightKg()
        {
            // 클라이언트에서는 서버 복제 값을 사용해 UI와 이동 제한 판정이 서버와 어긋나지 않게 합니다.
            if (IsSpawned && !IsServer)
                return Mathf.Max(0f, ReplicatedCurrentCarryWeightKg.Value);

            return currentCarryWeightKg;
        }

        private void PublishCarryWeightChanged(
            float oldCurrentWeightKg,
            float newCurrentWeightKg,
            float oldMaxCarryWeightKg,
            float newMaxCarryWeightKg)
        {
            EventBus.Publish(new PlayerCarryWeightChangedEvent
            {
                clientId = OwnerClientId,
                oldCurrentWeightKg = oldCurrentWeightKg,
                newCurrentWeightKg = newCurrentWeightKg,
                oldMaxCarryWeightKg = oldMaxCarryWeightKg,
                newMaxCarryWeightKg = newMaxCarryWeightKg,
                housingCarryWeightBonusKg = housingCarryWeightBonusKg,
                isOverWeight = newCurrentWeightKg > newMaxCarryWeightKg,
                weightRatio = newMaxCarryWeightKg > 0f
                    ? Mathf.Clamp01(newCurrentWeightKg / newMaxCarryWeightKg)
                    : 0f
            });
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

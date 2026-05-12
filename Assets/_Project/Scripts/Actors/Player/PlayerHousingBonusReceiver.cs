using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    // 이 플레이어의 개인 PlayerHousingProgress를 기준으로 하우징 보너스를 적용합니다.
    // 공유 은신처 시설 오브젝트와 플레이어별 하우징 진행도를 분리해서 관리합니다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHousingProgress))]
    public sealed class PlayerHousingBonusReceiver : NetworkBehaviour
    {
        [Header("적용 대상")]
        [SerializeField] private PlayerHealthSystem healthSystem;
        [SerializeField] private PlayerStaminaSystem staminaSystem;
        [SerializeField] private PlayerCarryWeightSystem carryWeightSystem;
        [SerializeField] private PlayerHousingProgress housingProgress;

        [Header("적용 옵션")]
        [SerializeField] private bool fillHpWhenMaxHpIncreased = true;
        [SerializeField] private bool fillStaminaWhenMaxStaminaIncreased = true;

        [Header("보너스 규칙")]
        [SerializeField, Min(1)] private int bonusStartLevel = 2;
        [SerializeField, Min(0f)] private float medicalHealthBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float kitchenStaminaBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float bedStaminaBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float gymCarryWeightBonusPerLevelKg = 7.5f;

        [Header("실행 중 보너스")]
        [SerializeField] private float medicalHealthBonus;
        [SerializeField] private float kitchenStaminaBonus;
        [SerializeField] private float bedStaminaBonus;
        [SerializeField] private float gymCarryWeightBonusKg;

        [Header("로그")]
        [SerializeField] private bool logBonusChanged = true;

        public float MedicalHealthBonus => medicalHealthBonus;
        public float KitchenStaminaBonus => kitchenStaminaBonus;
        public float BedStaminaBonus => bedStaminaBonus;
        public float GymCarryWeightBonusKg => gymCarryWeightBonusKg;
        public float TotalStaminaBonus => kitchenStaminaBonus + bedStaminaBonus;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnValidate()
        {
            if (bonusStartLevel < 1)
                bonusStartLevel = 1;

            FindRequiredComponents();
        }

        public override void OnNetworkSpawn()
        {
            FindRequiredComponents();

            if (housingProgress != null)
            {
                housingProgress.FacilityLevelChanged -= HandleFacilityLevelChanged;
                housingProgress.FacilityLevelChanged += HandleFacilityLevelChanged;
            }

            RecalculateAndApplyAllBonuses();

            if (logBonusChanged)
                Debug.Log("[PlayerHousingBonusReceiver] 플레이어 하우징 보너스 적용기가 준비되었습니다.", this);
        }

        public override void OnNetworkDespawn()
        {
            if (housingProgress != null)
                housingProgress.FacilityLevelChanged -= HandleFacilityLevelChanged;
        }

        private void FindRequiredComponents()
        {
            if (healthSystem == null)
                healthSystem = GetComponent<PlayerHealthSystem>();

            if (staminaSystem == null)
                staminaSystem = GetComponent<PlayerStaminaSystem>();

            if (carryWeightSystem == null)
                carryWeightSystem = GetComponent<PlayerCarryWeightSystem>();

            if (housingProgress == null)
                housingProgress = GetComponent<PlayerHousingProgress>();
        }

        private bool CanApplyToThisPlayer()
        {
            if (!IsSpawned)
                return true;

            return IsServer;
        }

        private void HandleFacilityLevelChanged(FacilityType facilityType, int oldLevel, int newLevel)
        {
            switch (facilityType)
            {
                case FacilityType.Medical:
                case FacilityType.Kitchen:
                case FacilityType.Bed:
                case FacilityType.Gym:
                    RecalculateAndApplyAllBonuses();
                    break;
            }
        }

        private void RecalculateAndApplyAllBonuses()
        {
            if (!CanApplyToThisPlayer())
                return;

            if (housingProgress == null)
            {
                FindRequiredComponents();
                if (housingProgress == null)
                    return;
            }

            medicalHealthBonus = CalculateBonus(housingProgress.GetLevel(FacilityType.Medical), medicalHealthBonusPerLevel);
            kitchenStaminaBonus = CalculateBonus(housingProgress.GetLevel(FacilityType.Kitchen), kitchenStaminaBonusPerLevel);
            bedStaminaBonus = CalculateBonus(housingProgress.GetLevel(FacilityType.Bed), bedStaminaBonusPerLevel);
            gymCarryWeightBonusKg = CalculateBonus(housingProgress.GetLevel(FacilityType.Gym), gymCarryWeightBonusPerLevelKg);

            ApplyHealthBonus();
            ApplyStaminaBonus();
            ApplyCarryWeightBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 개인 하우징 보너스 적용 완료\n" +
                    $"클라이언트 ID: {OwnerClientId}\n" +
                    $"의료시설 최대 HP: +{medicalHealthBonus:0.##}\n" +
                    $"주방 스태미나: +{kitchenStaminaBonus:0.##}\n" +
                    $"침대 스태미나: +{bedStaminaBonus:0.##}\n" +
                    $"체육관 소지 무게: +{gymCarryWeightBonusKg:0.##}kg",
                    this);
            }
        }

        private float CalculateBonus(int level, float bonusPerLevel)
        {
            if (level < bonusStartLevel)
                return 0f;

            int bonusLevelCount = level - bonusStartLevel + 1;
            return Mathf.Max(0f, bonusLevelCount * bonusPerLevel);
        }

        private void ApplyHealthBonus()
        {
            if (healthSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerHealthSystem을 찾을 수 없습니다.", this);
                return;
            }

            healthSystem.ApplyHousingMaxHpBonus(medicalHealthBonus, fillHpWhenMaxHpIncreased);
        }

        private void ApplyStaminaBonus()
        {
            if (staminaSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerStaminaSystem을 찾을 수 없습니다.", this);
                return;
            }

            staminaSystem.ApplyHousingMaxStaminaBonus(TotalStaminaBonus, fillStaminaWhenMaxStaminaIncreased);
        }

        private void ApplyCarryWeightBonus()
        {
            if (carryWeightSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerCarryWeightSystem을 찾을 수 없습니다.", this);
                return;
            }

            carryWeightSystem.ApplyHousingCarryWeightBonus(gymCarryWeightBonusKg);
        }

#if UNITY_EDITOR
        [ContextMenu("현재 하우징 보너스 출력")]
        private void DebugPrintCurrentBonuses()
        {
            Debug.Log(
                $"[PlayerHousingBonusReceiver] 현재 하우징 보너스\n" +
                $"의료시설 최대 HP: +{medicalHealthBonus:0.##}\n" +
                $"주방 스태미나: +{kitchenStaminaBonus:0.##}\n" +
                $"침대 스태미나: +{bedStaminaBonus:0.##}\n" +
                $"전체 스태미나: +{TotalStaminaBonus:0.##}\n" +
                $"체육관 소지 무게: +{gymCarryWeightBonusKg:0.##}kg",
                this);
        }

        [ContextMenu("하우징 보너스 초기화")]
        private void DebugResetBonuses()
        {
            medicalHealthBonus = 0f;
            kitchenStaminaBonus = 0f;
            bedStaminaBonus = 0f;
            gymCarryWeightBonusKg = 0f;

            ApplyHealthBonus();
            ApplyStaminaBonus();
            ApplyCarryWeightBonus();
        }
#endif
    }
}

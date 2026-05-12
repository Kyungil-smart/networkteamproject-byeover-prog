using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    // Applies housing bonuses from this player's own PlayerHousingProgress.
    // This keeps personal housing progress separate from shared hideout facility objects.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHousingProgress))]
    public sealed class PlayerHousingBonusReceiver : NetworkBehaviour
    {
        [Header("Apply Target")]
        [SerializeField] private PlayerHealthSystem healthSystem;
        [SerializeField] private PlayerStaminaSystem staminaSystem;
        [SerializeField] private PlayerCarryWeightSystem carryWeightSystem;
        [SerializeField] private PlayerHousingProgress housingProgress;

        [Header("Apply Options")]
        [SerializeField] private bool fillHpWhenMaxHpIncreased = true;
        [SerializeField] private bool fillStaminaWhenMaxStaminaIncreased = true;

        [Header("Bonus Rules")]
        [SerializeField, Min(1)] private int bonusStartLevel = 2;
        [SerializeField, Min(0f)] private float medicalHealthBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float kitchenStaminaBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float bedStaminaBonusPerLevel = 5f;
        [SerializeField, Min(0f)] private float gymCarryWeightBonusPerLevelKg = 7.5f;

        [Header("Runtime Bonuses")]
        [SerializeField] private float medicalHealthBonus;
        [SerializeField] private float kitchenStaminaBonus;
        [SerializeField] private float bedStaminaBonus;
        [SerializeField] private float gymCarryWeightBonusKg;

        [Header("Log")]
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
                Debug.Log("[PlayerHousingBonusReceiver] Player housing bonus receiver is ready.", this);
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
                    $"[PlayerHousingBonusReceiver] Applied personal housing bonuses\n" +
                    $"ClientId: {OwnerClientId}\n" +
                    $"Medical HP: +{medicalHealthBonus:0.##}\n" +
                    $"Kitchen stamina: +{kitchenStaminaBonus:0.##}\n" +
                    $"Bed stamina: +{bedStaminaBonus:0.##}\n" +
                    $"Gym carry weight: +{gymCarryWeightBonusKg:0.##}kg",
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
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerHealthSystem is missing.", this);
                return;
            }

            healthSystem.ApplyHousingMaxHpBonus(medicalHealthBonus, fillHpWhenMaxHpIncreased);
        }

        private void ApplyStaminaBonus()
        {
            if (staminaSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerStaminaSystem is missing.", this);
                return;
            }

            staminaSystem.ApplyHousingMaxStaminaBonus(TotalStaminaBonus, fillStaminaWhenMaxStaminaIncreased);
        }

        private void ApplyCarryWeightBonus()
        {
            if (carryWeightSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerCarryWeightSystem is missing.", this);
                return;
            }

            carryWeightSystem.ApplyHousingCarryWeightBonus(gymCarryWeightBonusKg);
        }

#if UNITY_EDITOR
        [ContextMenu("Print Current Housing Bonuses")]
        private void DebugPrintCurrentBonuses()
        {
            Debug.Log(
                $"[PlayerHousingBonusReceiver] Current housing bonuses\n" +
                $"Medical max HP: +{medicalHealthBonus:0.##}\n" +
                $"Kitchen stamina: +{kitchenStaminaBonus:0.##}\n" +
                $"Bed stamina: +{bedStaminaBonus:0.##}\n" +
                $"Total stamina: +{TotalStaminaBonus:0.##}\n" +
                $"Gym carry weight: +{gymCarryWeightBonusKg:0.##}kg",
                this);
        }

        [ContextMenu("Reset Housing Bonuses")]
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
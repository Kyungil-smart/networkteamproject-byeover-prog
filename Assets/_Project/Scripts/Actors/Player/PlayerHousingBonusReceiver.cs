using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    // 하우징 시설 보너스 이벤트를 받아 실제 플레이어 스탯 시스템에 반영
    // 시설 시스템은 보너스 계산만 담당하고, 실제 적용은 이 컴포넌트가 담당
    [DisallowMultipleComponent]
    public sealed class PlayerHousingBonusReceiver : NetworkBehaviour
    {
        [Header("적용 대상")]
        [SerializeField]
        private PlayerHealthSystem healthSystem;

        [SerializeField]
        private PlayerStaminaSystem staminaSystem;

        [SerializeField]
        private PlayerCarryWeightSystem carryWeightSystem;

        [Header("적용 옵션")]
        [SerializeField]
        private bool fillHpWhenMaxHpIncreased = true;

        [SerializeField]
        private bool fillStaminaWhenMaxStaminaIncreased = true;

        [Header("런타임 보너스 확인")]
        [SerializeField]
        private float medicalHealthBonus;

        [SerializeField]
        private float kitchenStaminaBonus;

        [SerializeField]
        private float bedStaminaBonus;

        [SerializeField]
        private float gymCarryWeightBonusKg;

        [Header("로그")]
        [SerializeField]
        private bool logBonusChanged = true;

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
            FindRequiredComponents();
        }

        public override void OnNetworkSpawn()
        {
            FindRequiredComponents();

            EventBus.Subscribe<MedicalHealthBonusChangedEvent>(HandleMedicalHealthBonusChanged);
            EventBus.Subscribe<KitchenStaminaBonusChangedEvent>(HandleKitchenStaminaBonusChanged);
            EventBus.Subscribe<BedStaminaBonusChangedEvent>(HandleBedStaminaBonusChanged);
            EventBus.Subscribe<GymCarryWeightBonusChangedEvent>(HandleGymCarryWeightBonusChanged);

            if (logBonusChanged)
                Debug.Log("[PlayerHousingBonusReceiver] 하우징 보너스 이벤트 구독 완료", this);
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<MedicalHealthBonusChangedEvent>(HandleMedicalHealthBonusChanged);
            EventBus.Unsubscribe<KitchenStaminaBonusChangedEvent>(HandleKitchenStaminaBonusChanged);
            EventBus.Unsubscribe<BedStaminaBonusChangedEvent>(HandleBedStaminaBonusChanged);
            EventBus.Unsubscribe<GymCarryWeightBonusChangedEvent>(HandleGymCarryWeightBonusChanged);
        }

        private void FindRequiredComponents()
        {
            if (healthSystem == null)
                healthSystem = GetComponent<PlayerHealthSystem>();

            if (staminaSystem == null)
                staminaSystem = GetComponent<PlayerStaminaSystem>();

            if (carryWeightSystem == null)
                carryWeightSystem = GetComponent<PlayerCarryWeightSystem>();
        }

        private bool CanApplyToThisPlayer()
        {
            if (!IsSpawned)
                return true;

            return IsServer || IsOwner;
        }

        private void HandleMedicalHealthBonusChanged(MedicalHealthBonusChangedEvent evt)
        {
            if (!CanApplyToThisPlayer())
                return;

            medicalHealthBonus = Mathf.Max(0f, evt.maxHealthBonus);
            ApplyHealthBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 의료시설 체력 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"최대 체력 보너스: +{medicalHealthBonus:0.##}",
                    this
                );
            }
        }

        private void HandleKitchenStaminaBonusChanged(KitchenStaminaBonusChangedEvent evt)
        {
            if (!CanApplyToThisPlayer())
                return;

            kitchenStaminaBonus = Mathf.Max(0f, evt.maxStaminaBonus);
            ApplyStaminaBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 주방 스태미너 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"주방 스태미너 보너스: +{kitchenStaminaBonus:0.##}\n" +
                    $"전체 스태미너 보너스: +{TotalStaminaBonus:0.##}",
                    this
                );
            }
        }

        private void HandleBedStaminaBonusChanged(BedStaminaBonusChangedEvent evt)
        {
            if (!CanApplyToThisPlayer())
                return;

            bedStaminaBonus = Mathf.Max(0f, evt.maxStaminaBonus);
            ApplyStaminaBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 침대 스태미너 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"침대 스태미너 보너스: +{bedStaminaBonus:0.##}\n" +
                    $"전체 스태미너 보너스: +{TotalStaminaBonus:0.##}",
                    this
                );
            }
        }

        private void HandleGymCarryWeightBonusChanged(GymCarryWeightBonusChangedEvent evt)
        {
            if (!CanApplyToThisPlayer())
                return;

            gymCarryWeightBonusKg = Mathf.Max(0f, evt.carryWeightBonusKg);
            ApplyCarryWeightBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 헬스장 소지 무게 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"소지 무게 보너스: +{gymCarryWeightBonusKg:0.##}kg",
                    this
                );
            }
        }

        private void ApplyHealthBonus()
        {
            if (healthSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerHealthSystem이 없어 체력 보너스를 적용하지 못했습니다.", this);
                return;
            }

            healthSystem.ApplyHousingMaxHpBonus(medicalHealthBonus, fillHpWhenMaxHpIncreased);
        }

        private void ApplyStaminaBonus()
        {
            if (staminaSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerStaminaSystem이 없어 스태미너 보너스를 적용하지 못했습니다.", this);
                return;
            }

            staminaSystem.ApplyHousingMaxStaminaBonus(TotalStaminaBonus, fillStaminaWhenMaxStaminaIncreased);
        }

        private void ApplyCarryWeightBonus()
        {
            if (carryWeightSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerCarryWeightSystem이 없어 소지 무게 보너스를 적용하지 못했습니다.", this);
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
                $"의료시설 최대 체력: +{medicalHealthBonus:0.##}\n" +
                $"주방 스태미너: +{kitchenStaminaBonus:0.##}\n" +
                $"침대 스태미너: +{bedStaminaBonus:0.##}\n" +
                $"전체 스태미너: +{TotalStaminaBonus:0.##}\n" +
                $"헬스장 소지 무게: +{gymCarryWeightBonusKg:0.##}kg",
                this
            );
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

            Debug.Log("[PlayerHousingBonusReceiver] 하우징 보너스를 초기화했습니다.", this);
        }
#endif
    }
}
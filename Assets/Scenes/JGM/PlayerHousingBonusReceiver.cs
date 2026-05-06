using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    // 하우징 시설 효과 이벤트를 받아 플레이어 능력치 시스템에 반영
    // 하우징 시스템이 PlayerHealthSystem, PlayerStaminaSystem, PlayerCarryWeightSystem, UI를 직접 참조하지 않게 만드는 중간 수신자
    [DisallowMultipleComponent]
    public sealed class PlayerHousingBonusReceiver : NetworkBehaviour
    {
        [Header("참조")]
        [SerializeField]
        [Tooltip("플레이어 체력 시스템입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private PlayerHealthSystem healthSystem;

        [SerializeField]
        [Tooltip("플레이어 스태미너 시스템입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private PlayerStaminaSystem staminaSystem;

        [SerializeField]
        [Tooltip("플레이어 소지 무게 시스템입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private PlayerCarryWeightSystem carryWeightSystem;

        [Header("적용 설정")]
        [SerializeField]
        [Tooltip("최대 체력 증가 시 현재 체력도 증가분만큼 채웁니다.")]
        private bool fillHpWhenMaxHpIncreased = true;

        [SerializeField]
        [Tooltip("최대 스태미너 증가 시 현재 스태미너도 증가분만큼 채웁니다.")]
        private bool fillStaminaWhenMaxStaminaIncreased = true;

        [Header("디버그")]
        [SerializeField]
        [Tooltip("하우징 보너스 수신 및 적용 로그를 출력합니다.")]
        private bool logBonusChanged = true;

        private float medicalHealthBonus;
        private float kitchenStaminaBonus;
        private float bedStaminaBonus;
        private float gymCarryWeightBonus;

        public float MedicalHealthBonus => medicalHealthBonus;
        public float KitchenStaminaBonus => kitchenStaminaBonus;
        public float BedStaminaBonus => bedStaminaBonus;
        public float TotalStaminaBonus => kitchenStaminaBonus + bedStaminaBonus;
        public float GymCarryWeightBonus => gymCarryWeightBonus;

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

        private void OnEnable()
        {
            EventBus.Subscribe<MedicalHealthBonusChangedEvent>(OnMedicalHealthBonusChanged);
            EventBus.Subscribe<KitchenStaminaBonusChangedEvent>(OnKitchenStaminaBonusChanged);
            EventBus.Subscribe<BedStaminaBonusChangedEvent>(OnBedStaminaBonusChanged);
            EventBus.Subscribe<GymCarryWeightBonusChangedEvent>(OnGymCarryWeightBonusChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MedicalHealthBonusChangedEvent>(OnMedicalHealthBonusChanged);
            EventBus.Unsubscribe<KitchenStaminaBonusChangedEvent>(OnKitchenStaminaBonusChanged);
            EventBus.Unsubscribe<BedStaminaBonusChangedEvent>(OnBedStaminaBonusChanged);
            EventBus.Unsubscribe<GymCarryWeightBonusChangedEvent>(OnGymCarryWeightBonusChanged);
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

        private bool ShouldApplyToThisPlayer()
        {
            if (!IsSpawned)
                return true;

            return IsServer || IsOwner;
        }

        private void OnMedicalHealthBonusChanged(MedicalHealthBonusChangedEvent evt)
        {
            if (!ShouldApplyToThisPlayer())
                return;

            medicalHealthBonus = Mathf.Max(0f, evt.maxHealthBonus);
            ApplyHealthBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 의료 시설 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"최대 체력 보너스: +{medicalHealthBonus:0.##}",
                    this
                );
            }
        }

        private void OnKitchenStaminaBonusChanged(KitchenStaminaBonusChangedEvent evt)
        {
            if (!ShouldApplyToThisPlayer())
                return;

            kitchenStaminaBonus = Mathf.Max(0f, evt.maxStaminaBonus);
            ApplyStaminaBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 주방 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"주방 스태미너 보너스: +{kitchenStaminaBonus:0.##}\n" +
                    $"현재 전체 스태미너 보너스: +{TotalStaminaBonus:0.##}",
                    this
                );
            }
        }

        private void OnBedStaminaBonusChanged(BedStaminaBonusChangedEvent evt)
        {
            if (!ShouldApplyToThisPlayer())
                return;

            bedStaminaBonus = Mathf.Max(0f, evt.maxStaminaBonus);
            ApplyStaminaBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 침실 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"침실 스태미너 보너스: +{bedStaminaBonus:0.##}\n" +
                    $"현재 전체 스태미너 보너스: +{TotalStaminaBonus:0.##}",
                    this
                );
            }
        }

        private void OnGymCarryWeightBonusChanged(GymCarryWeightBonusChangedEvent evt)
        {
            if (!ShouldApplyToThisPlayer())
                return;

            gymCarryWeightBonus = Mathf.Max(0f, evt.carryWeightBonusKg);
            ApplyCarryWeightBonus();

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[PlayerHousingBonusReceiver] 헬스장 소지 무게 보너스 수신\n" +
                    $"시설 레벨: Lv.{evt.level}\n" +
                    $"소지 무게 보너스: +{gymCarryWeightBonus:0.##}kg",
                    this
                );
            }
        }

        private void ApplyHealthBonus()
        {
            if (healthSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerHealthSystem이 연결되어 있지 않아 체력 보너스를 적용할 수 없습니다.", this);
                return;
            }

            healthSystem.ApplyHousingMaxHpBonus(medicalHealthBonus, fillHpWhenMaxHpIncreased);
        }

        private void ApplyStaminaBonus()
        {
            if (staminaSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerStaminaSystem이 연결되어 있지 않아 스태미너 보너스를 적용할 수 없습니다.", this);
                return;
            }

            staminaSystem.ApplyHousingMaxStaminaBonus(TotalStaminaBonus, fillStaminaWhenMaxStaminaIncreased);
        }

        private void ApplyCarryWeightBonus()
        {
            if (carryWeightSystem == null)
            {
                Debug.LogWarning("[PlayerHousingBonusReceiver] PlayerCarryWeightSystem이 연결되어 있지 않아 소지 무게 보너스를 적용할 수 없습니다.", this);
                return;
            }

            carryWeightSystem.ApplyHousingCarryWeightBonus(gymCarryWeightBonus);
        }

#if UNITY_EDITOR
        [ContextMenu("디버그 현재 하우징 보너스 출력")]
        private void DebugPrintCurrentBonuses()
        {
            Debug.Log(
                $"[PlayerHousingBonusReceiver] 현재 하우징 보너스\n" +
                $"의료 최대 체력: +{medicalHealthBonus:0.##}\n" +
                $"주방 스태미너: +{kitchenStaminaBonus:0.##}\n" +
                $"침실 스태미너: +{bedStaminaBonus:0.##}\n" +
                $"전체 스태미너: +{TotalStaminaBonus:0.##}\n" +
                $"헬스장 소지 무게: +{gymCarryWeightBonus:0.##}kg",
                this
            );
        }

        [ContextMenu("디버그 하우징 보너스 초기화")]
        private void DebugResetBonuses()
        {
            medicalHealthBonus = 0f;
            kitchenStaminaBonus = 0f;
            bedStaminaBonus = 0f;
            gymCarryWeightBonus = 0f;

            ApplyHealthBonus();
            ApplyStaminaBonus();
            ApplyCarryWeightBonus();

            Debug.Log("[PlayerHousingBonusReceiver] 하우징 보너스를 초기화했습니다.", this);
        }
#endif
    }
}
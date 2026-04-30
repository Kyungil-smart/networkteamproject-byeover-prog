using System;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 주방 시설 레벨에 따른 최대 스테미너 보너스를 계산합니다.
    /// PlayerStats, UI, 인벤토리는 직접 참조하지 않고 보너스 값만 제공합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public class KitchenStaminaBonusController : MonoBehaviour
    {
        [Header("주방 시설")]
        [SerializeField]
        [Tooltip("스테미너 보너스를 계산할 주방 시설입니다. 비워두면 같은 오브젝트의 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase kitchenFacility;

        [Header("스테미너 보너스 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("스테미너 보너스가 시작되는 주방 레벨입니다. 기본값은 Lv2입니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        [Tooltip("주방 레벨이 1 증가할 때마다 늘어나는 최대 스테미너 보너스입니다.")]
        private int staminaBonusPerLevel = 5;

        [Header("오프라인 테스트")]
        [SerializeField]
        [Tooltip("NetworkVariable을 직접 바꾸지 않고 테스트용 레벨로 스테미너 보너스를 계산할지 여부입니다.")]
        private bool useOfflineTestLevel;

        [SerializeField]
        [Range(1, 4)]
        [Tooltip("오프라인 테스트에서 사용할 주방 레벨입니다.")]
        private int offlineTestLevel = 1;

        [Header("현재 보너스 확인")]
        [SerializeField]
        [Tooltip("현재 주방 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentKitchenLevel = 1;

        [SerializeField]
        [Tooltip("현재 주방 레벨로 적용되는 최대 스테미너 보너스입니다. 런타임 확인용 값입니다.")]
        private int currentMaxStaminaBonus;

        [Header("로그")]
        [SerializeField]
        [Tooltip("주방 스테미너 보너스 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        public int CurrentKitchenLevel => currentKitchenLevel;
        public int CurrentMaxStaminaBonus => currentMaxStaminaBonus;

        public event Action<int, int> OnStaminaBonusChanged;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            RefreshBonus();
        }

        private void OnEnable()
        {
            SubscribeFacilityLevelChanged();
            RefreshBonus();
        }

        private void OnDisable()
        {
            UnsubscribeFacilityLevelChanged();
        }

        private void OnValidate()
        {
            if (bonusStartLevel < 2)
                bonusStartLevel = 2;

            if (staminaBonusPerLevel < 0)
                staminaBonusPerLevel = 0;

            offlineTestLevel = Mathf.Clamp(offlineTestLevel, 1, 4);

            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                currentKitchenLevel = useOfflineTestLevel ? offlineTestLevel : 1;
                currentMaxStaminaBonus = CalculateMaxStaminaBonus(currentKitchenLevel);
            }
        }

        private void FindRequiredComponents()
        {
            if (kitchenFacility == null)
                kitchenFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (kitchenFacility == null)
                return;

            kitchenFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            kitchenFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (kitchenFacility == null)
                return;

            kitchenFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void HandleFacilityLevelChanged(int previousLevel, int newLevel)
        {
            if (useOfflineTestLevel)
                return;

            RefreshBonus();
        }

        public void RefreshBonus()
        {
            if (!IsValidKitchenFacility())
                return;

            int previousLevel = currentKitchenLevel;
            int previousBonus = currentMaxStaminaBonus;

            currentKitchenLevel = GetCurrentKitchenLevel();
            currentMaxStaminaBonus = CalculateMaxStaminaBonus(currentKitchenLevel);

            if (previousLevel == currentKitchenLevel && previousBonus == currentMaxStaminaBonus)
                return;

            OnStaminaBonusChanged?.Invoke(currentKitchenLevel, currentMaxStaminaBonus);

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[KitchenStaminaBonusController] 주방 Lv.{currentKitchenLevel} / 최대 스테미너 보너스 +{currentMaxStaminaBonus}",
                    this
                );
            }
        }

        public int GetMaxStaminaBonus()
        {
            RefreshBonus();
            return currentMaxStaminaBonus;
        }

        public int GetMaxStaminaBonusForLevel(int kitchenLevel)
        {
            return CalculateMaxStaminaBonus(kitchenLevel);
        }

        public void SetOfflineTestLevel(int level)
        {
            useOfflineTestLevel = true;
            offlineTestLevel = Mathf.Clamp(level, 1, 4);
            RefreshBonus();
        }

        public void ClearOfflineTestLevel()
        {
            useOfflineTestLevel = false;
            RefreshBonus();
        }

        private int GetCurrentKitchenLevel()
        {
            if (useOfflineTestLevel)
                return Mathf.Clamp(offlineTestLevel, 1, 4);

            if (kitchenFacility == null)
                return 1;

            return Mathf.Max(1, kitchenFacility.CurrentLevel.Value);
        }

        private int CalculateMaxStaminaBonus(int kitchenLevel)
        {
            if (kitchenLevel < bonusStartLevel)
                return 0;

            int bonusLevelCount = kitchenLevel - bonusStartLevel + 1;
            return bonusLevelCount * staminaBonusPerLevel;
        }

        private bool IsValidKitchenFacility()
        {
            if (kitchenFacility == null)
            {
                Debug.LogWarning("[KitchenStaminaBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (kitchenFacility.Type != FacilityType.Kitchen)
            {
                Debug.LogWarning(
                    $"[KitchenStaminaBonusController] 연결된 시설 타입이 Kitchen이 아닙니다. 현재 타입: {kitchenFacility.Type}",
                    this
                );
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("스테미너 보너스 다시 계산")]
        private void DebugRefreshBonus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[KitchenStaminaBonusController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            RefreshBonus();
        }

        [ContextMenu("오프라인 테스트 레벨 해제")]
        private void DebugClearOfflineTestLevel()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[KitchenStaminaBonusController] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            ClearOfflineTestLevel();
        }
#endif
    }
}
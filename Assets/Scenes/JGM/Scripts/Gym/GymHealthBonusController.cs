using System;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 헬스장 시설 레벨에 따른 최대 체력 보너스를 계산합니다.
    /// 현재 프로젝트 구조상 Gym 컴포넌트를 직접 요구하지 않고,
    /// FacilityBase와 FacilityType.Gym 기준으로 헬스장 시설인지 확인합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityBase))]
    public class GymHealthBonusController : MonoBehaviour
    {
        [Header("헬스장 시설")]
        [SerializeField]
        [Tooltip("체력 보너스를 계산할 헬스장 시설입니다. 비워두면 같은 오브젝트의 FacilityBase를 자동으로 찾습니다.")]
        private FacilityBase gymFacility;

        [Header("체력 보너스 설정")]
        [SerializeField]
        [Min(2)]
        [Tooltip("체력 보너스가 시작되는 헬스장 레벨입니다. 기본값은 Lv2입니다.")]
        private int bonusStartLevel = 2;

        [SerializeField]
        [Min(0)]
        [Tooltip("헬스장 레벨이 1 증가할 때마다 늘어나는 최대 체력 보너스입니다.")]
        private int healthBonusPerLevel = 5;

        [Header("로그")]
        [SerializeField]
        [Tooltip("헬스장 체력 보너스 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logBonusChanged = true;

        private int currentGymLevel = 1;
        private int currentMaxHealthBonus;

        public int CurrentGymLevel => currentGymLevel;
        public int CurrentMaxHealthBonus => currentMaxHealthBonus;

        public event Action<int, int> OnHealthBonusChanged;

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

            if (healthBonusPerLevel < 0)
                healthBonusPerLevel = 0;

            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (gymFacility == null)
                gymFacility = GetComponent<FacilityBase>();
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (gymFacility == null)
                return;

            gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            gymFacility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            if (gymFacility == null)
                return;

            gymFacility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
        }

        private void HandleFacilityLevelChanged(int previousLevel, int newLevel)
        {
            RefreshBonus();
        }

        public void RefreshBonus()
        {
            if (!IsValidGymFacility())
                return;

            int previousBonus = currentMaxHealthBonus;

            currentGymLevel = GetCurrentGymLevel();
            currentMaxHealthBonus = CalculateMaxHealthBonus(currentGymLevel);

            if (previousBonus == currentMaxHealthBonus)
                return;

            OnHealthBonusChanged?.Invoke(currentGymLevel, currentMaxHealthBonus);

            if (logBonusChanged)
            {
                Debug.Log(
                    $"[GymHealthBonusController] 헬스장 Lv.{currentGymLevel} / 최대 체력 보너스 +{currentMaxHealthBonus}",
                    this
                );
            }
        }

        public int GetMaxHealthBonus()
        {
            RefreshBonus();
            return currentMaxHealthBonus;
        }

        public int GetMaxHealthBonusForLevel(int gymLevel)
        {
            return CalculateMaxHealthBonus(gymLevel);
        }

        private int GetCurrentGymLevel()
        {
            if (gymFacility == null)
                return 1;

            return Mathf.Max(1, gymFacility.CurrentLevel.Value);
        }

        private int CalculateMaxHealthBonus(int gymLevel)
        {
            if (gymLevel < bonusStartLevel)
                return 0;

            int bonusLevelCount = gymLevel - bonusStartLevel + 1;
            return bonusLevelCount * healthBonusPerLevel;
        }

        private bool IsValidGymFacility()
        {
            if (gymFacility == null)
            {
                Debug.LogWarning("[GymHealthBonusController] FacilityBase가 연결되어 있지 않습니다.", this);
                return false;
            }

            if (gymFacility.Type != FacilityType.Gym)
            {
                Debug.LogWarning(
                    $"[GymHealthBonusController] 연결된 시설 타입이 Gym이 아닙니다. 현재 타입: {gymFacility.Type}",
                    this
                );
                return false;
            }

            return true;
        }
    }
}
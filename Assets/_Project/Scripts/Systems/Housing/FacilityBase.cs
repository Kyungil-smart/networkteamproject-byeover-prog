using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 하우징 시설의 공통 기반 클래스입니다.
    /// 시설 레벨 상태, 레벨 변경 이벤트, 현재 레벨 데이터 조회를 담당합니다.
    /// 실제 업그레이드 재료 검사와 소모는 시설별 UpgradeController가 담당합니다.
    /// </summary>
    public abstract class FacilityBase : NetworkBehaviour
    {
        [Header("시설 데이터")]
        [SerializeField]
        [Tooltip("시설 타입, 레벨별 업그레이드 재료, 효과 설명을 가진 FacilityDataSO입니다.")]
        protected FacilityDataSO facilityData;

        [SerializeField]
        [Tooltip("플레이어가 시설과 상호작용할 수 있는 거리입니다.")]
        protected float interactDistance = 2.5f;

        public NetworkVariable<int> CurrentLevel = new(1);

        public FacilityType Type => facilityData != null ? facilityData.type : default;
        public float InteractDistance => interactDistance;
        public FacilityDataSO FacilityData => facilityData;

        public override void OnNetworkSpawn()
        {
            CurrentLevel.OnValueChanged += HandleLevelChanged;

            // 서버에서 현재 레벨 효과를 한 번 초기화합니다.
            if (IsServer)
            {
                HandleLevelChanged(0, CurrentLevel.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentLevel.OnValueChanged -= HandleLevelChanged;
        }

        private void HandleLevelChanged(int oldLevel, int newLevel)
        {
            OnLevelChanged(newLevel);

            if (!IsServer)
                return;

            EventBus.Publish(new FacilityUpgradedEvent
            {
                facilityType = Type,
                newLevel = newLevel
            });
        }

        protected abstract void OnLevelChanged(int newLevel);

        public bool CanSetLevel(int newLevel)
        {
            if (facilityData == null)
                return false;

            if (facilityData.levels == null || facilityData.levels.Length == 0)
                return false;

            return newLevel >= 1 && newLevel <= facilityData.levels.Length;
        }

        public bool TrySetLevelFromServer(int newLevel)
        {
            if (!IsServer)
                return false;

            if (!CanSetLevel(newLevel))
                return false;

            CurrentLevel.Value = newLevel;
            return true;
        }

        public FacilityLevel GetCurrentLevelData()
        {
            if (facilityData == null)
                return null;

            return facilityData.GetLevel(CurrentLevel.Value);
        }

        public FacilityLevel GetNextLevelData()
        {
            if (facilityData == null)
                return null;

            int nextLevel = CurrentLevel.Value + 1;

            if (!CanSetLevel(nextLevel))
                return null;

            return facilityData.GetLevel(nextLevel);
        }

        public bool IsMaxLevel()
        {
            if (facilityData == null || facilityData.levels == null)
                return true;

            return CurrentLevel.Value >= facilityData.levels.Length;
        }
    }
}
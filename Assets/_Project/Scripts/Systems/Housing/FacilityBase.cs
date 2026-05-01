using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 하우징 시설의 추상 베이스입니다.
    /// 공통 레벨 상태, 업그레이드 요청, 시설 업그레이드 이벤트 발행을 담당합니다.
    /// 개별 시설 효과는 서브클래스 또는 별도 시설 전용 컴포넌트에서 처리합니다.
    /// </summary>
    public abstract class FacilityBase : NetworkBehaviour
    {
        [Header("Facility")]
        [SerializeField] protected FacilityDataSO facilityData;
        [SerializeField] protected float interactDistance = 2.5f;

        public NetworkVariable<int> CurrentLevel = new(1);

        public FacilityType Type => facilityData != null ? facilityData.type : default;

        public override void OnNetworkSpawn()
        {
            CurrentLevel.OnValueChanged += HandleLevelChanged;

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
            {
                return;
            }

            EventBus.Publish(new FacilityUpgradedEvent
            {
                facilityType = Type,
                newLevel = newLevel,
            });
        }

        protected abstract void OnLevelChanged(int newLevel);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TryUpgradeServerRpc()
        {
            if (facilityData == null)
            {
                return;
            }

            int nextLevel = CurrentLevel.Value + 1;

            if (nextLevel > facilityData.levels.Length)
            {
                return;
            }

            CurrentLevel.Value = nextLevel;
        }

        public FacilityLevel GetCurrentLevelData()
        {
            return facilityData != null ? facilityData.GetLevel(CurrentLevel.Value) : null;
        }
    }
}
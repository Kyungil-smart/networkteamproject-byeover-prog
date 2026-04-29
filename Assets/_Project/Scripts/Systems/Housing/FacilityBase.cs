using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 6개 시설 타입의 추상 베이스.
    /// 구체 서브클래스(Workbench, CommStation, Gym, Stash, Kitchen, Bed)가 OnLevelChanged를 오버라이드한다.
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
            if (IsServer) HandleLevelChanged(0, CurrentLevel.Value);
        }

        public override void OnNetworkDespawn()
        {
            CurrentLevel.OnValueChanged -= HandleLevelChanged;
        }

        private void HandleLevelChanged(int oldLv, int newLv)
        {
            OnLevelChanged(newLv);
            if (IsServer)
            {
                EventBus.Publish(new FacilityUpgradedEvent
                {
                    facilityType = Type,
                    newLevel = newLv,
                });
            }
        }

        protected abstract void OnLevelChanged(int newLevel);

        [ServerRpc(RequireOwnership = false)]
        public void TryUpgradeServerRpc()
        {
            if (facilityData == null) return;
            int next = CurrentLevel.Value + 1;
            if (next > facilityData.levels.Length) return;
            CurrentLevel.Value = next;
        }

        public FacilityLevel GetCurrentLevelData()
        {
            return facilityData != null ? facilityData.GetLevel(CurrentLevel.Value) : null;
        }
    }
}

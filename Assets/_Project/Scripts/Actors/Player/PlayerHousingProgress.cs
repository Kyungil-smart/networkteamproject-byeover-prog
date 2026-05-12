using System;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Save;

namespace DeadZone.Actors
{
    [DisallowMultipleComponent]
    public sealed class PlayerHousingProgress : NetworkBehaviour
    {
        private const int MinLevel = 1;
        private const int MaxLevel = 4;

        [Header("플레이어별 시설 레벨")]
        public NetworkVariable<int> WorkbenchLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MedicalLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> GymLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> StashLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> KitchenLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> BedLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CommStationLevel = new(MinLevel, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        [Header("로그")]
        [SerializeField]
        private bool logLevelChanged = true;

        public event Action<FacilityType, int, int> FacilityLevelChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            WorkbenchLevel.OnValueChanged += HandleWorkbenchLevelChanged;
            MedicalLevel.OnValueChanged += HandleMedicalLevelChanged;
            GymLevel.OnValueChanged += HandleGymLevelChanged;
            StashLevel.OnValueChanged += HandleStashLevelChanged;
            KitchenLevel.OnValueChanged += HandleKitchenLevelChanged;
            BedLevel.OnValueChanged += HandleBedLevelChanged;
            CommStationLevel.OnValueChanged += HandleCommStationLevelChanged;
        }

        public override void OnNetworkDespawn()
        {
            WorkbenchLevel.OnValueChanged -= HandleWorkbenchLevelChanged;
            MedicalLevel.OnValueChanged -= HandleMedicalLevelChanged;
            GymLevel.OnValueChanged -= HandleGymLevelChanged;
            StashLevel.OnValueChanged -= HandleStashLevelChanged;
            KitchenLevel.OnValueChanged -= HandleKitchenLevelChanged;
            BedLevel.OnValueChanged -= HandleBedLevelChanged;
            CommStationLevel.OnValueChanged -= HandleCommStationLevelChanged;

            base.OnNetworkDespawn();
        }

        public int GetLevel(FacilityType facilityType)
        {
            return facilityType switch
            {
                FacilityType.Workbench => WorkbenchLevel.Value,
                FacilityType.Medical => MedicalLevel.Value,
                FacilityType.Gym => GymLevel.Value,
                FacilityType.Stash => StashLevel.Value,
                FacilityType.Kitchen => KitchenLevel.Value,
                FacilityType.Bed => BedLevel.Value,
                FacilityType.CommStation => CommStationLevel.Value,
                _ => MinLevel
            };
        }

        public bool CanUpgrade(FacilityType facilityType)
        {
            return GetLevel(facilityType) < MaxLevel;
        }

        public bool TrySetLevelFromServer(FacilityType facilityType, int level)
        {
            if (!IsServer)
                return false;

            int safeLevel = Mathf.Clamp(level, MinLevel, MaxLevel);

            switch (facilityType)
            {
                case FacilityType.Workbench:
                    WorkbenchLevel.Value = safeLevel;
                    return true;

                case FacilityType.Medical:
                    MedicalLevel.Value = safeLevel;
                    return true;

                case FacilityType.Gym:
                    GymLevel.Value = safeLevel;
                    return true;

                case FacilityType.Stash:
                    StashLevel.Value = safeLevel;
                    return true;

                case FacilityType.Kitchen:
                    KitchenLevel.Value = safeLevel;
                    return true;

                case FacilityType.Bed:
                    BedLevel.Value = safeLevel;
                    return true;

                case FacilityType.CommStation:
                    CommStationLevel.Value = safeLevel;
                    return true;

                default:
                    return false;
            }
        }

        public bool TryUpgradeFromServer(FacilityType facilityType)
        {
            if (!IsServer)
                return false;

            int currentLevel = GetLevel(facilityType);

            if (currentLevel >= MaxLevel)
                return false;

            return TrySetLevelFromServer(facilityType, currentLevel + 1);
        }

        public void ApplySaveDataFromServer(PlayerHousingProgressDTO dto)
        {
            if (!IsServer)
                return;

            if (dto == null)
                dto = new PlayerHousingProgressDTO();

            dto.Normalize();

            WorkbenchLevel.Value = dto.workbenchLevel;
            MedicalLevel.Value = dto.medicalLevel;
            GymLevel.Value = dto.gymLevel;
            StashLevel.Value = dto.stashLevel;
            KitchenLevel.Value = dto.kitchenLevel;
            BedLevel.Value = dto.bedLevel;
            CommStationLevel.Value = dto.commStationLevel;
        }

        public PlayerHousingProgressDTO ToSaveData()
        {
            PlayerHousingProgressDTO dto = new PlayerHousingProgressDTO
            {
                workbenchLevel = WorkbenchLevel.Value,
                medicalLevel = MedicalLevel.Value,
                gymLevel = GymLevel.Value,
                stashLevel = StashLevel.Value,
                kitchenLevel = KitchenLevel.Value,
                bedLevel = BedLevel.Value,
                commStationLevel = CommStationLevel.Value
            };

            dto.Normalize();
            return dto;
        }

        private void HandleWorkbenchLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Workbench, oldLevel, newLevel);
        private void HandleMedicalLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Medical, oldLevel, newLevel);
        private void HandleGymLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Gym, oldLevel, newLevel);
        private void HandleStashLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Stash, oldLevel, newLevel);
        private void HandleKitchenLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Kitchen, oldLevel, newLevel);
        private void HandleBedLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.Bed, oldLevel, newLevel);
        private void HandleCommStationLevelChanged(int oldLevel, int newLevel) => LogLevelChanged(FacilityType.CommStation, oldLevel, newLevel);

        private void LogLevelChanged(FacilityType facilityType, int oldLevel, int newLevel)
        {
            FacilityLevelChanged?.Invoke(facilityType, oldLevel, newLevel);

            if (!logLevelChanged)
                return;

            Debug.Log(
                $"[PlayerHousingProgress] 플레이어별 시설 레벨 변경\n" +
                $"플레이어 ClientId: {OwnerClientId}\n" +
                $"시설: {facilityType}\n" +
                $"레벨: Lv.{oldLevel} → Lv.{newLevel}",
                this
            );
        }
    }
}
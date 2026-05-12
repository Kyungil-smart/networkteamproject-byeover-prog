using System.Threading.Tasks;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Save;

namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어별 하우징 진행도를 Cloud Save와 NetworkVariable 사이에서 동기화합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHousingProgress))]
    public sealed class PlayerHousingSaveSyncer : NetworkBehaviour
    {
        [Header("저장 옵션")]
        [SerializeField]
        private bool saveToCloud = true;

        [Header("로그")]
        [SerializeField]
        private bool logSaveRequest = true;

        private PlayerHousingProgress progress;

        private void Awake()
        {
            progress = GetComponent<PlayerHousingProgress>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
                return;

            EventBus.Subscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
            TryApplyLoadedCloudDataToServer("PlayerHousingSaveSyncer spawned");
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                EventBus.Unsubscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// 서버에서 현재 플레이어 하우징 레벨을 소유 클라이언트의 Cloud Save에 저장하도록 요청합니다.
        /// </summary>
        public void RequestSaveFromServer(string saveReason)
        {
            if (!IsServer)
                return;

            if (progress == null)
                progress = GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] PlayerHousingProgress가 없습니다.", this);
                return;
            }

            PlayerHousingProgressDTO dto = progress.ToSaveData();

            ReceiveHousingSaveRequestRpc(
                dto.workbenchLevel,
                dto.medicalLevel,
                dto.gymLevel,
                dto.stashLevel,
                dto.kitchenLevel,
                dto.bedLevel,
                dto.commStationLevel,
                saveReason,
                RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp)
            );
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveHousingSaveRequestRpc(
            int workbenchLevel,
            int medicalLevel,
            int gymLevel,
            int stashLevel,
            int kitchenLevel,
            int bedLevel,
            int commStationLevel,
            string saveReason,
            RpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            PlayerHousingProgressDTO dto = new PlayerHousingProgressDTO
            {
                workbenchLevel = workbenchLevel,
                medicalLevel = medicalLevel,
                gymLevel = gymLevel,
                stashLevel = stashLevel,
                kitchenLevel = kitchenLevel,
                bedLevel = bedLevel,
                commStationLevel = commStationLevel
            };

            dto.Normalize();
            ApplyHousingStateToLobbySave(dto, saveReason);

            if (saveToCloud)
                _ = SaveHousingProgressToCloudAsync(dto, saveReason);
        }

        private void ApplyHousingStateToLobbySave(PlayerHousingProgressDTO dto, string saveReason)
        {
            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] LobbyFacilityState를 찾지 못했습니다. PersistentSystems 또는 Save 오브젝트 설정을 확인하세요.", this);
                return;
            }

            facilityState.SetFacilityLevel("Workbench", dto.workbenchLevel);
            facilityState.SetFacilityLevel("Medical", dto.medicalLevel);
            facilityState.SetFacilityLevel("Gym", dto.gymLevel);
            facilityState.SetFacilityLevel("Stash", dto.stashLevel);
            facilityState.SetFacilityLevel("Kitchen", dto.kitchenLevel);
            facilityState.SetFacilityLevel("Bed", dto.bedLevel);
            facilityState.SetFacilityLevel("CommStation", dto.commStationLevel);

            if (!logSaveRequest)
                return;

            Debug.Log(
                $"[PlayerHousingSaveSyncer] 플레이어별 시설 레벨 저장 상태 반영 완료\n" +
                $"사유: {saveReason}\n" +
                $"Workbench Lv.{dto.workbenchLevel}, Medical Lv.{dto.medicalLevel}, Gym Lv.{dto.gymLevel}, " +
                $"Stash Lv.{dto.stashLevel}, Kitchen Lv.{dto.kitchenLevel}, Bed Lv.{dto.bedLevel}, CommStation Lv.{dto.commStationLevel}",
                this
            );
        }

        private async Task SaveHousingProgressToCloudAsync(PlayerHousingProgressDTO dto, string saveReason)
        {
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);

            if (cloudSaveSystem == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] CloudSaveSystem을 찾지 못했습니다. Housing 저장을 건너뜁니다.", this);
                return;
            }

            bool success = await cloudSaveSystem.SaveHousingProgressAsync(dto);

            if (!logSaveRequest)
                return;

            Debug.Log(
                success
                    ? $"[PlayerHousingSaveSyncer] Housing Cloud Save 저장 완료. 사유: {saveReason}"
                    : $"[PlayerHousingSaveSyncer] Housing Cloud Save 저장 실패. 사유: {saveReason}",
                this
            );
        }

        private void HandleCloudSaveLoaded(CloudSaveLoadedEvent e)
        {
            if (!IsOwner)
                return;

            TryApplyLoadedCloudDataToServer("Cloud Save loaded");
        }

        private void TryApplyLoadedCloudDataToServer(string reason)
        {
            if (!IsOwner)
                return;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);

            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                return;

            PlayerHousingProgressDTO dto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();

            if (dto == null)
                return;

            ApplyHousingStateToLobbySave(dto, reason);

            ApplyHousingSaveDataRpc(
                dto.workbenchLevel,
                dto.medicalLevel,
                dto.gymLevel,
                dto.stashLevel,
                dto.kitchenLevel,
                dto.bedLevel,
                dto.commStationLevel,
                reason);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ApplyHousingSaveDataRpc(
            int workbenchLevel,
            int medicalLevel,
            int gymLevel,
            int stashLevel,
            int kitchenLevel,
            int bedLevel,
            int commStationLevel,
            string reason,
            RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[PlayerHousingSaveSyncer] 소유자가 아닌 클라이언트의 하우징 복원 요청을 거부했습니다. Sender={rpcParams.Receive.SenderClientId}, Owner={OwnerClientId}",
                    this);
                return;
            }

            if (progress == null)
                progress = GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] PlayerHousingProgress가 없어 하우징 저장 데이터를 적용할 수 없습니다.", this);
                return;
            }

            PlayerHousingProgressDTO dto = new PlayerHousingProgressDTO
            {
                workbenchLevel = workbenchLevel,
                medicalLevel = medicalLevel,
                gymLevel = gymLevel,
                stashLevel = stashLevel,
                kitchenLevel = kitchenLevel,
                bedLevel = bedLevel,
                commStationLevel = commStationLevel
            };

            dto.Normalize();
            progress.ApplySaveDataFromServer(dto);
            ApplyHousingStateToLobbySave(dto, reason);

            if (!logSaveRequest)
                return;

            Debug.Log(
                $"[PlayerHousingSaveSyncer] Cloud Save 하우징 데이터를 서버 PlayerHousingProgress에 적용했습니다. 사유: {reason}",
                this);
        }

        private static CloudSaveSystem ResolveCloudSaveSystem(bool preferLoadedData)
        {
            CloudSaveSystem service = ServiceLocator.Get<CloudSaveSystem>();

            if (!preferLoadedData)
                return service != null
                    ? service
                    : UnityEngine.Object.FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);

            if (service != null && service.HasLoadedData)
                return service;

            CloudSaveSystem[] systems = UnityEngine.Object.FindObjectsByType<CloudSaveSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].HasLoadedData)
                    return systems[i];
            }

            if (service != null)
                return service;

            return systems != null && systems.Length > 0 ? systems[0] : null;
        }
    }
}

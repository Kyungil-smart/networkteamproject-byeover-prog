using System.Threading.Tasks;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Network;
using DeadZone.Systems.Save;

namespace DeadZone.Actors
{
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
                _ = SaveLobbyDataToCloudAsync(saveReason);
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

        private async Task SaveLobbyDataToCloudAsync(string saveReason)
        {
            FirebaseAnonymousLoginSystem loginSystem =
                FindFirstObjectByType<FirebaseAnonymousLoginSystem>(FindObjectsInactive.Include);

            if (loginSystem == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] FirebaseAnonymousLoginSystem을 찾지 못했습니다. Cloud Save 저장을 건너뜁니다.", this);
                return;
            }

            bool signedIn = await loginSystem.EnsureSignedInAsync();

            if (!signedIn)
            {
                Debug.LogWarning($"[PlayerHousingSaveSyncer] Firebase 로그인 실패로 저장을 건너뜁니다. 사유: {saveReason}", this);
                return;
            }

            LobbySaveService saveService = FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);

            if (saveService == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] LobbySaveService를 찾지 못했습니다. Cloud Save 저장을 건너뜁니다.", this);
                return;
            }

            bool success = await saveService.SaveLobbyDataToCloudAsync();

            if (!logSaveRequest)
                return;

            Debug.Log(
                success
                    ? $"[PlayerHousingSaveSyncer] Cloud Save 저장 완료. 사유: {saveReason}"
                    : $"[PlayerHousingSaveSyncer] Cloud Save 저장 실패. 사유: {saveReason}",
                this
            );
        }
    }
}
using System.Threading.Tasks;
using System.Collections.Generic;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems;
using DeadZone.Systems.Housing;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const int DebugMinHousingLevel = 1;
        private const int DebugMaxHousingLevel = 4;
#endif

        [Header("저장 옵션")]
        [SerializeField]
        private bool saveToCloud = true;

        [Header("로그")]
        [SerializeField]
        private bool logSaveRequest = true;

        [SerializeField]
        [Tooltip("서버가 해당 플레이어의 Cloud Save 원본을 직접 가지고 있지 않을 때, 소유 클라이언트가 보낸 로드 데이터를 임시 호환 경로로 허용할지 여부입니다.")]
        private bool allowOwnerProvidedLoadDataWhenServerCloudDataMissing = true;

        private PlayerHousingProgress progress;
        private string lastAppliedLoadSignature;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Dictionary<string, DebugCraftMaterialGrant> debugCraftMaterialGrants = new();

        private sealed class DebugCraftMaterialGrant
        {
            public ItemDataSO item;
            public int baselineCount;
            public int grantedCount;
        }
#endif

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
            EventBus.Subscribe<SceneChangedEvent>(HandleSceneChanged);
            SceneManager.sceneLoaded += HandleUnitySceneLoaded;
            TryApplyLoadedDataForCurrentScene("PlayerHousingSaveSyncer spawned");
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                EventBus.Unsubscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
                EventBus.Unsubscribe<SceneChangedEvent>(HandleSceneChanged);
                SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
            }

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// 서버의 현재 플레이어 하우징 진행도를 소유 클라이언트의 Cloud Save에 저장하도록 요청합니다.
        /// </summary>
        public void RequestSaveFromServer(string saveReason)
        {
            if (!IsServer)
                return;

            Debug.Log($"[Save] Save requested. reason={saveReason}", this);

            if (progress == null)
                progress = GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] PlayerHousingProgress가 없습니다.", this);
                return;
            }

            PlayerHousingProgressDTO dto = progress.ToSaveData();

            if (IsOwner)
            {
                ApplyHousingStateToLobbySave(dto, saveReason);

                if (saveToCloud)
                    _ = SaveHousingProgressToCloudAsync(dto, saveReason);

                return;
            }

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

        // 서버 제작 성공 후 오너 클라이언트가 로비 인벤토리 스냅샷을 Cloud Save에 올리도록 요청합니다.
        public void RequestLobbyInventorySaveFromServer(string saveReason)
        {
            if (!IsServer)
                return;

            if (IsOwner)
            {
                _ = SaveLobbyInventorySnapshotAsync(saveReason);
                return;
            }

            RequestLobbyInventorySaveRpc(saveReason, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void RequestLobbyInventorySaveRpc(string saveReason, RpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            _ = SaveLobbyInventorySnapshotAsync(saveReason);
        }

        private async Task SaveLobbyInventorySnapshotAsync(string saveReason)
        {
            LobbySaveService saveService = FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);

            if (saveService == null)
            {
                Debug.LogWarning(
                    $"[PlayerHousingSaveSyncer] LobbySaveService missing. Inventory snapshot save skipped. reason={saveReason}",
                    this);
                return;
            }

            bool success = await saveService.SaveLobbyDataToCloudAsync();

            if (!logSaveRequest)
                return;

            Debug.Log(
                success
                    ? $"[PlayerHousingSaveSyncer] Lobby inventory snapshot saved. reason={saveReason}"
                    : $"[PlayerHousingSaveSyncer] Lobby inventory snapshot save failed or skipped. reason={saveReason}",
                this);
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
                if (!IsLobbyOrHideoutScene(SceneManager.GetActiveScene().name))
                    return;

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

            EventBus.Publish(new HousingSaveResultEvent
            {
                success = success,
                reason = success
                    ? $"Housing Cloud Save 저장 완료. 사유: {saveReason}"
                    : $"Housing Cloud Save 저장 실패. 사유: {saveReason}"
            });

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

        private void HandleSceneChanged(SceneChangedEvent e)
        {
            string sceneName = e.sceneName.ToString();

            if (!IsLobbyOrHideoutScene(sceneName) && !IsGameplayScene(sceneName))
                return;

            TryApplyLoadedCloudDataToServer($"{sceneName} network scene loaded");
        }

        private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsOwner)
                return;

            if (!IsLobbyOrHideoutScene(scene.name) && !IsGameplayScene(scene.name))
                return;

            TryApplyLoadedCloudDataToServer($"{scene.name} unity scene loaded");
        }

        // 스폰 시점에 이미 현재 씬이 로비/하이드아웃/게임이면 저장된 하우징 레벨을 즉시 적용합니다.
        private void TryApplyLoadedDataForCurrentScene(string reason)
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!IsLobbyOrHideoutScene(activeScene.name) && !IsGameplayScene(activeScene.name))
                return;

            TryApplyLoadedCloudDataToServer(reason);
        }

        private void TryApplyLoadedCloudDataToServer(string reason)
        {
            if (!IsOwner)
                return;

            bool isInParty = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            string userId = GetCurrentUserId();
            bool serverSaveExists = HasLoadedServerHousingData();
            bool localJsonExists = HasLocalJsonSave();

            string sceneName = SceneManager.GetActiveScene().name;
            Debug.Log($"[HousingLoad] Apply requested. scene={sceneName}, isInParty={isInParty}, userId={userId}", this);
            Debug.Log($"[HousingLoad] Server save exists={serverSaveExists}", this);
            Debug.Log($"[HousingLoad] Local json exists={localJsonExists}", this);

            if (!TryCreateLoadedHousingProgressDTO(out PlayerHousingProgressDTO dto, out string source))
            {
                dto = new PlayerHousingProgressDTO();
                dto.Normalize();
                source = "Default";
                Debug.Log("[Facility] Default level generated. reason=No server or local facility save data", this);
            }

            Debug.Log($"[HousingLoad] Applying facility levels from={source}", this);

            if (dto == null)
                return;

            string loadSignature = BuildLoadSignature(dto, source);
            if (lastAppliedLoadSignature == loadSignature)
            {
                Debug.Log($"[HousingLoad] Same housing data already applied. source={source}", this);
                return;
            }

            lastAppliedLoadSignature = loadSignature;
            ApplyHousingStateToLobbySave(dto, reason);

            if (IsServer)
            {
                ApplyHousingSaveDataOnServer(dto, reason);
                return;
            }

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

            if (IsOwner && TryCreateServerAuthorityHousingProgressDTO(out PlayerHousingProgressDTO serverDto, out string serverSource))
            {
                ApplyHousingSaveDataOnServer(serverDto, $"{reason} / {serverSource}");
                ApplyHousingStateToLobbySave(serverDto, reason);
                return;
            }

            if (!allowOwnerProvidedLoadDataWhenServerCloudDataMissing)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] 서버에 Cloud Save 원본이 없어 클라이언트 제공 하우징 데이터를 거부했습니다.", this);
                return;
            }

            Debug.LogWarning("[PlayerHousingSaveSyncer] 서버 Cloud Save 원본이 없어 소유 클라이언트가 보낸 하우징 로드 데이터를 호환 경로로 적용합니다.", this);

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
            ApplyHousingSaveDataOnServer(dto, reason);
            ApplyHousingStateToLobbySave(dto, reason);

            if (!logSaveRequest)
                return;

            Debug.Log(
                $"[PlayerHousingSaveSyncer] Cloud Save 하우징 데이터를 서버 PlayerHousingProgress에 적용했습니다. 사유: {reason}",
                this);
        }

        private bool TryCreateLoadedHousingProgressDTO(out PlayerHousingProgressDTO dto, out string source)
        {
            dto = null;
            source = string.Empty;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);

            if (cloudSaveSystem != null && cloudSaveSystem.HasLoadedData)
            {
                dto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();

                if (dto != null)
                {
                    dto.Normalize();
                    source = "Server";
                    return true;
                }
            }

            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null || facilityState.Facilities == null || facilityState.Facilities.Count == 0)
                return false;

            dto = new PlayerHousingProgressDTO();

            for (int i = 0; i < facilityState.Facilities.Count; i++)
                ApplyFacilityStateToDTO(dto, facilityState.Facilities[i]);

            dto.Normalize();
            source = "LocalJson";
            return true;
        }

        private void ApplyHousingSaveDataOnServer(PlayerHousingProgressDTO dto, string reason)
        {
            if (!IsServer)
                return;

            if (progress == null)
                progress = GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] PlayerHousingProgress가 없어 하우징 저장 데이터를 적용할 수 없습니다.", this);
                return;
            }

            progress.ApplySaveDataFromServer(dto);
            // 씬 오브젝트 시설 레벨은 하이드아웃에서만 직접 갱신하고, 게임 씬에서는 플레이어 보너스 적용만 사용합니다.
            if (IsHideoutScene(SceneManager.GetActiveScene().name))
                ApplyToSceneFacilities(dto);
            RefreshOpenHideoutWindows();

            if (!logSaveRequest)
                return;

            Debug.Log(
                $"[PlayerHousingSaveSyncer] 하우징 저장 데이터를 서버 PlayerHousingProgress에 적용했습니다. 사유: {reason}",
                this);
        }

        private static void ApplyFacilityStateToDTO(PlayerHousingProgressDTO dto, FacilitySaveDTO facility)
        {
            if (dto == null || facility == null)
                return;

            int safeLevel = Mathf.Max(1, facility.level);

            switch (NormalizeFacilityId(facility.facilityId))
            {
                case "workbench": dto.workbenchLevel = Mathf.Max(dto.workbenchLevel, safeLevel); break;
                case "commstation": dto.commStationLevel = Mathf.Max(dto.commStationLevel, safeLevel); break;
                case "medical": dto.medicalLevel = Mathf.Max(dto.medicalLevel, safeLevel); break;
                case "gym": dto.gymLevel = Mathf.Max(dto.gymLevel, safeLevel); break;
                case "stash": dto.stashLevel = Mathf.Max(dto.stashLevel, safeLevel); break;
                case "kitchen": dto.kitchenLevel = Mathf.Max(dto.kitchenLevel, safeLevel); break;
                case "bed": dto.bedLevel = Mathf.Max(dto.bedLevel, safeLevel); break;
            }
        }

        private static void ApplyToSceneFacilities(PlayerHousingProgressDTO dto)
        {
            if (dto == null)
                return;

            FacilityBase[] facilities = FindObjectsByType<FacilityBase>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < facilities.Length; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                if (!CanWriteSceneFacilityLevel(facility))
                    continue;

                int previousLevel = facility.GetCurrentLevel();
                int loadedLevel = dto.GetLevel(facility.Type);
                int finalLevel = facility.CanSetLevel(loadedLevel)
                    ? loadedLevel
                    : previousLevel;

                if (facility.CanSetLevel(finalLevel))
                    facility.CurrentLevel.Value = finalLevel;

                Debug.Log(
                    $"[Facility] Apply level. type={facility.Type}, loadedLevel={loadedLevel}, previousLevel={previousLevel}, finalLevel={finalLevel}",
                    facility);
            }
        }

        private static bool CanWriteSceneFacilityLevel(FacilityBase facility)
        {
            if (facility == null)
                return false;

            NetworkManager networkManager = NetworkManager.Singleton;
            return facility.IsSpawned && networkManager != null && networkManager.IsServer;
        }

        private static void RefreshOpenHideoutWindows()
        {
            DeadZone.Actors.UI.Hideout.FacilityUpgradeWindowUI[] upgradeWindows =
                FindObjectsByType<DeadZone.Actors.UI.Hideout.FacilityUpgradeWindowUI>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (int i = 0; i < upgradeWindows.Length; i++)
            {
                if (upgradeWindows[i] != null && upgradeWindows[i].IsOpen)
                    upgradeWindows[i].Refresh();
            }

            DeadZone.Actors.UI.Hideout.FacilityCraftWindowUI[] craftWindows =
                FindObjectsByType<DeadZone.Actors.UI.Hideout.FacilityCraftWindowUI>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (int i = 0; i < craftWindows.Length; i++)
            {
                if (craftWindows[i] != null && craftWindows[i].IsOpen)
                    craftWindows[i].Refresh();
            }
        }

        private static bool TryCreateServerAuthorityHousingProgressDTO(out PlayerHousingProgressDTO dto, out string source)
        {
            dto = null;
            source = string.Empty;

            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
                return false;

            dto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();
            if (dto == null)
                return false;

            dto.Normalize();
            source = "ServerAuthorityCloudSave";
            return true;
        }

        private static string BuildLoadSignature(PlayerHousingProgressDTO dto, string source)
        {
            if (dto == null)
                return "null";

            return $"{dto.workbenchLevel}|{dto.medicalLevel}|{dto.gymLevel}|{dto.stashLevel}|{dto.kitchenLevel}|{dto.bedLevel}|{dto.commStationLevel}";
        }

        private static bool IsHideoutScene(string sceneName)
        {
            return string.Equals(sceneName, "Hideout", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sceneName, "HideOut", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLobbyOrHideoutScene(string sceneName)
        {
            return IsHideoutScene(sceneName) ||
                   string.Equals(sceneName, "Lobby", System.StringComparison.OrdinalIgnoreCase);
        }

        // 레이드 게임 씬에서도 하우징 보너스를 받을 수 있게 Game_ 접두 씬을 적용 대상으로 포함합니다.
        private static bool IsGameplayScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName) &&
                   sceneName.StartsWith("Game_", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCurrentUserId()
        {
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);

            if (cloudSaveSystem != null && !string.IsNullOrWhiteSpace(cloudSaveSystem.LoadedFirebaseUid))
                return cloudSaveSystem.LoadedFirebaseUid;

            return "unknown";
        }

        private static bool HasLoadedServerHousingData()
        {
            CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(true);
            return cloudSaveSystem != null && cloudSaveSystem.HasLoadedData;
        }

        private static bool HasLocalJsonSave()
        {
            LobbySaveService saveService = FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
            return saveService != null && saveService.LocalJsonExists();
        }

        private static string NormalizeFacilityId(string facilityId)
        {
            return string.IsNullOrWhiteSpace(facilityId)
                ? string.Empty
                : facilityId.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // F12 테스트 입력을 네트워크 플레이어 경로로 전달합니다. 실제 인벤토리 GridInventory가 있을 때 사용됩니다.
        public bool TryRunDebugCraftMaterialTest(bool removeMaterials)
        {
            if (!IsSpawned || !IsOwner)
                return false;

            RequestDebugCraftMaterialTestRpc(removeMaterials);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDebugCraftMaterialTestRpc(bool removeMaterials, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[PlayerHousingSaveSyncer] F12 제작 재료 테스트 요청을 거부했습니다. Sender={rpcParams.Receive.SenderClientId}, Owner={OwnerClientId}",
                    this);
                return;
            }

            GridInventory inventory = GetComponent<GridInventory>();
            if (inventory == null)
                inventory = GetComponentInChildren<GridInventory>(true);

            if (inventory == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] F12 제작 재료 테스트 실패. GridInventory가 없습니다.", this);
                return;
            }

            if (removeMaterials)
            {
                RemoveDebugCraftMaterials(inventory);
                return;
            }

            AddDebugCraftMaterials(inventory);
        }

        private void AddDebugCraftMaterials(GridInventory inventory)
        {
            if (inventory == null)
                return;

            // 작업대/의료시설의 모든 레시피 재료를 모아 한 번에 테스트 재료로 지급합니다.
            Dictionary<string, DebugCraftMaterialGrant> requestedMaterials = CollectDebugCraftMaterialRequirements();

            if (requestedMaterials.Count == 0)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] F12 제작 재료 테스트 실패. 작업대/의료시설 레시피 재료를 찾지 못했습니다.", this);
                return;
            }

            debugCraftMaterialGrants.Clear();

            int addedKinds = 0;
            int addedTotal = 0;

            foreach (DebugCraftMaterialGrant grant in requestedMaterials.Values)
            {
                if (grant == null || grant.item == null || grant.grantedCount <= 0)
                    continue;

                grant.baselineCount = inventory.GetItemCount(grant.item.itemID);

                if (!inventory.TryAddItem(grant.item, grant.grantedCount))
                {
                    Debug.LogWarning(
                        $"[PlayerHousingSaveSyncer] F12 제작 재료 추가 실패. item={grant.item.itemID}, amount={grant.grantedCount}",
                        this);
                    continue;
                }

                debugCraftMaterialGrants[grant.item.itemID] = grant;
                addedKinds++;
                addedTotal += grant.grantedCount;
            }

            Debug.Log(
                $"[PlayerHousingSaveSyncer] F12 제작 재료 추가 완료\n" +
                $"ClientId: {OwnerClientId}\n" +
                $"아이템 종류: {addedKinds}\n" +
                $"총 수량: {addedTotal}",
                this);
        }

        private void RemoveDebugCraftMaterials(GridInventory inventory)
        {
            if (inventory == null)
                return;

            if (debugCraftMaterialGrants.Count == 0)
            {
                Debug.Log("[PlayerHousingSaveSyncer] Shift+F12 제거할 테스트 제작 재료 기록이 없습니다.", this);
                return;
            }

            int removedKinds = 0;
            int removedTotal = 0;

            foreach (DebugCraftMaterialGrant grant in debugCraftMaterialGrants.Values)
            {
                if (grant == null || grant.item == null || string.IsNullOrWhiteSpace(grant.item.itemID))
                    continue;

                int currentCount = inventory.GetItemCount(grant.item.itemID);
                int removableCount = Mathf.Min(
                    Mathf.Max(0, grant.grantedCount),
                    Mathf.Max(0, currentCount - Mathf.Max(0, grant.baselineCount)));

                if (removableCount <= 0)
                    continue;

                if (!inventory.ConsumeItem(grant.item.itemID, removableCount))
                {
                    Debug.LogWarning(
                        $"[PlayerHousingSaveSyncer] Shift+F12 제작 재료 제거 실패. item={grant.item.itemID}, amount={removableCount}",
                        this);
                    continue;
                }

                removedKinds++;
                removedTotal += removableCount;
            }

            debugCraftMaterialGrants.Clear();

            Debug.Log(
                $"[PlayerHousingSaveSyncer] Shift+F12 제작 재료 제거 완료\n" +
                $"ClientId: {OwnerClientId}\n" +
                $"아이템 종류: {removedKinds}\n" +
                $"총 수량: {removedTotal}",
                this);
        }

        // 씬에 배치된 작업대/의료시설 제작 레시피를 훑어 필요한 재료 총량을 계산합니다.
        private static Dictionary<string, DebugCraftMaterialGrant> CollectDebugCraftMaterialRequirements()
        {
            Dictionary<string, DebugCraftMaterialGrant> materials = new();

            WorkbenchRecipeCatalog[] workbenchCatalogs = FindObjectsByType<WorkbenchRecipeCatalog>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < workbenchCatalogs.Length; i++)
            {
                if (workbenchCatalogs[i] == null)
                    continue;

                AddRecipeMaterials(materials, workbenchCatalogs[i].GetAllRecipes());
            }

            MedicalCraftingController[] medicalControllers = FindObjectsByType<MedicalCraftingController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < medicalControllers.Length; i++)
            {
                if (medicalControllers[i] == null)
                    continue;

                AddRecipeMaterials(materials, medicalControllers[i].GetAllRecipes());
            }

            return materials;
        }

        private static void AddRecipeMaterials(
            Dictionary<string, DebugCraftMaterialGrant> materials,
            IReadOnlyList<RecipeSO> recipes)
        {
            if (materials == null || recipes == null)
                return;

            for (int recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
            {
                RecipeSO recipe = recipes[recipeIndex];

                if (recipe == null || recipe.ingredients == null)
                    continue;

                for (int ingredientIndex = 0; ingredientIndex < recipe.ingredients.Count; ingredientIndex++)
                {
                    ItemRequirement ingredient = recipe.ingredients[ingredientIndex];

                    if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                        continue;

                    int amount = Mathf.Max(1, ingredient.amount);
                    string itemId = ingredient.item.itemID;

                    if (!materials.TryGetValue(itemId, out DebugCraftMaterialGrant grant))
                    {
                        grant = new DebugCraftMaterialGrant
                        {
                            item = ingredient.item,
                            grantedCount = 0
                        };

                        materials.Add(itemId, grant);
                    }

                    grant.grantedCount += amount;
                }
            }
        }

        public bool TryRunDebugHousingLevelTest(bool resetToLevelOne)
        {
            if (!IsSpawned || !IsOwner)
                return false;

            RequestEndKeyHousingTestRpc(resetToLevelOne);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestEndKeyHousingTestRpc(bool resetToLevelOne, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[PlayerHousingSaveSyncer] End 키 하우징 테스트 요청을 거부했습니다. Sender={rpcParams.Receive.SenderClientId}, Owner={OwnerClientId}",
                    this);
                return;
            }

            if (progress == null)
                progress = GetComponent<PlayerHousingProgress>();

            if (progress == null)
            {
                Debug.LogWarning("[PlayerHousingSaveSyncer] PlayerHousingProgress가 없어 End 키 하우징 테스트를 실행할 수 없습니다.", this);
                return;
            }

            PlayerHousingProgressDTO dto = progress.ToSaveData();

            if (resetToLevelOne)
                SetDebugHousingLevel(dto, DebugMinHousingLevel);
            else
                IncrementDebugHousingLevel(dto);

            dto.Normalize();
            string reason = resetToLevelOne ? "Shift+End 하우징 테스트 초기화" : "End 키 하우징 테스트 업그레이드";

            progress.ApplySaveDataFromServer(dto);
            ApplyHousingStateToLobbySave(dto, reason);
            RequestSaveFromServer(reason);

            if (!logSaveRequest)
                return;

            Debug.Log(
                resetToLevelOne
                    ? "[PlayerHousingSaveSyncer] Shift+End 테스트로 모든 하우징 시설을 Lv.1로 초기화했습니다."
                    : "[PlayerHousingSaveSyncer] End 테스트로 모든 하우징 시설을 한 단계 업그레이드했습니다.",
                this);
        }

        private static void IncrementDebugHousingLevel(PlayerHousingProgressDTO dto)
        {
            dto.workbenchLevel = Mathf.Clamp(dto.workbenchLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.medicalLevel = Mathf.Clamp(dto.medicalLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.gymLevel = Mathf.Clamp(dto.gymLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.stashLevel = Mathf.Clamp(dto.stashLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.kitchenLevel = Mathf.Clamp(dto.kitchenLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.bedLevel = Mathf.Clamp(dto.bedLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.commStationLevel = Mathf.Clamp(dto.commStationLevel + 1, DebugMinHousingLevel, DebugMaxHousingLevel);
        }

        private static void SetDebugHousingLevel(PlayerHousingProgressDTO dto, int level)
        {
            int safeLevel = Mathf.Clamp(level, DebugMinHousingLevel, DebugMaxHousingLevel);
            dto.workbenchLevel = safeLevel;
            dto.medicalLevel = safeLevel;
            dto.gymLevel = safeLevel;
            dto.stashLevel = safeLevel;
            dto.kitchenLevel = safeLevel;
            dto.bedLevel = safeLevel;
            dto.commStationLevel = safeLevel;
        }
#endif

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

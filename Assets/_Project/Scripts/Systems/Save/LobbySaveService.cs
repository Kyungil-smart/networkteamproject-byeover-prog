using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DeadZone.Core;
using DeadZone.Network;

using Sirenix.OdinInspector;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadZone.Systems.Save
{
    public class LobbySaveService : MonoBehaviour
    {
        [Header("Save Collectors")]
        [SerializeField] private InventorySaveCollector inventorySaveCollector;
        [SerializeField] private FacilitySaveCollector facilitySaveCollector;

        [Header("Save State")]
        [SerializeField] private LobbyInventoryState inventoryState;
        [SerializeField] private LobbyFacilityState facilityState;
        [SerializeField] private LobbyInventoryStateUiBridge inventoryStateUiBridge;

        [Header("Server Save")]
        [SerializeField] private CloudSaveSystem cloudSaveSystem;
        [SerializeField] private bool loadFromCloudOnStart = true;
        [SerializeField] private bool loadFromCloudOnCloudSaveLoaded = true;
        [SerializeField] private bool saveToCloudOnApplicationPause = true;
        [SerializeField] private bool saveToCloudOnApplicationQuit = true;
        [SerializeField] private bool useLocalJsonFallback = true;
        [SerializeField] private bool preferLocalJsonInventorySections = false;
        [SerializeField] private bool allowLifecycleAutoSave;

        [Header("Debug JSON")]
        [TextArea(8, 20)]
        [SerializeField] private string lastJson;

        private bool isCloudSaveRunning;
        private bool hasPendingCloudSaveRequest;
        private Coroutine pendingCloudLoadCoroutine;
        private bool isInitialLoadCompleted;
        private string lastLoadedLocalJsonPath;
        private bool CanUseLocalJsonFallback => useLocalJsonFallback && Application.isEditor;
        public bool IsInitialLoadCompleted => isInitialLoadCompleted;

        private void OnEnable()
        {
            EventBus.Subscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CloudSaveLoadedEvent>(HandleCloudSaveLoaded);
        }

        private void Start()
        {
            if (loadFromCloudOnStart)
                QueueLoadLobbyDataFromCloudAfterUiReady();
            else
                isInitialLoadCompleted = true;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!allowLifecycleAutoSave || !pauseStatus || !saveToCloudOnApplicationPause)
                return;

            if (!isInitialLoadCompleted)
            {
                Debug.LogWarning("[Save] Pause save skipped because load is not completed yet.", this);
                return;
            }

            SaveLobbyDataToLocalJson(CreateCurrentLobbySaveDTO(), "ApplicationPause backup");
        }

        private void OnApplicationQuit()
        {
            if (!allowLifecycleAutoSave || !saveToCloudOnApplicationQuit)
                return;

            if (!isInitialLoadCompleted)
            {
                Debug.LogWarning("[Save] Save skipped because load is not completed yet.", this);
                return;
            }

            SaveLobbyDataToLocalJson(CreateCurrentLobbySaveDTO(), "ApplicationQuit backup");
        }

        [Button("Print Lobby Save JSON")]
        public void SaveLobbyData()
        {
            LobbySaveDTO dto = CreateCurrentLobbySaveDTO();
            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            Debug.Log($"[LobbySaveService] Lobby Save JSON\n{json}", this);
            SaveLobbyDataToLocalJson(dto, "Manual JSON save");
        }

        public void SaveCurrentStateToLocalJson(string reason)
        {
            LobbySaveDTO dto = CreateCurrentStateSnapshotDTO();
            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            SaveLobbyDataToLocalJson(
                dto,
                string.IsNullOrWhiteSpace(reason) ? "Local snapshot" : reason);
        }

        [Button("Save Lobby To Firebase")]
        public async void SaveLobbyDataToCloud()
        {
            await SaveLobbyDataToCloudAsync();
        }

        public async Task<bool> SaveLobbyDataToCloudAsync()
        {
            if (isCloudSaveRunning)
            {
                hasPendingCloudSaveRequest = true;
                Debug.LogWarning("[LobbySaveService] Server save is already running. Queued one more save with the latest state.", this);
                return false;
            }

            isCloudSaveRunning = true;
            bool anySuccess = false;

            try
            {
                do
                {
                    hasPendingCloudSaveRequest = false;
                    bool success = await SaveLobbyDataToCloudOnceAsync();
                    anySuccess |= success;
                }
                while (hasPendingCloudSaveRequest);

                return anySuccess;
            }
            finally
            {
                isCloudSaveRunning = false;
            }
        }

        private async Task<bool> SaveLobbyDataToCloudOnceAsync()
        {
            Debug.Log("[Save] Save requested. reason=LobbySaveService.SaveLobbyDataToCloudAsync", this);

            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();

            if (!isInitialLoadCompleted && saveSystem != null && saveSystem.HasLoadedData)
                isInitialLoadCompleted = true;

            if (!isInitialLoadCompleted)
            {
                LobbySaveDTO pendingDto = CreateCurrentLobbySaveDTO();
                SaveLobbyDataToLocalJson(pendingDto, "Save requested before load completed fallback");
                Debug.LogWarning("[Save] Save skipped because load is not completed yet.", this);
                return false;
            }

            LobbySaveDTO dto = CreateCurrentLobbySaveDTO();
            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            Debug.Log($"[LobbySaveService] Server save request JSON\n{json}", this);

            if (WouldOverwriteExistingSaveWithEmptyState(dto, saveSystem))
            {
                Debug.LogWarning("[Save] Save skipped because collected lobby inventory/stash/equipment is empty and would overwrite existing save.", this);
                return false;
            }

            if (saveSystem == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem missing. Cloud save was not written.", this);
                SaveLobbyDataToLocalJson(dto, "CloudSaveSystem missing");
                return false;
            }

            bool success = await saveSystem.SaveLobbyDataAsync(dto);
            Debug.Log(success
                ? "[LobbySaveService] Server save success"
                : "[LobbySaveService] Server save failed", this);

            SaveLobbyDataToLocalJson(
                dto,
                success ? "Server save success sync" : "Server save failed fallback");

            return success;
        }

        [Button("Load Last JSON")]
        public void LoadLastJson()
        {
            LoadLobbyDataFromJson(lastJson);
        }

        [Button("Load Lobby From Firebase")]
        public void LoadLobbyDataFromCloud()
        {
            LoadLobbyDataFromCloud(allowLocalJsonMerge: false, localSyncReason: "Server load success sync");
        }

        public void LoadLobbyDataFromCloudIgnoringLocalJson(string localSyncReason)
        {
            LoadLobbyDataFromCloud(
                allowLocalJsonMerge: false,
                localSyncReason: string.IsNullOrWhiteSpace(localSyncReason)
                    ? "Server load success sync"
                    : localSyncReason);
        }

        private void LoadLobbyDataFromCloud(bool allowLocalJsonMerge, string localSyncReason)
        {
            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();
            if (saveSystem == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem missing. Server lobby save cannot be loaded.", this);
                TryLoadLobbyDataFromLocalJson("CloudSaveSystem missing");
                isInitialLoadCompleted = true;
                return;
            }

            LobbySaveDTO dto = saveSystem.CreateLobbySaveDTOFromCurrentData();
            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] Server DTO missing. Server lobby save cannot be loaded.", this);
                TryLoadLobbyDataFromLocalJson("Server DTO missing");
                isInitialLoadCompleted = true;
                return;
            }

            if (!HasAnyLobbySaveData(dto) && TryLoadLobbyDataFromLocalJson("Server DTO empty"))
            {
                isInitialLoadCompleted = true;
                return;
            }

            if (allowLocalJsonMerge)
                MergeLocalJsonSectionsInto(dto, "Server DTO missing lobby inventory sections");
            else
                Debug.Log("[LobbySaveService] Local JSON merge skipped. Applying server lobby save as authoritative.", this);

            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            ApplyLobbySaveDTO(dto, preserveRuntimeInventoryOnEmptyInput: allowLocalJsonMerge);
            Debug.Log("[LobbySaveService] Lobby save JSON loaded.", this);
            SaveLobbyDataToLocalJson(
                dto,
                string.IsNullOrWhiteSpace(localSyncReason) ? "Server load success sync" : localSyncReason);
            isInitialLoadCompleted = true;
        }

        public void LoadLobbyDataFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[LobbySaveService] JSON is empty.", this);
                return;
            }

            LobbySaveDTO dto;

            try
            {
                dto = JsonUtility.FromJson<LobbySaveDTO>(json);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[LobbySaveService] JSON parse failed: {exception.Message}", this);
                return;
            }

            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] Could not create LobbySaveDTO from JSON.", this);
                return;
            }

            ApplyLobbySaveDTO(dto, preserveRuntimeInventoryOnEmptyInput: true);
            Debug.Log("[LobbySaveService] Lobby save JSON loaded.", this);
        }

        private void ApplyLobbySaveDTO(LobbySaveDTO dto, bool preserveRuntimeInventoryOnEmptyInput)
        {
            if (inventoryState != null)
            {
                if (dto.hasCredits)
                    inventoryState.SetCredits(dto.credits);

                if (ShouldApplyInventorySection(dto))
                {
                    inventoryState.SetInventoryItems(dto.inventoryItems);
                }
                else
                {
                    Debug.LogWarning("[LobbySaveService] Incoming inventoryItems section is unknown. Keeping runtime player inventory to avoid scene-transition wipe.", this);
                }

                if (ShouldApplyStashSection(dto))
                    inventoryState.SetStashItems(dto.stashItems);
                else
                    Debug.LogWarning("[LobbySaveService] Incoming stashItems section is unknown. Keeping runtime stash state.", this);

                if (ShouldApplyEquipmentSection(dto))
                    inventoryState.SetEquipmentItems(dto.equipmentItems);
                else
                    Debug.LogWarning("[LobbySaveService] Incoming equipmentItems section is unknown. Keeping runtime equipment state.", this);

                if (ShouldApplyQuickSlotSection(dto))
                    inventoryState.SetQuickSlotItems(dto.quickSlotItems);
                else
                    Debug.LogWarning("[LobbySaveService] Incoming quickSlotItems section is unknown. Keeping runtime quickslot state.", this);
            }
            else
            {
                Debug.LogWarning("[LobbySaveService] LobbyInventoryState missing. Inventory state not applied.", this);
            }

            if (facilityState != null)
            {
                facilityState.SetFacilities(dto.facilities);
                ApplyFacilityStateToSceneFacilities();
            }
            else
                Debug.LogWarning("[LobbySaveService] LobbyFacilityState missing. Facility state not applied.", this);

            if (inventoryStateUiBridge != null)
                inventoryStateUiBridge.ApplyStateToUi();
            else if (IsLobbyScene())
                Debug.LogWarning("[LobbySaveService] LobbyInventoryStateUiBridge missing. UI not refreshed.", this);
            else
                Debug.Log("[LobbySaveService] LobbyInventoryStateUiBridge missing in non-lobby scene. UI refresh skipped.", this);
        }

        private static bool IsLobbyScene()
        {
            return SceneManager.GetActiveScene().name == "Lobby";
        }

        private void ApplyFacilityStateToSceneFacilities()
        {
            if (facilityState == null || facilityState.Facilities == null || facilityState.Facilities.Count == 0)
                return;

            FacilityBase[] facilities = FindObjectsByType<FacilityBase>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < facilities.Length; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null || !TryGetSavedFacilityLevel(facility, out int savedLevel))
                    continue;

                if (!facility.CanSetLevel(savedLevel))
                    continue;

                if (!CanWriteSceneFacilityLevel(facility))
                    continue;

                int previousLevel = facility.CurrentLevel.Value;
                facility.CurrentLevel.Value = savedLevel;

                Debug.Log(
                    $"[Facility] Apply level. type={facility.Type}, loadedLevel={savedLevel}, previousLevel={previousLevel}, finalLevel={facility.CurrentLevel.Value}",
                    facility);
            }

            RefreshOpenHideoutWindows();
        }

        private bool TryGetSavedFacilityLevel(FacilityBase facility, out int level)
        {
            level = 1;

            if (facility == null || facilityState?.Facilities == null)
                return false;

            string facilityId = NormalizeFacilityId(facility.Type.ToString());

            for (int i = 0; i < facilityState.Facilities.Count; i++)
            {
                FacilitySaveDTO savedFacility = facilityState.Facilities[i];

                if (savedFacility == null)
                    continue;

                if (NormalizeFacilityId(savedFacility.facilityId) != facilityId)
                    continue;

                level = Mathf.Max(1, savedFacility.level);
                return true;
            }

            return false;
        }

        private static bool CanWriteSceneFacilityLevel(FacilityBase facility)
        {
            if (facility == null)
                return false;

            if (!facility.IsSpawned)
                return false;

            return facility.IsServer;
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

        private static string NormalizeFacilityId(string facilityId)
        {
            return string.IsNullOrWhiteSpace(facilityId)
                ? string.Empty
                : facilityId.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

        private bool ShouldApplyInventorySection(LobbySaveDTO incomingDto)
        {
            return incomingDto != null &&
                   (incomingDto.hasInventorySection ||
                    (incomingDto.inventoryItems != null && incomingDto.inventoryItems.Count > 0));
        }

        private static bool ShouldApplyStashSection(LobbySaveDTO incomingDto)
        {
            return incomingDto != null &&
                   (incomingDto.hasStashSection ||
                    (incomingDto.stashItems != null && incomingDto.stashItems.Count > 0));
        }

        private static bool ShouldApplyEquipmentSection(LobbySaveDTO incomingDto)
        {
            return incomingDto != null &&
                   (incomingDto.hasEquipmentSection ||
                    (incomingDto.equipmentItems != null && incomingDto.equipmentItems.Count > 0));
        }

        private static bool ShouldApplyQuickSlotSection(LobbySaveDTO incomingDto)
        {
            return incomingDto != null &&
                   (incomingDto.hasQuickSlotSection ||
                    (incomingDto.quickSlotItems != null && incomingDto.quickSlotItems.Count > 0));
        }

        private LobbySaveDTO CreateCurrentLobbySaveDTO()
        {
            LobbySaveDTO dto = new LobbySaveDTO();

            if (inventorySaveCollector != null)
                inventorySaveCollector.Collect(dto);
            else
                Debug.LogWarning("[LobbySaveService] InventorySaveCollector missing.", this);

            if (facilitySaveCollector != null)
                facilitySaveCollector.Collect(dto);
            else
                Debug.LogWarning("[LobbySaveService] FacilitySaveCollector missing.", this);

            return dto;
        }

        private LobbySaveDTO CreateCurrentStateSnapshotDTO()
        {
            LobbySaveDTO dto = new LobbySaveDTO();

            if (inventoryState != null)
            {
                dto.hasCredits = inventoryState.HasCredits;
                dto.credits = inventoryState.Credits;
                dto.hasInventorySection = true;
                dto.hasStashSection = true;
                dto.hasEquipmentSection = true;
                dto.hasQuickSlotSection = true;

                if (inventoryState.InventoryItems != null)
                    dto.inventoryItems.AddRange(inventoryState.InventoryItems);

                if (inventoryState.StashItems != null)
                    dto.stashItems.AddRange(inventoryState.StashItems);

                if (inventoryState.QuickSlotItems != null)
                    dto.quickSlotItems.AddRange(inventoryState.QuickSlotItems);

                if (inventoryState.EquipmentItems != null)
                    dto.equipmentItems.AddRange(inventoryState.EquipmentItems);
            }
            else
            {
                Debug.LogWarning("[LobbySaveService] LobbyInventoryState missing. Local snapshot will not include inventory data.", this);
            }

            if (facilityState != null && facilityState.Facilities != null)
                dto.facilities.AddRange(facilityState.Facilities);
            else if (facilitySaveCollector != null)
                facilitySaveCollector.Collect(dto);

            return dto;
        }

        private IEnumerator LoadLobbyDataFromCloudAfterUiReady()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            pendingCloudLoadCoroutine = null;
            LoadLobbyDataFromCloud();
        }

        private void HandleCloudSaveLoaded(CloudSaveLoadedEvent e)
        {
            if (!loadFromCloudOnCloudSaveLoaded)
                return;

            QueueLoadLobbyDataFromCloudAfterUiReady();
        }

        private void QueueLoadLobbyDataFromCloudAfterUiReady()
        {
            if (pendingCloudLoadCoroutine != null)
                StopCoroutine(pendingCloudLoadCoroutine);

            pendingCloudLoadCoroutine = StartCoroutine(LoadLobbyDataFromCloudAfterUiReady());
        }

        private bool TryLoadLobbyDataFromLocalJson(string reason)
        {
            if (!CanUseLocalJsonFallback)
                return false;

            if (!TryReadFirstLocalJsonDTO(out LobbySaveDTO dto, out string path, out string json))
            {
                Debug.LogWarning($"[LobbySaveService] Local JSON fallback not found. Reason={reason}, Paths={string.Join(", ", GetLocalJsonCandidatePaths())}", this);
                isInitialLoadCompleted = true;
                return false;
            }

            Debug.Log($"[LobbySaveService] Applying local JSON fallback. Reason={reason}, Path={path}", this);
            Debug.LogWarning("[HideoutLoad] Applying facility levels from=LocalJson", this);
            lastJson = json;
            ApplyLobbySaveDTO(dto, preserveRuntimeInventoryOnEmptyInput: true);
            Debug.Log("[LobbySaveService] Lobby save JSON loaded.", this);
            isInitialLoadCompleted = true;
            return true;
        }

        private void MergeLocalJsonSectionsInto(LobbySaveDTO dto, string reason)
        {
            if (dto == null || !CanUseLocalJsonFallback)
                return;

            if (!TryReadFirstLocalJsonDTO(out LobbySaveDTO localDto, out string path, out _))
                return;

            bool changed = false;

            if (!dto.hasCredits && localDto.hasCredits)
            {
                dto.hasCredits = true;
                dto.credits = localDto.credits;
                changed = true;
            }

            if (!preferLocalJsonInventorySections)
                return;

            if (localDto.hasInventorySection || HasAny(localDto.inventoryItems))
            {
                dto.inventoryItems = localDto.inventoryItems != null
                    ? new List<ItemSaveDTO>(localDto.inventoryItems)
                    : new List<ItemSaveDTO>();
                dto.hasInventorySection = true;
                changed = true;
            }

            if (localDto.hasStashSection || HasAny(localDto.stashItems))
            {
                dto.stashItems = localDto.stashItems != null
                    ? new List<ItemSaveDTO>(localDto.stashItems)
                    : new List<ItemSaveDTO>();
                dto.hasStashSection = true;
                changed = true;
            }

            if (localDto.hasEquipmentSection || HasAny(localDto.equipmentItems))
            {
                dto.equipmentItems = localDto.equipmentItems != null
                    ? new List<EquipmentSaveDTO>(localDto.equipmentItems)
                    : new List<EquipmentSaveDTO>();
                dto.hasEquipmentSection = true;
                changed = true;
            }

            if (localDto.hasQuickSlotSection || HasAny(localDto.quickSlotItems))
            {
                dto.quickSlotItems = localDto.quickSlotItems != null
                    ? new List<ItemSaveDTO>(localDto.quickSlotItems)
                    : new List<ItemSaveDTO>();
                dto.hasQuickSlotSection = true;
                changed = true;
            }

            if (!changed)
                return;

            Debug.Log(
                $"[LobbySaveService] Merged local JSON lobby sections into loaded server save. Reason={reason}, Path={path}, " +
                $"preferLocal={preferLocalJsonInventorySections}, inventory={dto.inventoryItems.Count}, stash={dto.stashItems.Count}, quickSlots={dto.quickSlotItems?.Count ?? 0}, equipment={dto.equipmentItems.Count}",
                this);
        }

        public bool LocalJsonExists()
        {
            if (!CanUseLocalJsonFallback)
                return false;

            string[] candidatePaths = GetLocalJsonCandidatePaths();
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                if (File.Exists(candidatePaths[i]))
                    return true;
            }

            return false;
        }

        private void SaveLobbyDataToLocalJson(LobbySaveDTO dto, string reason)
        {
            if (!CanUseLocalJsonFallback || dto == null)
                return;

            string path = GetLocalJsonPath();

            if (WouldOverwriteLocalJsonWithEmptyState(dto, path))
            {
                Debug.LogWarning($"[LobbySaveService] Local JSON save skipped to avoid overwriting existing non-empty save with empty state. Reason={reason}, Path={path}", this);
                return;
            }

            string json = JsonUtility.ToJson(dto, true);

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, json);
                lastJson = json;
                Debug.Log($"[LobbySaveService] Local JSON saved. Reason={reason}, Path={path}", this);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[LobbySaveService] Local JSON save failed. Reason={reason}, Error={exception.Message}, Path={path}", this);
            }
        }

        private string GetLocalJsonPath()
        {
            string userKey = GetLocalJsonUserKey();
            if (string.Equals(userKey, "local", System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(lastLoadedLocalJsonPath))
            {
                return lastLoadedLocalJsonPath;
            }

            return GetLocalJsonPathForUserKey(userKey);
        }

        private string[] GetLocalJsonCandidatePaths()
        {
            List<string> paths = new();
            string primaryPath = GetLocalJsonPath();
            string localPath = GetLocalJsonPathForUserKey("local");

            AddLocalJsonCandidatePath(paths, primaryPath);
            AddLocalJsonCandidatePath(paths, localPath);

            AddExistingLocalJsonPaths(paths);

            return paths.ToArray();
        }

        private static void AddExistingLocalJsonPaths(List<string> paths)
        {
            string directory = Path.Combine(Application.persistentDataPath, "LobbySave");
            if (!Directory.Exists(directory))
                return;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "lobby_*.json", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return;
            }

            System.Array.Sort(
                files,
                (left, right) => File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));

            for (int i = 0; i < files.Length; i++)
            {
                AddLocalJsonCandidatePath(paths, files[i]);
            }
        }

        private static void AddLocalJsonCandidatePath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
                return;

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], path, System.StringComparison.OrdinalIgnoreCase))
                    return;
            }

            paths.Add(path);
        }

        private bool TryReadFirstLocalJsonDTO(out LobbySaveDTO dto, out string path, out string json)
        {
            dto = null;
            path = null;
            json = null;
            LobbySaveDTO firstValidDto = null;
            string firstValidPath = null;
            string firstValidJson = null;

            string[] candidatePaths = GetLocalJsonCandidatePaths();
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string candidatePath = candidatePaths[i];
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                    continue;

                try
                {
                    string candidateJson = File.ReadAllText(candidatePath);
                    if (string.IsNullOrWhiteSpace(candidateJson))
                        continue;

                    LobbySaveDTO candidateDto = JsonUtility.FromJson<LobbySaveDTO>(candidateJson);
                    if (candidateDto == null)
                        continue;

                    NormalizeSectionFlags(candidateDto);

                    if (firstValidDto == null)
                    {
                        firstValidDto = candidateDto;
                        firstValidPath = candidatePath;
                        firstValidJson = candidateJson;
                    }

                    if (!HasMeaningfulSaveData(candidateDto))
                        continue;

                    dto = candidateDto;
                    path = candidatePath;
                    json = candidateJson;
                    lastLoadedLocalJsonPath = candidatePath;
                    return true;
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[LobbySaveService] Local JSON read failed. Error={exception.Message}, Path={candidatePath}", this);
                }
            }

            if (firstValidDto == null)
                return false;

            dto = firstValidDto;
            path = firstValidPath;
            json = firstValidJson;
            lastLoadedLocalJsonPath = firstValidPath;
            return true;
        }

        private string GetLocalJsonUserKey()
        {
            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();

            if (saveSystem != null && !string.IsNullOrWhiteSpace(saveSystem.LoadedFirebaseUid))
                return SanitizeLocalJsonUserKey(saveSystem.LoadedFirebaseUid);

            return "local";
        }

        private static string GetLocalJsonPathForUserKey(string userKey)
        {
            return Path.Combine(Application.persistentDataPath, "LobbySave", $"lobby_{SanitizeLocalJsonUserKey(userKey)}.json");
        }

        private static string SanitizeLocalJsonUserKey(string userKey)
        {
            if (string.IsNullOrWhiteSpace(userKey))
                userKey = "local";

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                userKey = userKey.Replace(invalidChar, '_');

            return userKey;
        }

        private bool WouldOverwriteExistingSaveWithEmptyState(LobbySaveDTO dto, CloudSaveSystem saveSystem)
        {
            if (!IsEmptyInventoryAndDefaultFacilities(dto))
                return false;

            if (HasMeaningfulLocalJsonSave())
                return true;

            if (saveSystem == null || !saveSystem.HasLoadedData)
                return false;

            LobbySaveDTO serverDto = saveSystem.CreateLobbySaveDTOFromCurrentData();
            return HasMeaningfulSaveData(serverDto);
        }

        private bool WouldOverwriteLocalJsonWithEmptyState(LobbySaveDTO dto, string path)
        {
            if (!IsEmptyInventoryAndDefaultFacilities(dto))
                return false;

            LobbySaveDTO existingDto = TryReadLocalJsonDTO(path);
            return HasMeaningfulSaveData(existingDto) || HasMeaningfulLocalJsonSave();
        }

        private bool HasMeaningfulLocalJsonSave()
        {
            string[] candidatePaths = GetLocalJsonCandidatePaths();

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                if (HasMeaningfulSaveData(TryReadLocalJsonDTO(candidatePaths[i])))
                    return true;
            }

            return false;
        }

        private static LobbySaveDTO TryReadLocalJsonDTO(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                LobbySaveDTO dto = JsonUtility.FromJson<LobbySaveDTO>(json);
                NormalizeSectionFlags(dto);
                return dto;
            }
            catch
            {
                return null;
            }
        }

        private CloudSaveSystem ResolveCloudSaveSystem()
        {
            CloudSaveSystem registeredSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (registeredSaveSystem != null && registeredSaveSystem.enabled && registeredSaveSystem.HasLoadedData)
            {
                cloudSaveSystem = registeredSaveSystem;
                return cloudSaveSystem;
            }

            if (cloudSaveSystem != null && cloudSaveSystem.enabled && cloudSaveSystem.HasLoadedData)
                return cloudSaveSystem;

            if (registeredSaveSystem != null && registeredSaveSystem.enabled)
            {
                cloudSaveSystem = registeredSaveSystem;
                return cloudSaveSystem;
            }

            if (cloudSaveSystem != null && cloudSaveSystem.enabled)
                return cloudSaveSystem;

            if (cloudSaveSystem != null && !cloudSaveSystem.enabled)
            {
                Debug.LogWarning("[LobbySaveService] Ignoring disabled CloudSaveSystem reference and resolving the active save authority.", this);
                cloudSaveSystem = null;
            }

            CloudSaveSystem[] saveSystems = FindObjectsByType<CloudSaveSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (saveSystems.Length > 1)
            {
                Debug.LogWarning(
                    $"[LobbySaveService] Found {saveSystems.Length} CloudSaveSystem instances. The loaded and enabled instance will be used as the save authority.",
                    this);
            }

            for (int i = 0; i < saveSystems.Length; i++)
            {
                CloudSaveSystem saveSystem = saveSystems[i];
                if (saveSystem != null && saveSystem.enabled && saveSystem.HasLoadedData)
                {
                    cloudSaveSystem = saveSystem;
                    return cloudSaveSystem;
                }
            }

            for (int i = 0; i < saveSystems.Length; i++)
            {
                CloudSaveSystem saveSystem = saveSystems[i];
                if (saveSystem != null && saveSystem.enabled)
                {
                    cloudSaveSystem = saveSystem;
                    return cloudSaveSystem;
                }
            }

            return null;
        }

        private static bool HasAnyLobbySaveData(LobbySaveDTO dto)
        {
            if (dto == null)
                return false;

            return dto.hasCredits ||
                   dto.hasInventorySection ||
                   dto.hasStashSection ||
                   dto.hasEquipmentSection ||
                   dto.hasQuickSlotSection ||
                   HasAny(dto.inventoryItems) ||
                   HasAny(dto.stashItems) ||
                   HasAny(dto.quickSlotItems) ||
                   HasAny(dto.equipmentItems) ||
                   HasAny(dto.facilities);
        }

        private static bool HasAnyLobbyInventorySection(LobbySaveDTO dto)
        {
            if (dto == null)
                return false;

            return dto.hasInventorySection ||
                   dto.hasStashSection ||
                   dto.hasEquipmentSection ||
                   dto.hasQuickSlotSection ||
                   HasAny(dto.inventoryItems) ||
                   HasAny(dto.stashItems) ||
                   HasAny(dto.quickSlotItems) ||
                   HasAny(dto.equipmentItems);
        }

        private static void NormalizeSectionFlags(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            dto.hasInventorySection |= HasAny(dto.inventoryItems);
            dto.hasStashSection |= HasAny(dto.stashItems);
            dto.hasEquipmentSection |= HasAny(dto.equipmentItems);
            dto.hasQuickSlotSection |= HasAny(dto.quickSlotItems);
        }

        private static bool HasAny<T>(System.Collections.Generic.ICollection<T> items)
        {
            return items != null && items.Count > 0;
        }

        private static bool IsEmptyInventoryAndDefaultFacilities(LobbySaveDTO dto)
        {
            if (dto == null)
                return true;

            bool allInventorySectionsAuthoritative =
                dto.hasInventorySection &&
                dto.hasStashSection &&
                dto.hasEquipmentSection &&
                dto.hasQuickSlotSection;

            if (allInventorySectionsAuthoritative)
                return false;

            return !HasAny(dto.inventoryItems) &&
                   !HasAny(dto.stashItems) &&
                   !HasAny(dto.quickSlotItems) &&
                   !HasAny(dto.equipmentItems) &&
                   !HasAny(dto.quickSlotItems) &&
                   !HasNonDefaultFacility(dto);
        }

        private static bool HasMeaningfulSaveData(LobbySaveDTO dto)
        {
            if (dto == null)
                return false;

            return HasAny(dto.inventoryItems) ||
                   HasAny(dto.stashItems) ||
                   HasAny(dto.quickSlotItems) ||
                   HasAny(dto.equipmentItems) ||
                   HasAny(dto.quickSlotItems) ||
                   HasNonDefaultFacility(dto);
        }

        private static bool HasNonDefaultFacility(LobbySaveDTO dto)
        {
            if (dto?.facilities == null)
                return false;

            for (int i = 0; i < dto.facilities.Count; i++)
            {
                FacilitySaveDTO facility = dto.facilities[i];

                if (facility != null && facility.level > 1)
                    return true;
            }

            return false;
        }
    }
}

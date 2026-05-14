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
        [SerializeField] private bool preferLocalJsonInventorySections = true;
        [SerializeField] private bool allowLifecycleAutoSave;

        [Header("Debug JSON")]
        [TextArea(8, 20)]
        [SerializeField] private string lastJson;

        private bool isCloudSaveRunning;
        private Coroutine pendingCloudLoadCoroutine;
        private bool isInitialLoadCompleted;
        private bool forceApplyIncomingInventoryItemsOnce;

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

            SaveLobbyDataToCloud();
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
            SaveLobbyDataToCloud();
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
                Debug.LogWarning("[LobbySaveService] Server save is already running.", this);
                return false;
            }

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
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem missing. Saving local JSON fallback.", this);
                SaveLobbyDataToLocalJson(dto, "CloudSaveSystem missing");
                return false;
            }

            isCloudSaveRunning = true;

            try
            {
                bool success = await saveSystem.SaveLobbyDataAsync(dto);
                Debug.Log(success
                    ? "[LobbySaveService] Server save success"
                    : "[LobbySaveService] Server save failed", this);

                SaveLobbyDataToLocalJson(
                    dto,
                    success ? "Server save success sync" : "Server save failed fallback");

                return success;
            }
            finally
            {
                isCloudSaveRunning = false;
            }
        }

        [Button("Load Last JSON")]
        public void LoadLastJson()
        {
            LoadLobbyDataFromJson(lastJson);
        }

        [Button("Load Lobby From Firebase")]
        public void LoadLobbyDataFromCloud()
        {
            LoadLobbyDataFromCloud(allowLocalJsonMerge: true, localSyncReason: "Server load success sync");
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
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem missing. Trying local JSON fallback.", this);
                TryLoadLobbyDataFromLocalJson("CloudSaveSystem missing");
                isInitialLoadCompleted = true;
                return;
            }

            LobbySaveDTO dto = saveSystem.CreateLobbySaveDTOFromCurrentData();
            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] Server DTO missing. Trying local JSON fallback.", this);
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
            bool forceApplyIncomingInventoryItems = forceApplyIncomingInventoryItemsOnce;
            forceApplyIncomingInventoryItemsOnce = false;

            if (inventoryState != null)
            {
                if (dto.hasCredits)
                    inventoryState.SetCredits(dto.credits);

                if (preserveRuntimeInventoryOnEmptyInput && ShouldKeepExistingInventoryItems(dto))
                {
                    Debug.LogWarning("[LobbySaveService] Incoming inventoryItems is empty while runtime inventory has items. Keeping runtime player inventory to avoid scene-transition wipe.", this);
                }
                else
                {
                    inventoryState.SetInventoryItems(dto.inventoryItems);
                }

                inventoryState.SetStashItems(dto.stashItems);
                inventoryState.SetQuickSlotItems(dto.quickSlotItems);
                inventoryState.SetEquipmentItems(dto.equipmentItems);
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

        private bool ShouldKeepExistingInventoryItems(LobbySaveDTO incomingDto)
        {
            if (!isInitialLoadCompleted)
                return false;

            if (incomingDto == null || incomingDto.inventoryItems == null || incomingDto.inventoryItems.Count > 0)
                return false;

            return inventoryState != null &&
                   inventoryState.InventoryItems != null &&
                   inventoryState.InventoryItems.Count > 0;
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
            if (!useLocalJsonFallback)
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
            if (dto == null || !useLocalJsonFallback)
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

            bool serverInventorySectionsMissing = !HasAnyLobbyInventorySection(dto);
            if (!preferLocalJsonInventorySections || !serverInventorySectionsMissing)
                return;

            if (HasAny(localDto.inventoryItems))
            {
                dto.inventoryItems = new List<ItemSaveDTO>(localDto.inventoryItems);
                changed = true;
            }

            if (HasAny(localDto.stashItems))
            {
                dto.stashItems = new List<ItemSaveDTO>(localDto.stashItems);
                changed = true;
            }

            if (HasAny(localDto.quickSlotItems))
            {
                dto.quickSlotItems = new List<ItemSaveDTO>(localDto.quickSlotItems);
                changed = true;
            }

            if (HasAny(localDto.equipmentItems))
            {
                dto.equipmentItems = new List<EquipmentSaveDTO>(localDto.equipmentItems);
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
            if (!useLocalJsonFallback)
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
            if (!useLocalJsonFallback || dto == null)
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
            return GetLocalJsonPathForUserKey(GetLocalJsonUserKey());
        }

        private string[] GetLocalJsonCandidatePaths()
        {
            List<string> paths = new();
            string primaryPath = GetLocalJsonPath();
            string localPath = GetLocalJsonPathForUserKey("local");

            paths.Add(primaryPath);

            if (!string.Equals(primaryPath, localPath, System.StringComparison.OrdinalIgnoreCase))
                paths.Add(localPath);

            return paths.ToArray();
        }

        private bool TryReadFirstLocalJsonDTO(out LobbySaveDTO dto, out string path, out string json)
        {
            dto = null;
            path = null;
            json = null;

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

                    dto = candidateDto;
                    path = candidatePath;
                    json = candidateJson;
                    return true;
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[LobbySaveService] Local JSON read failed. Error={exception.Message}, Path={candidatePath}", this);
                }
            }

            return false;
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
            return HasMeaningfulSaveData(existingDto);
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

                return JsonUtility.FromJson<LobbySaveDTO>(json);
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

            return HasAny(dto.inventoryItems) ||
                   HasAny(dto.stashItems) ||
                   HasAny(dto.quickSlotItems) ||
                   HasAny(dto.equipmentItems);
        }

        private static bool HasAny<T>(System.Collections.Generic.ICollection<T> items)
        {
            return items != null && items.Count > 0;
        }

        private static bool IsEmptyInventoryAndDefaultFacilities(LobbySaveDTO dto)
        {
            if (dto == null)
                return true;

            return !HasAny(dto.inventoryItems) &&
                   !HasAny(dto.stashItems) &&
                   !HasAny(dto.quickSlotItems) &&
                   !HasAny(dto.equipmentItems) &&
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

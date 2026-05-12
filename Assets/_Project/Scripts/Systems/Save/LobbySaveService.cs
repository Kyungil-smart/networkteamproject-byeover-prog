using System.Collections;
using System.IO;
using System.Threading.Tasks;

using DeadZone.Core;
using DeadZone.Network;

using Sirenix.OdinInspector;

using UnityEngine;

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

        [Header("Debug JSON")]
        [TextArea(8, 20)]
        [SerializeField] private string lastJson;

        private bool isCloudSaveRunning;
        private Coroutine pendingCloudLoadCoroutine;

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
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus || !saveToCloudOnApplicationPause)
                return;

            SaveLobbyDataToCloud();
        }

        private void OnApplicationQuit()
        {
            if (!saveToCloudOnApplicationQuit)
                return;

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

            LobbySaveDTO dto = CreateCurrentLobbySaveDTO();
            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            Debug.Log($"[LobbySaveService] Server save request JSON\n{json}", this);

            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();
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
            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();
            if (saveSystem == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem missing. Trying local JSON fallback.", this);
                TryLoadLobbyDataFromLocalJson("CloudSaveSystem missing");
                return;
            }

            LobbySaveDTO dto = saveSystem.CreateLobbySaveDTOFromCurrentData();
            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] Server DTO missing. Trying local JSON fallback.", this);
                TryLoadLobbyDataFromLocalJson("Server DTO missing");
                return;
            }

            if (!HasAnyLobbySaveData(dto) && TryLoadLobbyDataFromLocalJson("Server DTO empty"))
                return;

            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;

            LoadLobbyDataFromJson(json);
            SaveLobbyDataToLocalJson(dto, "Server load success sync");
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

            ApplyLobbySaveDTO(dto);
            Debug.Log("[LobbySaveService] Lobby save JSON loaded.", this);
        }

        private void ApplyLobbySaveDTO(LobbySaveDTO dto)
        {
            if (inventoryState != null)
            {
                if (dto.hasCredits)
                    inventoryState.SetCredits(dto.credits);

                inventoryState.SetInventoryItems(dto.inventoryItems);
                inventoryState.SetStashItems(dto.stashItems);
                inventoryState.SetEquipmentItems(dto.equipmentItems);
            }
            else
            {
                Debug.LogWarning("[LobbySaveService] LobbyInventoryState missing. Inventory state not applied.", this);
            }

            if (facilityState != null)
                facilityState.SetFacilities(dto.facilities);
            else
                Debug.LogWarning("[LobbySaveService] LobbyFacilityState missing. Facility state not applied.", this);

            if (inventoryStateUiBridge != null)
                inventoryStateUiBridge.ApplyStateToUi();
            else
                Debug.LogWarning("[LobbySaveService] LobbyInventoryStateUiBridge missing. UI not refreshed.", this);
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

            string path = GetLocalJsonPath();

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[LobbySaveService] Local JSON fallback not found. Reason={reason}, Path={path}", this);
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[LobbySaveService] Local JSON read failed. Reason={reason}, Error={exception.Message}, Path={path}", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning($"[LobbySaveService] Local JSON is empty. Reason={reason}, Path={path}", this);
                return false;
            }

            Debug.Log($"[LobbySaveService] Applying local JSON fallback. Reason={reason}, Path={path}", this);
            lastJson = json;
            LoadLobbyDataFromJson(json);
            return true;
        }

        private void SaveLobbyDataToLocalJson(LobbySaveDTO dto, string reason)
        {
            if (!useLocalJsonFallback || dto == null)
                return;

            string path = GetLocalJsonPath();
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
            string userKey = "local";
            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();

            if (saveSystem != null && !string.IsNullOrWhiteSpace(saveSystem.LoadedFirebaseUid))
                userKey = saveSystem.LoadedFirebaseUid;

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                userKey = userKey.Replace(invalidChar, '_');

            return Path.Combine(Application.persistentDataPath, "LobbySave", $"lobby_{userKey}.json");
        }

        private CloudSaveSystem ResolveCloudSaveSystem()
        {
            if (cloudSaveSystem != null)
                return cloudSaveSystem;

            cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem != null)
                return cloudSaveSystem;

            cloudSaveSystem = FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);
            return cloudSaveSystem;
        }

        private static bool HasAnyLobbySaveData(LobbySaveDTO dto)
        {
            if (dto == null)
                return false;

            return dto.hasCredits ||
                   HasAny(dto.inventoryItems) ||
                   HasAny(dto.stashItems) ||
                   HasAny(dto.equipmentItems) ||
                   HasAny(dto.facilities);
        }

        private static bool HasAny<T>(System.Collections.Generic.ICollection<T> items)
        {
            return items != null && items.Count > 0;
        }
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading.Tasks;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    [DefaultExecutionOrder(-1000)]
    public sealed class HousingEndKeyUpgradeTester : MonoBehaviour
    {
        private const int MinLevel = 1;
        private const int MaxLevel = 4;

        private static HousingEndKeyUpgradeTester instance;

        [SerializeField]
        private bool shiftEndResetsHousingLevels = true;

        [SerializeField]
        private bool logDiagnostics = true;

        private bool cloudOnlySaveInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            GameObject go = new GameObject(nameof(HousingEndKeyUpgradeTester));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HousingEndKeyUpgradeTester>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (!logDiagnostics)
                return;

            Debug.Log("[HousingEndKeyUpgradeTester] Active. Press End to advance housing levels, Shift+End to reset to Lv.1.");
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            if (!keyboard.endKey.wasPressedThisFrame)
                return;

            bool resetToLevelOne =
                shiftEndResetsHousingLevels &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (logDiagnostics)
            {
                Debug.Log(
                    $"[HousingEndKeyUpgradeTester] End detected. Scene={GetActiveSceneName()}, " +
                    $"NetworkListening={NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening}, " +
                    $"Reset={resetToLevelOne}");
            }

            if (TryRunThroughNetworkPlayer(resetToLevelOne))
                return;

            _ = RunCloudOnlyFallbackAsync(resetToLevelOne);
        }

        private bool TryRunThroughNetworkPlayer(bool resetToLevelOne)
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening)
            {
                LogDiagnostic("Network path skipped. NetworkManager is not listening.");
                return false;
            }

            NetworkClient localClient = networkManager.LocalClient;
            NetworkObject playerObject = localClient?.PlayerObject;

            if (playerObject == null)
            {
                LogDiagnostic("Network path skipped. Local PlayerObject is null.");
                return false;
            }

            PlayerHousingSaveSyncer saveSyncer = playerObject.GetComponent<PlayerHousingSaveSyncer>();

            if (saveSyncer == null)
                saveSyncer = playerObject.GetComponentInChildren<PlayerHousingSaveSyncer>(true);

            if (saveSyncer == null)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] Network path failed. PlayerHousingSaveSyncer is missing on the local PlayerObject.");
                return false;
            }

            bool requested = saveSyncer.TryRunDebugHousingLevelTest(resetToLevelOne);

            if (!requested)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] Network path failed. PlayerHousingSaveSyncer is not spawned as the local owner yet.");
                return false;
            }

            Debug.Log(resetToLevelOne
                ? "[HousingEndKeyUpgradeTester] Shift+End requested through local network player."
                : "[HousingEndKeyUpgradeTester] End requested through local network player.");

            return true;
        }

        private async Task RunCloudOnlyFallbackAsync(bool resetToLevelOne)
        {
            if (cloudOnlySaveInProgress)
            {
                LogDiagnostic("Cloud-only path skipped. A previous End-key save is still running.");
                return;
            }

            cloudOnlySaveInProgress = true;

            try
            {
                CloudSaveSystem cloudSaveSystem = ResolveCloudSaveSystem(preferLoadedData: true);

                if (cloudSaveSystem == null)
                {
                    Debug.LogWarning("[HousingEndKeyUpgradeTester] Cloud-only path failed. CloudSaveSystem was not found.");
                    return;
                }

                if (!cloudSaveSystem.HasLoadedData)
                {
                    LogDiagnostic("Cloud-only path found CloudSaveSystem, but data is not loaded yet. Trying LoadAsync.");
                    PlayerCloudData loadedData = await cloudSaveSystem.LoadAsync();

                    if (loadedData == null || !cloudSaveSystem.HasLoadedData)
                    {
                        Debug.LogWarning("[HousingEndKeyUpgradeTester] Cloud-only path failed. Cloud Save is not loaded. Log in first, then press End after the lobby finishes loading.");
                        return;
                    }
                }

                PlayerHousingProgressDTO dto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();

                if (dto == null)
                {
                    Debug.LogWarning("[HousingEndKeyUpgradeTester] Cloud-only path failed. Housing DTO could not be created from Cloud Save.");
                    return;
                }

                if (resetToLevelOne)
                    SetLevel(dto, MinLevel);
                else
                    IncrementLevel(dto);

                dto.Normalize();
                ApplyToLobbyFacilityState(dto);

                bool success = await cloudSaveSystem.SaveHousingProgressAsync(dto);

                Debug.Log(success
                    ? $"[HousingEndKeyUpgradeTester] Cloud-only housing test saved. Workbench Lv.{dto.workbenchLevel}, Medical Lv.{dto.medicalLevel}, Gym Lv.{dto.gymLevel}, Stash Lv.{dto.stashLevel}, Kitchen Lv.{dto.kitchenLevel}, Bed Lv.{dto.bedLevel}, CommStation Lv.{dto.commStationLevel}"
                    : "[HousingEndKeyUpgradeTester] Cloud-only housing test save failed.");
            }
            finally
            {
                cloudOnlySaveInProgress = false;
            }
        }

        private static CloudSaveSystem ResolveCloudSaveSystem(bool preferLoadedData)
        {
            CloudSaveSystem service = ServiceLocator.Get<CloudSaveSystem>();

            if (!preferLoadedData && service != null)
                return service;

            if (service != null && service.HasLoadedData)
                return service;

            CloudSaveSystem[] systems = FindObjectsByType<CloudSaveSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].HasLoadedData)
                    return systems[i];
            }

            if (service != null)
                return service;

            return systems.Length > 0 ? systems[0] : null;
        }

        private static void ApplyToLobbyFacilityState(PlayerHousingProgressDTO dto)
        {
            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null)
                return;

            facilityState.SetFacilityLevel("Workbench", dto.workbenchLevel);
            facilityState.SetFacilityLevel("Medical", dto.medicalLevel);
            facilityState.SetFacilityLevel("Gym", dto.gymLevel);
            facilityState.SetFacilityLevel("Stash", dto.stashLevel);
            facilityState.SetFacilityLevel("Kitchen", dto.kitchenLevel);
            facilityState.SetFacilityLevel("Bed", dto.bedLevel);
            facilityState.SetFacilityLevel("CommStation", dto.commStationLevel);
        }

        private static void IncrementLevel(PlayerHousingProgressDTO dto)
        {
            dto.workbenchLevel = Mathf.Clamp(dto.workbenchLevel + 1, MinLevel, MaxLevel);
            dto.medicalLevel = Mathf.Clamp(dto.medicalLevel + 1, MinLevel, MaxLevel);
            dto.gymLevel = Mathf.Clamp(dto.gymLevel + 1, MinLevel, MaxLevel);
            dto.stashLevel = Mathf.Clamp(dto.stashLevel + 1, MinLevel, MaxLevel);
            dto.kitchenLevel = Mathf.Clamp(dto.kitchenLevel + 1, MinLevel, MaxLevel);
            dto.bedLevel = Mathf.Clamp(dto.bedLevel + 1, MinLevel, MaxLevel);
            dto.commStationLevel = Mathf.Clamp(dto.commStationLevel + 1, MinLevel, MaxLevel);
        }

        private static void SetLevel(PlayerHousingProgressDTO dto, int level)
        {
            int safeLevel = Mathf.Clamp(level, MinLevel, MaxLevel);
            dto.workbenchLevel = safeLevel;
            dto.medicalLevel = safeLevel;
            dto.gymLevel = safeLevel;
            dto.stashLevel = safeLevel;
            dto.kitchenLevel = safeLevel;
            dto.bedLevel = safeLevel;
            dto.commStationLevel = safeLevel;
        }

        private void LogDiagnostic(string message)
        {
            if (!logDiagnostics)
                return;

            Debug.Log($"[HousingEndKeyUpgradeTester] {message} Scene={GetActiveSceneName()}");
        }

        private static string GetActiveSceneName()
        {
            Scene scene = SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : "<invalid>";
        }
    }
}
#endif

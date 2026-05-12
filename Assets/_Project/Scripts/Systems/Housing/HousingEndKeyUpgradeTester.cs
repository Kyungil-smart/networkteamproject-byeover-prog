#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading.Tasks;

using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

using DeadZone.Actors;
using DeadZone.Network;
using DeadZone.Systems.Save;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 에디터와 개발 빌드에서 End 키로 하우징 저장/복원 경로를 빠르게 검증하는 전역 테스트 리스너입니다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class HousingEndKeyUpgradeTester : MonoBehaviour
    {
        private const int MinLevel = 1;
        private const int MaxLevel = 4;

        private static HousingEndKeyUpgradeTester instance;

        [SerializeField]
        private bool shiftEndResetsHousingLevels = true;

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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard.endKey.wasPressedThisFrame)
                return;

            bool resetToLevelOne =
                shiftEndResetsHousingLevels &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (TryRunThroughNetworkPlayer(resetToLevelOne))
                return;

            _ = RunCloudOnlyFallbackAsync(resetToLevelOne);
        }

        private static bool TryRunThroughNetworkPlayer(bool resetToLevelOne)
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening)
                return false;

            NetworkClient localClient = networkManager.LocalClient;
            NetworkObject playerObject = localClient?.PlayerObject;

            if (playerObject == null)
                return false;

            PlayerHousingSaveSyncer saveSyncer = playerObject.GetComponent<PlayerHousingSaveSyncer>();

            if (saveSyncer == null)
                saveSyncer = playerObject.GetComponentInChildren<PlayerHousingSaveSyncer>(true);

            if (saveSyncer == null)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] Local PlayerObject에 PlayerHousingSaveSyncer가 없어 End 키 네트워크 테스트를 실행할 수 없습니다.");
                return false;
            }

            bool requested = saveSyncer.TryRunDebugHousingLevelTest(resetToLevelOne);

            if (!requested)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] PlayerHousingSaveSyncer가 아직 owner spawn 상태가 아니어서 End 키 네트워크 테스트를 실행하지 못했습니다.");
                return false;
            }

            Debug.Log(resetToLevelOne
                ? "[HousingEndKeyUpgradeTester] Shift+End 입력 감지. 네트워크 하우징 레벨 초기화를 요청했습니다."
                : "[HousingEndKeyUpgradeTester] End 입력 감지. 네트워크 하우징 레벨 업그레이드를 요청했습니다.");

            return true;
        }

        private async Task RunCloudOnlyFallbackAsync(bool resetToLevelOne)
        {
            if (cloudOnlySaveInProgress)
                return;

            CloudSaveSystem cloudSaveSystem = ResolveLoadedCloudSaveSystem();

            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] End 키를 감지했지만 Network Player와 로드된 CloudSaveSystem을 모두 찾지 못했습니다.");
                return;
            }

            PlayerHousingProgressDTO dto = cloudSaveSystem.CreateHousingProgressDTOFromCurrentData();

            if (dto == null)
            {
                Debug.LogWarning("[HousingEndKeyUpgradeTester] Cloud Save 하우징 데이터를 DTO로 만들지 못했습니다.");
                return;
            }

            if (resetToLevelOne)
                SetLevel(dto, MinLevel);
            else
                IncrementLevel(dto);

            dto.Normalize();
            ApplyToLobbyFacilityState(dto);

            cloudOnlySaveInProgress = true;

            try
            {
                bool success = await cloudSaveSystem.SaveHousingProgressAsync(dto);

                Debug.Log(success
                    ? resetToLevelOne
                        ? "[HousingEndKeyUpgradeTester] Shift+End Cloud Save 하우징 초기화 완료."
                        : "[HousingEndKeyUpgradeTester] End Cloud Save 하우징 업그레이드 완료."
                    : "[HousingEndKeyUpgradeTester] End 키 Cloud Save 하우징 테스트 저장 실패.");
            }
            finally
            {
                cloudOnlySaveInProgress = false;
            }
        }

        private static CloudSaveSystem ResolveLoadedCloudSaveSystem()
        {
            CloudSaveSystem[] systems = FindObjectsByType<CloudSaveSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].HasLoadedData)
                    return systems[i];
            }

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
    }
}
#endif

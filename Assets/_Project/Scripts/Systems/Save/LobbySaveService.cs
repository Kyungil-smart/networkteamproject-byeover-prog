using System.Threading.Tasks;
using DeadZone.Core;
using DeadZone.Network;
using Sirenix.OdinInspector;
using UnityEngine;
using System.Collections;

namespace DeadZone.Systems.Save
{
    public class LobbySaveService : MonoBehaviour
    {
        [Header("저장 데이터 수집기")]
        [SerializeField] private InventorySaveCollector inventorySaveCollector;
        [SerializeField] private FacilitySaveCollector facilitySaveCollector;

        [Header("저장 상태")]
        [SerializeField] private LobbyInventoryState inventoryState;
        [SerializeField] private LobbyFacilityState facilityState;
        [SerializeField] private LobbyInventoryStateUiBridge inventoryStateUiBridge;

        [Header("서버 저장")]
        [SerializeField] private CloudSaveSystem cloudSaveSystem;
        [SerializeField] private bool loadFromCloudOnStart = true;
        [SerializeField] private bool saveToCloudOnApplicationPause = true;
        [SerializeField] private bool saveToCloudOnApplicationQuit = true;

        [Header("테스트 JSON")]
        [TextArea(8, 20)]
        [SerializeField] private string lastJson;

        private bool isCloudSaveRunning;

        private void Start()
        {
            if (loadFromCloudOnStart)
                StartCoroutine(LoadLobbyDataFromCloudAfterUiReady());
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

            SaveLobbyDataToCloud();
        }

        [Button("로비 저장 JSON 출력")]
        public void SaveLobbyData()
        {
            LobbySaveDTO dto = CreateCurrentLobbySaveDTO();

            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;
            Debug.Log($"[LobbySaveService] Lobby Save JSON\n{json}", this);
        }

        [Button("Firebase에 로비 저장")]
        public async void SaveLobbyDataToCloud()
        {
            await SaveLobbyDataToCloudAsync();
        }

        public async Task<bool> SaveLobbyDataToCloudAsync()
        {
            if (isCloudSaveRunning)
            {
                Debug.LogWarning("[LobbySaveService] 이미 Firebase 로비 저장이 진행 중입니다.", this);
                return false;
            }

            LobbySaveDTO dto = CreateCurrentLobbySaveDTO();

            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;
            Debug.Log($"[LobbySaveService] Firebase Save Request JSON\n{json}", this);

            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();
            if (saveSystem == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem을 찾지 못했습니다. PersistentSystems에 CloudSaveSystem이 있는지 확인하세요.", this);
                return false;
            }

            isCloudSaveRunning = true;

            try
            {
                bool success = await saveSystem.SaveLobbyDataAsync(dto);
                Debug.Log(success
                    ? "[LobbySaveService] Firebase 로비 저장 성공"
                    : "[LobbySaveService] Firebase 로비 저장 실패", this);

                return success;
            }
            finally
            {
                isCloudSaveRunning = false;
            }
        }

        [Button("마지막 JSON 로드")]
        public void LoadLastJson()
        {
            LoadLobbyDataFromJson(lastJson);
        }

        [Button("Firebase에서 로비 로드")]
        public void LoadLobbyDataFromCloud()
        {
            CloudSaveSystem saveSystem = ResolveCloudSaveSystem();
            if (saveSystem == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem을 찾지 못했습니다. PersistentSystems에 CloudSaveSystem이 있는지 확인하세요.", this);
                return;
            }

            LobbySaveDTO dto = saveSystem.CreateLobbySaveDTOFromCurrentData();
            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] CloudSaveSystem에서 로비 저장 데이터를 가져오지 못했습니다.", this);
                return;
            }

            string json = JsonUtility.ToJson(dto, true);
            lastJson = json;
            LoadLobbyDataFromJson(json);
        }

        public void LoadLobbyDataFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[LobbySaveService] 로드할 JSON이 비어 있습니다.", this);
                return;
            }

            LobbySaveDTO dto;

            try
            {
                dto = JsonUtility.FromJson<LobbySaveDTO>(json);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[LobbySaveService] JSON 파싱 실패: {exception.Message}", this);
                return;
            }

            if (dto == null)
            {
                Debug.LogWarning("[LobbySaveService] JSON에서 LobbySaveDTO를 만들지 못했습니다.", this);
                return;
            }

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
                Debug.LogWarning("[LobbySaveService] LobbyInventoryState가 연결되지 않아 인벤토리 상태를 복원하지 못했습니다.", this);
            }

            if (facilityState != null)
            {
                facilityState.SetFacilities(dto.facilities);
            }
            else
            {
                Debug.LogWarning("[LobbySaveService] LobbyFacilityState가 연결되지 않아 시설 상태를 복원하지 못했습니다.", this);
            }

            if (inventoryStateUiBridge != null)
                inventoryStateUiBridge.ApplyStateToUi();
            else
                Debug.LogWarning("[LobbySaveService] LobbyInventoryStateUiBridge가 연결되지 않아 UI를 갱신하지 못했습니다.", this);

            Debug.Log("[LobbySaveService] Lobby Save JSON 로드 완료", this);
        }

        private LobbySaveDTO CreateCurrentLobbySaveDTO()
        {
            LobbySaveDTO dto = new LobbySaveDTO();

            if (inventorySaveCollector != null)
                inventorySaveCollector.Collect(dto);
            else
                Debug.LogWarning("[LobbySaveService] InventorySaveCollector가 연결되지 않았습니다.", this);

            if (facilitySaveCollector != null)
                facilitySaveCollector.Collect(dto);
            else
                Debug.LogWarning("[LobbySaveService] FacilitySaveCollector가 연결되지 않았습니다.", this);

            return dto;
        }

        private IEnumerator LoadLobbyDataFromCloudAfterUiReady()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            LoadLobbyDataFromCloud();
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
    }
}

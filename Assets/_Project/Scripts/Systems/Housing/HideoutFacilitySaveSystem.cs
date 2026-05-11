using System.Collections.Generic;
using System.IO;

using Unity.Netcode;
using UnityEngine;

using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    // 은신처 시설 레벨을 JSON 파일로 저장하고 로드
    // 서버가 저장 데이터를 읽어 FacilityBase.CurrentLevel에 적용하면 클라이언트는 NetworkVariable로 자동 동기화
    [DisallowMultipleComponent]
    public sealed class HideoutFacilitySaveSystem : NetworkBehaviour
    {
        [Header("저장 대상 시설")]
        [SerializeField]
        private List<FacilityBase> facilities = new();

        [Header("자동 수집")]
        [SerializeField]
        private bool autoFindFacilitiesOnSpawn = true;

        [Header("저장 파일")]
        [SerializeField]
        private string saveFileName = "hideout_facility_save.json";

        [Header("저장 옵션")]
        [SerializeField]
        private bool saveWhenFacilityLevelChanged = true;

        [SerializeField]
        private bool saveOnDespawn = true;

        [Header("로그")]
        [SerializeField]
        private bool logSaveLoad = true;

        private bool isApplyingLoadedData;

        private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            if (autoFindFacilitiesOnSpawn)
                CollectFacilitiesInScene();

            LoadAndApplyLevels();
            SubscribeFacilityLevelChanged();
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            UnsubscribeFacilityLevelChanged();

            if (saveOnDespawn)
                SaveCurrentLevels();
        }

        private void OnApplicationQuit()
        {
            if (!Application.isPlaying)
                return;

            if (!IsServer)
                return;

            SaveCurrentLevels();
        }

        public void CollectFacilitiesInScene()
        {
            facilities.Clear();

            FacilityBase[] foundFacilities = FindObjectsByType<FacilityBase>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            for (int i = 0; i < foundFacilities.Length; i++)
            {
                FacilityBase facility = foundFacilities[i];

                if (facility == null)
                    continue;

                HousingFacilitySaveKey saveKey = facility.GetComponent<HousingFacilitySaveKey>();

                if (saveKey == null || !saveKey.IsValid)
                {
                    Debug.LogWarning(
                        $"[HideoutFacilitySaveSystem] 저장 키가 없는 시설은 저장 대상에서 제외됩니다: {facility.name}",
                        facility
                    );

                    continue;
                }

                if (!facilities.Contains(facility))
                    facilities.Add(facility);
            }

            if (logSaveLoad)
                Debug.Log($"[HideoutFacilitySaveSystem] 저장 대상 시설 수집 완료: {facilities.Count}개", this);
        }

        public void SaveCurrentLevels()
        {
            if (!IsServer)
                return;

            HideoutFacilitySaveData saveData = new HideoutFacilitySaveData();

            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                HousingFacilitySaveKey saveKey = facility.GetComponent<HousingFacilitySaveKey>();

                if (saveKey == null || !saveKey.IsValid)
                    continue;

                int currentLevel = Mathf.Clamp(
                    facility.GetCurrentLevel(),
                    1,
                    facility.GetMaxLevel()
                );

                saveData.facilities.Add(new FacilityLevelSaveData
                {
                    facilityId = saveKey.FacilityId,
                    level = currentLevel
                });
            }

            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SavePath, json);

            if (logSaveLoad)
                Debug.Log($"[HideoutFacilitySaveSystem] 시설 레벨 저장 완료: {SavePath}", this);
        }

        public void LoadAndApplyLevels()
        {
            if (!IsServer)
                return;

            if (!File.Exists(SavePath))
            {
                if (logSaveLoad)
                    Debug.Log($"[HideoutFacilitySaveSystem] 저장 파일이 없습니다. 기본 시설 레벨을 사용합니다: {SavePath}", this);

                return;
            }

            string json = File.ReadAllText(SavePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[HideoutFacilitySaveSystem] 저장 파일이 비어 있습니다.", this);
                return;
            }

            HideoutFacilitySaveData saveData = JsonUtility.FromJson<HideoutFacilitySaveData>(json);

            if (saveData == null || saveData.facilities == null)
            {
                Debug.LogWarning("[HideoutFacilitySaveSystem] 저장 데이터 파싱에 실패했습니다.", this);
                return;
            }

            Dictionary<string, int> levelByFacilityId = new();

            for (int i = 0; i < saveData.facilities.Count; i++)
            {
                FacilityLevelSaveData savedFacility = saveData.facilities[i];

                if (savedFacility == null)
                    continue;

                if (string.IsNullOrWhiteSpace(savedFacility.facilityId))
                    continue;

                levelByFacilityId[savedFacility.facilityId] = Mathf.Max(1, savedFacility.level);
            }

            isApplyingLoadedData = true;

            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                HousingFacilitySaveKey saveKey = facility.GetComponent<HousingFacilitySaveKey>();

                if (saveKey == null || !saveKey.IsValid)
                    continue;

                if (!levelByFacilityId.TryGetValue(saveKey.FacilityId, out int savedLevel))
                    continue;

                int clampedLevel = Mathf.Clamp(savedLevel, 1, facility.GetMaxLevel());
                facility.CurrentLevel.Value = clampedLevel;

                if (logSaveLoad)
                {
                    Debug.Log(
                        $"[HideoutFacilitySaveSystem] 시설 레벨 로드 적용: {saveKey.FacilityId} Lv.{clampedLevel}",
                        facility
                    );
                }
            }

            isApplyingLoadedData = false;
        }

        private void SubscribeFacilityLevelChanged()
        {
            if (!saveWhenFacilityLevelChanged)
                return;

            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                facility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
                facility.CurrentLevel.OnValueChanged += HandleFacilityLevelChanged;
            }
        }

        private void UnsubscribeFacilityLevelChanged()
        {
            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                facility.CurrentLevel.OnValueChanged -= HandleFacilityLevelChanged;
            }
        }

        private void HandleFacilityLevelChanged(int previousLevel, int currentLevel)
        {
            if (!IsServer)
                return;

            if (isApplyingLoadedData)
                return;

            if (!saveWhenFacilityLevelChanged)
                return;

            SaveCurrentLevels();
        }

#if UNITY_EDITOR
        [ContextMenu("저장 대상 시설 다시 수집")]
        private void DebugCollectFacilities()
        {
            CollectFacilitiesInScene();
        }

        [ContextMenu("현재 시설 레벨 저장")]
        private void DebugSaveCurrentLevels()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HideoutFacilitySaveSystem] 플레이 중에만 저장할 수 있습니다.", this);
                return;
            }

            SaveCurrentLevels();
        }

        [ContextMenu("저장된 시설 레벨 로드")]
        private void DebugLoadCurrentLevels()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HideoutFacilitySaveSystem] 플레이 중에만 로드할 수 있습니다.", this);
                return;
            }

            LoadAndApplyLevels();
        }

        [ContextMenu("시설 저장 파일 삭제")]
        private void DebugDeleteSaveFile()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log($"[HideoutFacilitySaveSystem] 저장 파일 삭제 완료: {SavePath}", this);
            }
            else
            {
                Debug.Log($"[HideoutFacilitySaveSystem] 삭제할 저장 파일이 없습니다: {SavePath}", this);
            }
        }
#endif
    }
}
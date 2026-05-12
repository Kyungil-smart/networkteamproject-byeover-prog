using System.Collections.Generic;
using System.Collections;
using DeadZone.Core;
using DeadZone.Systems;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class HideoutFacilitySaveBinder : MonoBehaviour
    {
        [Header("저장 상태")]
        [SerializeField] private LobbyFacilityState facilityState;

        [Header("시설 참조")]
        [SerializeField] private FacilityBase[] facilities;

        [Header("동기화")]
        [SerializeField] private bool captureOnStart = true;

        private void Start()
        {
            if (!captureOnStart)
                return;

            StartCoroutine(ApplyAfterSaveLoadReady());
        }

        private IEnumerator ApplyAfterSaveLoadReady()
        {
            LobbySaveService saveService = FindFirstObjectByType<LobbySaveService>(FindObjectsInactive.Include);
            int remainingFrames = 180;

            while (saveService != null && !saveService.IsInitialLoadCompleted && remainingFrames > 0)
            {
                remainingFrames--;
                yield return null;
            }

            if (saveService != null && !saveService.IsInitialLoadCompleted)
            {
                Debug.LogWarning("[Save] Save skipped because load is not completed yet.", this);
                yield break;
            }

            if (HasSavedFacilityState())
                ApplyStateToFacilities();
            else
            {
                Debug.Log("[Facility] Default level generated. reason=No saved facility state after load completed", this);
                CaptureFacilitiesToState();
            }
        }

        [Button("시설 상태를 저장 상태로 반영")]
        public void CaptureFacilitiesToState()
        {
            if (facilityState == null)
            {
                Debug.LogWarning("[HideoutFacilitySaveBinder] LobbyFacilityState가 연결되지 않았습니다.", this);
                return;
            }

            List<FacilitySaveDTO> capturedFacilities = new();

            if (facilities == null || facilities.Length == 0)
            {
                Debug.LogWarning("[HideoutFacilitySaveBinder] FacilityBase 파생 시설 참조가 비어 있습니다.", this);
                facilityState.SetFacilities(capturedFacilities);
                return;
            }

            for (int i = 0; i < facilities.Length; i++)
            {
                FacilityBase facility = facilities[i];
                if (facility == null)
                    continue;

                capturedFacilities.Add(new FacilitySaveDTO
                {
                    facilityId = GetFacilityId(facility),
                    level = facility.GetCurrentLevel()
                });
            }

            facilityState.SetFacilities(capturedFacilities);
        }

        [Button("????곹깭瑜??쒖꽕濡??곸슜")]
        public void ApplyStateToFacilities()
        {
            if (facilityState == null)
            {
                Debug.LogWarning("[HideoutFacilitySaveBinder] LobbyFacilityState媛 ?곌껐?섏? ?딆븯?듬땲??", this);
                return;
            }

            if (facilities == null || facilities.Length == 0)
            {
                Debug.LogWarning("[HideoutFacilitySaveBinder] FacilityBase ?뚯깮 ?쒖꽕 李몄“媛 鍮꾩뼱 ?덉뒿?덈떎.", this);
                return;
            }

            for (int i = 0; i < facilities.Length; i++)
            {
                FacilityBase facility = facilities[i];

                if (facility == null)
                    continue;

                if (!TryGetSavedLevel(facility, out int savedLevel))
                    continue;

                if (!facility.CanSetLevel(savedLevel))
                    continue;

                if (facility.IsSpawned && !facility.IsServer)
                    continue;

                int previousLevel = facility.GetCurrentLevel();
                facility.CurrentLevel.Value = savedLevel;
                Debug.Log(
                    $"[Facility] Apply level. type={facility.Type}, loadedLevel={savedLevel}, previousLevel={previousLevel}, finalLevel={facility.CurrentLevel.Value}",
                    facility);
            }
        }

        private bool HasSavedFacilityState()
        {
            return facilityState != null &&
                   facilityState.Facilities != null &&
                   facilityState.Facilities.Count > 0;
        }

        private bool TryGetSavedLevel(FacilityBase facility, out int level)
        {
            level = 1;

            if (facility == null || facilityState == null || facilityState.Facilities == null)
                return false;

            string facilityId = NormalizeFacilityId(GetFacilityId(facility));

            for (int i = 0; i < facilityState.Facilities.Count; i++)
            {
                FacilitySaveDTO savedFacility = facilityState.Facilities[i];

                if (savedFacility == null)
                    continue;

                if (NormalizeFacilityId(savedFacility.facilityId) != facilityId)
                    continue;

                level = savedFacility.level;
                return true;
            }

            return false;
        }

        private static string GetFacilityId(FacilityBase facility)
        {
            if (facility == null)
                return string.Empty;

            FacilityDataSO facilityData = facility.GetFacilityData();
            if (facilityData != null)
                return facilityData.type.ToString();

            return facility.GetType().Name;
        }

        private static string NormalizeFacilityId(string facilityId)
        {
            return string.IsNullOrWhiteSpace(facilityId)
                ? string.Empty
                : facilityId.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

#if UNITY_EDITOR
        [Button("시설 자동 탐색")]
        private void AutoFindFacilities()
        {
            if (facilityState == null)
                facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            facilities = FindObjectsByType<FacilityBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
#endif
    }
}

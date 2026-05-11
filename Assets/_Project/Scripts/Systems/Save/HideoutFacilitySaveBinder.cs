using System.Collections.Generic;
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
            if (captureOnStart)
                CaptureFacilitiesToState();
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

        private static string GetFacilityId(FacilityBase facility)
        {
            if (facility == null)
                return string.Empty;

            FacilityDataSO facilityData = facility.GetFacilityData();
            if (facilityData != null)
                return facilityData.type.ToString();

            return facility.GetType().Name;
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

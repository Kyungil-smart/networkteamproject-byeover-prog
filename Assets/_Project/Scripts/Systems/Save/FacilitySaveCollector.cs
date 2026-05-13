using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class FacilitySaveCollector : MonoBehaviour
    {
        [Header("시설 저장 상태")]
        [SerializeField] private LobbyFacilityState facilityState;

        public void Collect(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            if (facilityState == null)
            {
                Debug.LogWarning("[FacilitySaveCollector] LobbyFacilityState가 연결되지 않았습니다. Hideout 씬과 공유할 시설 저장 상태 오브젝트를 연결해야 합니다.", this);
                return;
            }

            dto.facilities.AddRange(facilityState.Facilities);
        }

#if UNITY_EDITOR
        [Button("참조 자동 탐색")]
        private void AutoFindReferences()
        {
            if (facilityState == null)
                facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);
        }
#endif
    }
}

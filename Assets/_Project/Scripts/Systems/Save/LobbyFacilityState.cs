using System.Collections.Generic;
using DeadZone.Core;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Systems.Save
{
    public class LobbyFacilityState : MonoBehaviour
    {
        [Header("씬 유지")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("저장 상태")]
        [SerializeField] private List<FacilitySaveDTO> facilities = new();

        public IReadOnlyList<FacilitySaveDTO> Facilities => facilities;

        private void Awake()
        {
            if (dontDestroyOnLoad)
            {
                Debug.Log(
                    "[LobbyFacilityState] dontDestroyOnLoad is ignored. Lobby facility state is a scene-local cache; CloudSaveSystem is the persistent authority.",
                    this);
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<AuthSignedOutEvent>(HandleAuthSignedOut);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<AuthSignedOutEvent>(HandleAuthSignedOut);
        }

        public void SetFacilities(IEnumerable<FacilitySaveDTO> nextFacilities)
        {
            facilities.Clear();

            if (nextFacilities == null)
                return;

            facilities.AddRange(nextFacilities);
        }

        public void SetFacilityLevel(string facilityId, int level)
        {
            if (string.IsNullOrWhiteSpace(facilityId))
                return;

            for (int i = 0; i < facilities.Count; i++)
            {
                if (facilities[i].facilityId != facilityId)
                    continue;

                facilities[i].level = level;
                return;
            }

            facilities.Add(new FacilitySaveDTO
            {
                facilityId = facilityId,
                level = level
            });
        }

        [Button("시설 상태 비우기")]
        public void Clear()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                Debug.LogWarning("[Party] WARNING: Save data reset attempted during party creation. caller=LobbyFacilityState.Clear", this);

            facilities.Clear();
        }

        private void HandleAuthSignedOut(AuthSignedOutEvent e)
        {
            Clear();
        }
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// Owner 측 관전자 로직. 살아있는 팀원들을 폴링하고 유저가 Q/E로 순환할 수 있게 한다.
    /// Tab으로 자유 카메라 토글.
    /// </summary>
    public class SpectatorController : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private SpectatorCamera spectatorCamera;
        [SerializeField] private float refreshIntervalSeconds = 1.5f;

        private readonly List<NetworkObject> aliveTeammates = new();
        private int currentIndex = -1;
        private bool freeCamActive;
        private float lastRefreshTime;
        private PlayerHealthSystem health;

        public bool IsActive { get; private set; }
        public bool IsFreeCam => freeCamActive;
        public NetworkObject CurrentTarget =>
            (currentIndex >= 0 && currentIndex < aliveTeammates.Count) ? aliveTeammates[currentIndex] : null;

        private void Awake()
        {
            if (spectatorCamera == null) spectatorCamera = GetComponentInChildren<SpectatorCamera>();
            health = GetComponent<PlayerHealthSystem>();
        }

        public override void OnNetworkSpawn()
        {
            if (health != null)
            {
                health.State.OnValueChanged += OnPlayerStateChanged;
                ApplyState(health.State.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.State.OnValueChanged -= OnPlayerStateChanged;
            SetActive(false);
        }

        private void OnPlayerStateChanged(PlayerState oldState, PlayerState newState)
        {
            ApplyState(newState);
        }

        private void ApplyState(PlayerState state)
        {
            if (!IsOwner) return;

            if (state == PlayerState.Dead)
            {
                SetActive(true);
                RefreshTeammates();
                if (aliveTeammates.Count > 0)
                {
                    currentIndex = 0;
                    UpdateCameraTarget();
                }
                else
                {
                    freeCamActive = true;
                    UpdateCameraTarget();
                }
            }
            else
            {
                SetActive(false);
            }
        }

        private void Update()
        {
            if (!IsOwner || !IsActive) return;

            if (Time.time - lastRefreshTime > refreshIntervalSeconds)
            {
                RefreshTeammates();
                if (CurrentTarget == null && aliveTeammates.Count > 0)
                {
                    currentIndex = 0;
                    UpdateCameraTarget();
                }
            }

            if (CurrentTarget == null && !freeCamActive && aliveTeammates.Count == 0)
            {
                freeCamActive = true;
                UpdateCameraTarget();
            }
        }

        public void SwitchTo(int direction)
        {
            if (!IsOwner || !IsActive) return;
            if (freeCamActive)
            {
                freeCamActive = false;
            }

            if (aliveTeammates.Count == 0)
            {
                freeCamActive = true;
                UpdateCameraTarget();
                return;
            }

            currentIndex = (currentIndex + direction + aliveTeammates.Count) % aliveTeammates.Count;
            UpdateCameraTarget();
        }

        public void ToggleFreeCam()
        {
            if (!IsOwner || !IsActive) return;
            freeCamActive = !freeCamActive;
            UpdateCameraTarget();
        }

        public void SetFreeCamInput(Vector2 move, Vector2 look)
        {
            if (spectatorCamera != null) spectatorCamera.SetFreeCamInput(move, look);
        }

        private void RefreshTeammates()
        {
            lastRefreshTime = Time.time;
            aliveTeammates.Clear();

            if (NetworkManager.Singleton == null) return;
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                if (kvp.Key == OwnerClientId) continue;
                var po = kvp.Value.PlayerObject;
                if (po == null) continue;
                var theirHealth = po.GetComponent<PlayerHealthSystem>();
                if (theirHealth != null && theirHealth.IsAlive)
                {
                    aliveTeammates.Add(po);
                }
            }
        }

        private void UpdateCameraTarget()
        {
            if (spectatorCamera == null) return;
            if (freeCamActive)
            {
                spectatorCamera.SetFreeCam();
            }
            else if (CurrentTarget != null)
            {
                spectatorCamera.FollowTarget(CurrentTarget.transform);
            }

            EventBus.Publish(new SpectatorTargetChangedEvent
            {
                spectatorClientId = OwnerClientId,
                newTargetClientId = CurrentTarget != null ? CurrentTarget.OwnerClientId : ulong.MaxValue,
            });
        }

        private void SetActive(bool active)
        {
            IsActive = active;
            if (spectatorCamera != null) spectatorCamera.gameObject.SetActive(active);
        }
    }
}

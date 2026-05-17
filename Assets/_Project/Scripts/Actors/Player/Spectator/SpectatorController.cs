using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// Owner 로컬 플레이어가 Dead 상태일 때 기존 PlayerCameraController의 추적 대상만 팀원으로 바꾼다.
    /// FreeCam과 다른 플레이어의 Camera/AudioListener 활성화는 사용하지 않는다.
    /// </summary>
    public class SpectatorController : NetworkBehaviour
    {
        private const string SpectatorTargetName = "SpectatorTarget";

        [Header("Refs")]
        [SerializeField] private PlayerCameraController playerCameraController;
        [SerializeField] private SpectatorCamera legacySpectatorCamera;
        [SerializeField] private float refreshIntervalSeconds = 1.5f;

        private readonly List<NetworkObject> spectatorTargets = new();
        private int currentIndex = -1;
        private float lastRefreshTime;
        private PlayerHealthSystem health;

        public bool IsActive { get; private set; }
        public bool IsFreeCam => false;
        public NetworkObject CurrentTarget =>
            (currentIndex >= 0 && currentIndex < spectatorTargets.Count) ? spectatorTargets[currentIndex] : null;

        private void Awake()
        {
            if (playerCameraController == null) playerCameraController = GetComponent<PlayerCameraController>();
            if (legacySpectatorCamera == null) legacySpectatorCamera = GetComponentInChildren<SpectatorCamera>(true);
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
                RefreshTargets(ulong.MaxValue, keepPreferred: false, preferNextAfterPreferred: false);
                UpdateCameraTarget();
            }
            else
            {
                SetActive(false);
            }
        }

        private void Update()
        {
            if (!IsOwner || !IsActive) return;

            NetworkObject beforeTarget = CurrentTarget;
            ulong beforeClientId = beforeTarget != null ? beforeTarget.OwnerClientId : ulong.MaxValue;
            bool targetMissing = beforeTarget == null && currentIndex >= 0;
            bool targetInvalid = beforeTarget != null && !IsValidSpectatorTarget(beforeTarget);
            bool shouldRefresh = targetMissing || targetInvalid || Time.time - lastRefreshTime > refreshIntervalSeconds;

            if (shouldRefresh)
            {
                RefreshTargets(
                    beforeClientId,
                    keepPreferred: !targetInvalid,
                    preferNextAfterPreferred: beforeTarget != null);
            }

            if (beforeTarget != CurrentTarget || targetMissing || targetInvalid)
            {
                UpdateCameraTarget();
            }
        }

        public void SwitchTo(int direction)
        {
            if (!IsOwner || !IsActive) return;

            NetworkObject beforeTarget = CurrentTarget;
            ulong beforeClientId = beforeTarget != null ? beforeTarget.OwnerClientId : ulong.MaxValue;
            RefreshTargets(beforeClientId, keepPreferred: true, preferNextAfterPreferred: false);

            if (spectatorTargets.Count == 0)
            {
                currentIndex = -1;
                UpdateCameraTarget();
                return;
            }

            if (currentIndex < 0)
            {
                currentIndex = direction >= 0 ? 0 : spectatorTargets.Count - 1;
            }
            else
            {
                currentIndex = (currentIndex + direction + spectatorTargets.Count) % spectatorTargets.Count;
            }

            UpdateCameraTarget();
        }

        public void ToggleFreeCam()
        {
            // 최종 규칙상 FreeCam은 사용하지 않는다. 기존 입력 인터페이스 호환용 no-op.
        }

        public void SetFreeCamInput(Vector2 move, Vector2 look)
        {
            // 최종 규칙상 FreeCam 이동 입력은 소비하지 않는다.
        }

        private void RefreshTargets(ulong preferredClientId, bool keepPreferred, bool preferNextAfterPreferred)
        {
            lastRefreshTime = Time.time;
            spectatorTargets.Clear();

            if (NetworkManager.Singleton == null) return;
            if (NetworkManager.Singleton.SpawnManager == null) return;
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjectsList == null) return;

            foreach (NetworkObject spawnedObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
            {
                if (IsValidSpectatorTarget(spawnedObject))
                    spectatorTargets.Add(spawnedObject);
            }

            spectatorTargets.Sort(CompareByOwnerClientId);

            if (spectatorTargets.Count == 0)
            {
                currentIndex = -1;
                return;
            }

            if (keepPreferred && TryFindIndexByClientId(preferredClientId, out int preferredIndex))
            {
                currentIndex = preferredIndex;
                return;
            }

            if (preferNextAfterPreferred && preferredClientId != ulong.MaxValue)
            {
                currentIndex = FindNextIndexAfter(preferredClientId);
                return;
            }

            currentIndex = SelectInitialIndex();
        }

        private void UpdateCameraTarget()
        {
            NetworkObject target = CurrentTarget;
            bool hasTarget = IsValidSpectatorTarget(target);
            PlayerState targetState = PlayerState.Dead;

            if (hasTarget)
            {
                PlayerHealthSystem targetHealth = target.GetComponent<PlayerHealthSystem>();
                targetState = targetHealth.State.Value;
                playerCameraController?.SetFollowTarget(ResolveSpectatorFollowTarget(target));
            }
            else
            {
                currentIndex = -1;
                playerCameraController?.ResetFollowTargetToOwner();
            }

            EventBus.Publish(new SpectatorTargetChangedEvent
            {
                spectatorClientId = OwnerClientId,
                newTargetClientId = hasTarget ? target.OwnerClientId : ulong.MaxValue,
                hasTarget = hasTarget,
                targetState = targetState,
            });
        }

        private void SetActive(bool active)
        {
            IsActive = active;

            DisableLegacySpectatorCamera();

            if (!active)
            {
                currentIndex = -1;
                spectatorTargets.Clear();
                playerCameraController?.ResetFollowTargetToOwner();
            }
        }

        private bool IsValidSpectatorTarget(NetworkObject target)
        {
            if (target == null || !target.IsSpawned)
                return false;

            if (target.OwnerClientId == OwnerClientId)
                return false;

            PlayerHealthSystem targetHealth = target.GetComponent<PlayerHealthSystem>();
            if (targetHealth == null)
                return false;

            PlayerState state = targetHealth.State.Value;
            return state == PlayerState.Alive || state == PlayerState.Knocked;
        }

        private void DisableLegacySpectatorCamera()
        {
            if (legacySpectatorCamera == null)
                return;

            legacySpectatorCamera.enabled = false;

            if (legacySpectatorCamera.gameObject == gameObject)
                return;

            Camera[] cameras = legacySpectatorCamera.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameras[i].enabled = false;
            }

            AudioListener[] listeners = legacySpectatorCamera.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null)
                    listeners[i].enabled = false;
            }
        }

        private int SelectInitialIndex()
        {
            for (int i = 0; i < spectatorTargets.Count; i++)
            {
                if (TryGetTargetState(i, out PlayerState state) && state == PlayerState.Alive)
                    return i;
            }

            for (int i = 0; i < spectatorTargets.Count; i++)
            {
                if (TryGetTargetState(i, out PlayerState state) && state == PlayerState.Knocked)
                    return i;
            }

            return spectatorTargets.Count > 0 ? 0 : -1;
        }

        private bool TryGetTargetState(int index, out PlayerState state)
        {
            state = PlayerState.Dead;

            if (index < 0 || index >= spectatorTargets.Count)
                return false;

            PlayerHealthSystem targetHealth = spectatorTargets[index] != null
                ? spectatorTargets[index].GetComponent<PlayerHealthSystem>()
                : null;

            if (targetHealth == null)
                return false;

            state = targetHealth.State.Value;
            return true;
        }

        private bool TryFindIndexByClientId(ulong clientId, out int index)
        {
            for (int i = 0; i < spectatorTargets.Count; i++)
            {
                NetworkObject target = spectatorTargets[i];
                if (target != null && target.OwnerClientId == clientId)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private int FindNextIndexAfter(ulong clientId)
        {
            for (int i = 0; i < spectatorTargets.Count; i++)
            {
                NetworkObject target = spectatorTargets[i];
                if (target != null && target.OwnerClientId > clientId)
                    return i;
            }

            return spectatorTargets.Count > 0 ? 0 : -1;
        }

        private Transform ResolveSpectatorFollowTarget(NetworkObject target)
        {
            Transform spectatorTarget = FindChildRecursive(target.transform, SpectatorTargetName);
            return spectatorTarget != null ? spectatorTarget : target.transform;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                    return child;

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static int CompareByOwnerClientId(NetworkObject left, NetworkObject right)
        {
            if (left == right)
                return 0;

            if (left == null)
                return 1;

            if (right == null)
                return -1;

            int ownerCompare = left.OwnerClientId.CompareTo(right.OwnerClientId);
            return ownerCompare != 0
                ? ownerCompare
                : left.NetworkObjectId.CompareTo(right.NetworkObjectId);
        }
    }
}

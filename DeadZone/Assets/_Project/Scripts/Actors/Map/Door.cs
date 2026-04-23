using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// v1.1 §6 — 서버 권위 문. isOpen NetworkVariable이 Collider와 NavMeshObstacle을
    /// 동시에 토글한다. 닫힌 상태는 AI 시야를 막는다 —
    /// BoxCollider가 Door 레이어에 있기 때문 (EnemyVision.obstacleMask에 포함).
    /// </summary>
    public class Door : NetworkBehaviour, IInteractable
    {
        [Header("Refs")]
        [SerializeField] private Transform hingeTransform;
        [SerializeField] private BoxCollider doorCollider;
        [SerializeField] private NavMeshObstacle navObstacle;

        [Header("Config")]
        [SerializeField] private bool isLocked = false;
        [SerializeField] private string requiredKeyId = "";
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float openSpeed = 3f;
        [SerializeField] private bool initiallyOpen = false;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;

        public NetworkVariable<bool> IsOpen = new(false);

        public override void OnNetworkSpawn()
        {
            if (IsServer) IsOpen.Value = initiallyOpen;
            IsOpen.OnValueChanged += OnDoorStateChanged;
            ApplyVisualState(IsOpen.Value);
            ApplyPhysicsState(IsOpen.Value);
        }

        public override void OnNetworkDespawn()
        {
            IsOpen.OnValueChanged -= OnDoorStateChanged;
        }

        public void OnInteract(ulong clientId)
        {
            RequestToggleServerRpc();
        }

        public string GetPromptText() => IsOpen.Value ? "[F] Close" : (isLocked ? "[F] Locked" : "[F] Open");

        [ServerRpc(RequireOwnership = false)]
        private void RequestToggleServerRpc(ServerRpcParams rpc = default)
        {
            if (isLocked && !string.IsNullOrEmpty(requiredKeyId)) return;
            IsOpen.Value = !IsOpen.Value;
        }

        private void OnDoorStateChanged(bool oldVal, bool newVal)
        {
            ApplyPhysicsState(newVal);
            if (audioSource != null)
            {
                var clip = newVal ? openSound : closeSound;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
            EventBus.Publish(new DoorStateChangedEvent { position = transform.position, isOpen = newVal });
        }

        private void ApplyPhysicsState(bool open)
        {
            if (doorCollider != null) doorCollider.enabled = !open;
            if (navObstacle != null) navObstacle.enabled = !open;
        }

        private void ApplyVisualState(bool open)
        {
            if (hingeTransform != null)
            {
                hingeTransform.localRotation = Quaternion.Euler(0, open ? openAngle : 0, 0);
            }
        }

        private void Update()
        {
            if (hingeTransform == null) return;
            float target = IsOpen.Value ? openAngle : 0f;
            var current = hingeTransform.localEulerAngles;
            float currentY = Mathf.LerpAngle(current.y, target, Time.deltaTime * openSpeed);
            hingeTransform.localRotation = Quaternion.Euler(0, currentY, 0);
        }
    }
}

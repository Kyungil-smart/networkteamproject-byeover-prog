using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

using DeadZone.Actors;
using DeadZone.Core;

namespace DeadZone.Actors.UI
{
    public class InventoryInputController : MonoBehaviour
    {
        [BoxGroup("UI 연결")]
        [Tooltip("Tab 키로 열고 닫을 InventoryUI입니다.")]
        [SerializeField] private InventoryUI inventoryUI;

        private PlayerHealthSystem ownerHealth;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        private void Update()
        {
            CloseInventoryIfOwnerDead();

            if (Keyboard.current == null)
                return;

            if (InGameMenuUI.IsAnyMenuBlockingInput())
                return;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                ToggleInventory();
            }
        }

        [BoxGroup("테스트")]
        [Button("인벤토리 토글")]
        private void ToggleInventory()
        {
            // Dead 상태에서는 Team Spectator의 Q/E 입력만 유지하고 인벤토리 UI 입력은 열지 않는다.
            if (IsOwnerDead())
            {
                CloseInventoryIfOpen();
                return;
            }

            InventoryUI targetInventoryUI = ResolveInventoryUI();
            if (targetInventoryUI == null)
            {
                Debug.LogWarning("[InventoryInputController] InventoryUI가 연결되지 않았습니다.");
                return;
            }

            targetInventoryUI.Toggle();
        }

        private void OnPlayerStateChanged(PlayerStateChangedEvent evt)
        {
            PlayerHealthSystem health = ResolveOwnerHealth();
            if (health == null || evt.clientId != health.OwnerClientId)
                return;

            if (evt.newState == PlayerState.Dead)
                CloseInventoryIfOpen();
        }

        private void CloseInventoryIfOwnerDead()
        {
            if (IsOwnerDead())
                CloseInventoryIfOpen();
        }

        private bool IsOwnerDead()
        {
            PlayerHealthSystem health = ResolveOwnerHealth();
            return health != null && health.IsDead;
        }

        private PlayerHealthSystem ResolveOwnerHealth()
        {
            if (ownerHealth != null && ownerHealth.IsOwner)
                return ownerHealth;

            ownerHealth = null;

            PlayerHealthSystem[] candidates = FindObjectsByType<PlayerHealthSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < candidates.Length; i++)
            {
                PlayerHealthSystem candidate = candidates[i];
                if (candidate == null || !candidate.IsOwner)
                    continue;

                ownerHealth = candidate;
                return ownerHealth;
            }

            return null;
        }

        private InventoryUI ResolveInventoryUI()
        {
            if (inventoryUI != null)
                return inventoryUI;

            inventoryUI = InventoryUI.ActiveInstance;
            if (inventoryUI != null)
                return inventoryUI;

            inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            return inventoryUI;
        }

        private void CloseInventoryIfOpen()
        {
            InventoryUI targetInventoryUI = ResolveInventoryUI();
            if (targetInventoryUI != null && targetInventoryUI.IsOpen)
                targetInventoryUI.Close();
        }
    }
}

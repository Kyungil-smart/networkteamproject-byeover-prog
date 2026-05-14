using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    public class LootInteractable : NetworkBehaviour, IInteractable, ILootCarrier
    {
        public NetworkVariable<FixedString64Bytes> ItemId = new("");
        public NetworkVariable<ushort> Amount = new(1);

        private ItemDataSO cachedItem;
        private IItemDatabase itemDb;
        private GameObject visualInstance;
        private string visualItemId;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            itemDb = ServiceLocator.Get<IItemDatabase>();

            if (itemDb == null)
            {
                Debug.LogError("[LootInteractable] IItemDatabase 서비스가 등록되어 있지 않음. " +
                               "PersistentSystems > ItemDatabase 가 씬에 있는지 확인.");
            }

            ItemId.OnValueChanged += OnItemIdChanged;
            RefreshWorldVisual();
        }

        public override void OnNetworkDespawn()
        {
            ItemId.OnValueChanged -= OnItemIdChanged;
            DestroyWorldVisual();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || visualInstance != null)
                return;

            RefreshWorldVisual();
        }

        public string GetPromptText()
        {
            if (cachedItem == null) cachedItem = ResolveItem(ItemId.Value.ToString());
            return cachedItem != null ? $"[F] Pick up {cachedItem.displayName}" : "[F] Pick up";
        }

        public void Initialize(ItemDataSO item)
        {
            if (!CanInitializeOnServer() || item == null) return;
            ItemId.Value = item.itemID;
            Amount.Value = 1;
            cachedItem = item;
        }

        public void Initialize(ItemDataSO item, int amount)
        {
            if (!CanInitializeOnServer() || item == null) return;
            ItemId.Value = item.itemID;
            Amount.Value = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue);
            cachedItem = item;
        }

        private bool CanInitializeOnServer()
        {
            if (IsServer)
                return true;

            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null && networkManager.IsServer;
        }

        private void OnItemIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            cachedItem = null;
            RefreshWorldVisual();
        }

        private void RefreshWorldVisual()
        {
            RefreshWorldVisual(ItemId.Value.ToString());
        }

        private void RefreshWorldVisual(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || visualItemId == itemId)
                return;

            ItemDataSO item = ResolveItem(itemId);
            if (item == null || item.worldPrefab == null)
                return;

            DestroyWorldVisual();

            visualInstance = Instantiate(item.worldPrefab, transform, false);
            visualInstance.name = $"{item.worldPrefab.name}_Visual";
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualItemId = itemId;

            StripGameplayComponents(visualInstance);
        }

        public void ForceRefreshWorldVisual()
        {
            if (!IsServer)
                return;

            ForceRefreshWorldVisualClientRpc(ItemId.Value.ToString());
        }

        [ClientRpc]
        private void ForceRefreshWorldVisualClientRpc(string itemId)
        {
            RefreshWorldVisual(itemId);
        }

        private ItemDataSO ResolveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            itemDb ??= ServiceLocator.Get<IItemDatabase>();
            cachedItem = itemDb?.GetById(itemId);
            return cachedItem;
        }

        private void DestroyWorldVisual()
        {
            if (visualInstance != null)
                Destroy(visualInstance);

            visualInstance = null;
            visualItemId = null;
        }

        private static void StripGameplayComponents(GameObject root)
        {
            if (root == null)
                return;

            NetworkBehaviour[] networkBehaviours = root.GetComponentsInChildren<NetworkBehaviour>(true);
            for (int i = 0; i < networkBehaviours.Length; i++)
                Destroy(networkBehaviours[i]);

            NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
            for (int i = 0; i < networkObjects.Length; i++)
                Destroy(networkObjects[i]);

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                Destroy(colliders[i]);

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
                Destroy(rigidbodies[i]);
        }

        public void OnInteract(ulong clientId)
        {
            if (GetComponent<LootContainer>() != null)
            {
                Debug.LogError("[LootInteractable] Dropped item has LootContainer. This prefab is invalid for single item pickup.", this);
                return;
            }

            OpenLootingUI();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryLootServerRpc(RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;
            var playerObj = client.PlayerObject;
            if (playerObj == null) return;

            var inv = playerObj.GetComponent<IInventory>();
            inv ??= playerObj.GetComponentInChildren<IInventory>(true);
            if (inv == null) return;

            // 서버에서도 itemDb 확인
            if (itemDb == null)
            {
                itemDb = ServiceLocator.Get<IItemDatabase>();
                if (itemDb == null) return;
            }

            var item = ResolveItem(ItemId.Value.ToString());
            if (item == null) return;

            int amount = Mathf.Max(1, Amount.Value);
            if (inv.TryAddItem(item, amount))
            {
                EventBus.Publish(new ItemLootedEvent
                {
                    clientId = clientId,
                    itemId = ItemId.Value,
                    amount = amount,
                });
                NetworkObject.Despawn(destroy: true);
            }
            // else: 인벤 가득 → Despawn 안 함, 아이템 그대로 유지
        }

        public bool TryGetSlot(int slotIndex, out ContainerSlotNetData slotData)
        {
            slotData = default;

            if (slotIndex != 0)
                return false;

            string itemId = ItemId.Value.ToString();
            int amount = Mathf.Max(0, Amount.Value);
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
                return false;

            slotData = new ContainerSlotNetData
            {
                itemId = ItemId.Value,
                amount = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue)
            };
            return true;
        }

        public void RequestTakeSlotToPlayer(int slotIndex)
        {
            if (slotIndex != 0)
                return;

            TryLootServerRpc();
        }

        private void OpenLootingUI()
        {
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour.GetType().Name != "LootingUIController")
                    continue;

                if (!behaviour.gameObject.scene.IsValid())
                    continue;

                behaviour.SendMessage("Open", this, SendMessageOptions.DontRequireReceiver);
                return;
            }

            Debug.LogWarning("[LootInteractable] LootingUIController was not found. Place LootingUIController in the scene UI.", this);
        }
    }
}

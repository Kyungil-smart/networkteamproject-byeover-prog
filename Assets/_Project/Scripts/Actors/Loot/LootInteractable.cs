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

        private ItemDataSO cachedItem;
        private IItemDatabase itemDb;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            itemDb = ServiceLocator.Get<IItemDatabase>();

            if (itemDb == null)
            {
                Debug.LogError("[LootInteractable] IItemDatabase 서비스가 등록되어 있지 않음. " +
                               "PersistentSystems > ItemDatabase 가 씬에 있는지 확인.");
            }
        }

        public string GetPromptText()
        {
            if (cachedItem == null) cachedItem = itemDb?.GetById(ItemId.Value.ToString());
            return cachedItem != null ? $"[F] Pick up {cachedItem.displayName}" : "[F] Pick up";
        }

        public void Initialize(ItemDataSO item)
        {
            if (!IsServer || item == null) return;
            ItemId.Value = item.itemID;
            cachedItem = item;
        }

        public void OnInteract(ulong clientId)
        {
            TryLootServerRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryLootServerRpc(RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;
            var playerObj = client.PlayerObject;
            if (playerObj == null) return;

            var inv = playerObj.GetComponent<IInventory>();
            if (inv == null) return;

            // 서버에서도 itemDb 확인
            if (itemDb == null)
            {
                itemDb = ServiceLocator.Get<IItemDatabase>();
                if (itemDb == null) return;
            }

            var item = itemDb.GetById(ItemId.Value.ToString());
            if (item == null) return;

            if (inv.TryAddItem(item))
            {
                EventBus.Publish(new ItemLootedEvent
                {
                    clientId = clientId,
                    itemId = ItemId.Value,
                    amount = 1,
                });
                NetworkObject.Despawn(destroy: true);
            }
            // else: 인벤 가득 → Despawn 안 함, 아이템 그대로 유지
        }
    }
}

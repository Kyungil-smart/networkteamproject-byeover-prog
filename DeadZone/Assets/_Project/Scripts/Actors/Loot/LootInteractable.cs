using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 월드에 스폰된 루팅 아이템. 플레이어가 F 키를 누르면 → ServerRpc → 서버가 인벤토리에 추가 + despawns.
    /// </summary>
    public class LootInteractable : NetworkBehaviour, IInteractable, ILootCarrier
    {
        [SerializeField] private ItemDataSO[] itemDatabase;

        public NetworkVariable<FixedString64Bytes> ItemId = new("");

        private ItemDataSO cachedItem;

        public string GetPromptText()
        {
            if (cachedItem == null) cachedItem = LookupItem(ItemId.Value.ToString());
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

        [ServerRpc(RequireOwnership = false)]
        private void TryLootServerRpc(ServerRpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;
            var playerObj = client.PlayerObject;
            if (playerObj == null) return;

            var inv = playerObj.GetComponent<IInventory>();
            if (inv == null) return;

            var item = LookupItem(ItemId.Value.ToString());
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
        }

        private ItemDataSO LookupItem(string id)
        {
            if (itemDatabase == null) return null;
            foreach (var so in itemDatabase)
            {
                if (so != null && so.itemID == id) return so;
            }
            return null;
        }
    }
}

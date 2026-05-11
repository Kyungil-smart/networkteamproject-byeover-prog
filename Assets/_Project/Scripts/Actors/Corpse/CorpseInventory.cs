using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Actors.UI;

namespace DeadZone.Actors
{
    /// <summary>
    /// PlayerCorpse에 부착되는 서버 권위 인벤토리.
    /// 사망한 플레이어의 GridInventory + EquipmentSlots에서 복사된 ItemSlotData를 저장한다.
    /// 다른 플레이어가 TakeItemServerRpc로 루팅한다.
    /// </summary>
    public class CorpseInventory : NetworkBehaviour
    {
        [Header("Refs")]
        [SerializeField] private ItemDataSO[] itemDatabase;

        public NetworkList<ItemSlotData> Slots;

        public int SlotCount => Slots != null ? Slots.Count : 0;

        private void Awake()
        {
            Slots = new NetworkList<ItemSlotData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);
        }

        /// <summary>
        /// 시체 스폰 직후 서버 측에서 호출되어 사망한 플레이어의 컨테이너로부터
        /// 아이템을 복사한다.
        /// </summary>
        public void PopulateFromPlayer(GridInventory sourceInventory, EquipmentSlots sourceEquipment)
        {
            if (!IsServer) return;
            if (sourceInventory != null)
            {
                for (int i = 0; i < sourceInventory.ServerGrid.Count; i++)
                {
                    Slots.Add(sourceInventory.ServerGrid[i]);
                }
            }
            if (sourceEquipment != null)
            {
                AppendEquippedItem(sourceEquipment.HeadSlotId.Value);
                AppendEquippedItem(sourceEquipment.TorsoSlotId.Value);
                AppendEquippedItem(sourceEquipment.Primary1Id.Value);
                AppendEquippedItem(sourceEquipment.Primary2Id.Value);
                AppendEquippedItem(sourceEquipment.SecondaryId.Value);
                AppendEquippedItem(sourceEquipment.MeleeId.Value);
            }
        }

        private void AppendEquippedItem(Unity.Collections.FixedString64Bytes itemId)
        {
            if (itemId.IsEmpty) return;
            Slots.Add(new ItemSlotData
            {
                itemId = itemId,
                gridX = 0,
                gridY = 0,
                rotated = false,
                stackCount = 1,
                currentDurability = 0,
                currentAmmo = 0,
            });
        }

        /// <summary>루팅하는 플레이어 쪽에서 시체 UI를 연다 — 현재는 이벤트 발행만 한다.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenServerRpc(ulong looterClientId)
        {
        }

        /// <summary>
        /// 로컬 클라이언트의 루팅 UI를 이 시체 인벤토리로 엽니다.
        /// </summary>
        public void OpenLootingUI()
        {
            LootingUIController controller = LootingUIController.ActiveInstance != null
                ? LootingUIController.ActiveInstance
                : Object.FindFirstObjectByType<LootingUIController>(FindObjectsInactive.Include);

            if (controller == null)
            {
                Debug.LogWarning("[CorpseInventory] LootingUIController를 찾지 못했습니다. 씬 UI에 LootingUIController를 배치하세요.", this);
                return;
            }

            controller.Open(this);
        }

        /// <summary>
        /// 지정한 시체 슬롯을 상호작용한 플레이어 인벤토리로 이동하도록 서버에 요청합니다.
        /// </summary>
        /// <param name="slotIndex">가져올 시체 슬롯 인덱스입니다.</param>
        public void RequestTakeSlotToPlayer(int slotIndex)
        {
            TakeItemServerRpc(slotIndex);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TakeItemServerRpc(int slotIndex, ServerRpcParams rpc = default)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count) return;

            ulong looterId = rpc.Receive.SenderClientId;
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(looterId, out var client)) return;

            var playerObj = client.PlayerObject;
            if (playerObj == null) return;

            var gridInventory = playerObj.GetComponent<GridInventory>();
            var fallbackInventory = playerObj.GetComponent<IInventory>();
            if (gridInventory == null && fallbackInventory == null) return;

            var slot = Slots[slotIndex];
            var itemSO = LookupItem(slot.itemId.ToString());
            if (itemSO == null) return;

            bool added = gridInventory != null
                ? gridInventory.TryAddItemSlot(itemSO, slot)
                : fallbackInventory.TryAddItem(itemSO, slot.stackCount);

            if (added)
            {
                Slots.RemoveAt(slotIndex);
                EventBus.Publish(new CorpseLootedEvent
                {
                    looterClientId = looterId,
                    corpseOwnerClientId = OwnerClientId,
                    itemId = slot.itemId,
                });
            }
        }

        private ItemDataSO LookupItem(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                IItemDatabase serviceDatabase = ServiceLocator.Get<IItemDatabase>();
                ItemDataSO item = serviceDatabase?.GetById(id);
                if (item != null)
                    return item;
            }

            if (itemDatabase == null) return null;
            foreach (var so in itemDatabase)
            {
                if (so != null && so.itemID == id) return so;
            }
            return null;
        }
    }
}

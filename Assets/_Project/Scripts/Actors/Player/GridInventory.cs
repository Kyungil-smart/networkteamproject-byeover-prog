using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    
    public class GridInventory : NetworkBehaviour, IInventory
    {
        // ----------- 상수 (마스터 결정) -----------

        public const byte BASE_WIDTH = 4;
        public const byte BASE_HEIGHT = 5;

        // ----------- Network State -----------

        /// <summary>그리드에 놓인 아이템 슬롯 리스트</summary>
        public NetworkList<ItemSlotData> ServerGrid;

        // ----------- 캐시 -----------

        private EquipmentSlots equipment;
        private IItemDatabase itemDb;

        public byte Width => BASE_WIDTH;
        public byte Height => BASE_HEIGHT;

        // ----------- Lifecycle -----------

        private void Awake()
        {
            ServerGrid = new NetworkList<ItemSlotData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);

            equipment = GetComponent<EquipmentSlots>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            itemDb = ServiceLocator.Get<IItemDatabase>();

            if (itemDb == null)
            {
                Debug.LogError("[GridInventory] IItemDatabase 서비스가 등록되어 있지 않음. " +
                               "씬 ItemDatabase GameObject 확인.");
            }
        }

        // ----------- IInventory: Add / Has / Consume -----------

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (!IsServer || item == null) return false;

            for (byte y = 0; y < BASE_HEIGHT; y++)
            {
                for (byte x = 0; x < BASE_WIDTH; x++)
                {
                    if (CanPlaceAt(x, y, item.gridSize, false))
                    {
                        ServerGrid.Add(new ItemSlotData
                        {
                            itemId = item.itemID,
                            gridX = x,
                            gridY = y,
                            rotated = false,
                            stackCount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize),
                            currentDurability = (item is WeaponDataSO w) ? w.maxDurability : 0,
                            currentAmmo = 0,
                        });
                        EventBus.Publish(new ItemAddedEvent
                        {
                            clientId = OwnerClientId,
                            itemId = item.itemID
                        });
                        return true;
                    }
                }
            }
            return false;
        }

        public bool HasItem(string itemId, int count)
        {
            int total = 0;
            for (int i = 0; i < ServerGrid.Count; i++)
            {
                if (ServerGrid[i].itemId.ToString() == itemId)
                    total += ServerGrid[i].stackCount;
                if (total >= count) return true;
            }
            return false;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (!IsServer) return false;
            if (!HasItem(itemId, count)) return false;

            int remaining = count;
            for (int i = ServerGrid.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var slot = ServerGrid[i];
                if (slot.itemId.ToString() != itemId) continue;
                if (slot.stackCount <= remaining)
                {
                    remaining -= slot.stackCount;
                    ServerGrid.RemoveAt(i);
                    EventBus.Publish(new ItemRemovedEvent
                    {
                        clientId = OwnerClientId,
                        itemId = slot.itemId
                    });
                }
                else
                {
                    slot.stackCount -= (ushort)remaining;
                    ServerGrid[i] = slot;
                    remaining = 0;
                }
            }
            return true;
        }

        // ----------- Collision Check -----------

        /// <summary>
        /// (x, y) 위치에 size 크기의 아이템을 놓을 수 있는지 검사.
        /// IItemDatabase로 기존 슬롯의 진짜 SO를 조회해서 정확한 사이즈로 충돌 검사.
        /// </summary>
        private bool CanPlaceAt(byte x, byte y, Vector2Int size, bool rotated)
        {
            int w = rotated ? size.y : size.x;
            int h = rotated ? size.x : size.y;

            if (x + w > BASE_WIDTH || y + h > BASE_HEIGHT) return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                var s = ServerGrid[i];

                Vector2Int existingSize = Vector2Int.one;
                if (itemDb != null)
                {
                    var so = itemDb.GetById(s.itemId.ToString());
                    if (so != null) existingSize = so.gridSize;
                }

                int sw = s.rotated ? existingSize.y : existingSize.x;
                int sh = s.rotated ? existingSize.x : existingSize.y;

                bool overlap = !(x + w <= s.gridX || s.gridX + sw <= x ||
                                 y + h <= s.gridY || s.gridY + sh <= y);
                if (overlap) return false;
            }
            return true;
        }
    }
}
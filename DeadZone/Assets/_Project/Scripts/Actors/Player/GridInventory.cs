using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 8x6 그리드 인벤토리. 서버 권위.
    /// readPerm = Owner라서 다른 플레이어는 본인 인벤토리를 볼 수 없다.
    /// </summary>
    public class GridInventory : NetworkBehaviour, IInventory
    {
        [SerializeField] private byte gridWidth = 8;
        [SerializeField] private byte gridHeight = 6;

        public NetworkList<ItemSlotData> ServerGrid;

        private EquipmentSlots equipment;

        public byte Width => gridWidth;
        public byte Height => gridHeight;

        private void Awake()
        {
            ServerGrid = new NetworkList<ItemSlotData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);

            equipment = GetComponent<EquipmentSlots>();
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (!IsServer || item == null) return false;

            for (byte y = 0; y < gridHeight; y++)
            {
                for (byte x = 0; x < gridWidth; x++)
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
                        EventBus.Publish(new ItemAddedEvent { clientId = OwnerClientId, itemId = item.itemID });
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
                    EventBus.Publish(new ItemRemovedEvent { clientId = OwnerClientId, itemId = slot.itemId });
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

        private bool CanPlaceAt(byte x, byte y, Vector2Int size, bool rotated)
        {
            int w = rotated ? size.y : size.x;
            int h = rotated ? size.x : size.y;
            if (x + w > gridWidth || y + h > gridHeight) return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                var s = ServerGrid[i];
                var soSize = new Vector2Int(1, 1);
                int sw = s.rotated ? soSize.y : soSize.x;
                int sh = s.rotated ? soSize.x : soSize.y;

                bool overlap = !(x + w <= s.gridX || s.gridX + sw <= x ||
                                 y + h <= s.gridY || s.gridY + sh <= y);
                if (overlap) return false;
            }
            return true;
        }
    }
}

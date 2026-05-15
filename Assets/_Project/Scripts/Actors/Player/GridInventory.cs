using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors
{
    public class GridInventory : NetworkBehaviour, IInventory
    {
        public const byte BASE_WIDTH = 4;
        public const byte BASE_HEIGHT = 5;
        public const byte QUICK_SLOT_COUNT = 6;

        public NetworkList<ItemSlotData> ServerGrid;
        public NetworkList<QuickSlotData> QuickSlots;

        private EquipmentSlots equipment;
        private IItemDatabase itemDb;
        private int activeSlotCount = BASE_WIDTH * BASE_HEIGHT;
        private bool isUsingMedicalItem;

        [Header("월드 드롭")]
        [SerializeField] private GameObject droppedLootItemPrefab;
        [SerializeField] private float dropForwardDistance = 1.25f;
        [SerializeField] private float dropGroundRayHeight = 1.5f;
        [SerializeField] private float dropGroundRayDistance = 4f;
        [SerializeField] private float dropGroundOffset = 0.25f;

        public byte Width => BASE_WIDTH;
        public byte Height => (byte)Mathf.CeilToInt(activeSlotCount / (float)BASE_WIDTH);

        private void Awake()
        {
            ServerGrid = new NetworkList<ItemSlotData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);

            QuickSlots = new NetworkList<QuickSlotData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);

            equipment = GetComponent<EquipmentSlots>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            itemDb = ServiceLocator.Get<IItemDatabase>();
            EventBus.Subscribe<ReloadExecuteRequestedEvent>(OnReloadExecuteRequested);
            EventBus.Subscribe<BackpackChangedEvent>(OnBackpackChanged);

            if (itemDb == null)
            {
                Debug.LogError("[GridInventory] IItemDatabase 서비스가 등록되어 있지 않음. 씬 ItemDatabase GameObject 확인.");
            }
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<ReloadExecuteRequestedEvent>(OnReloadExecuteRequested);
            EventBus.Unsubscribe<BackpackChangedEvent>(OnBackpackChanged);
            base.OnNetworkDespawn();
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (!IsServer || item == null || amount <= 0) return false;
            if (!CanAddItem(item, amount)) return false;

            int mergedAmount = MergeIntoExistingStacks(item, amount, out int remainingAmount);
            bool addedAllRemaining = TryAddRemainingAsNewStacks(item, remainingAmount);

            if (mergedAmount > 0)
            {
                EventBus.Publish(new ItemAddedEvent
                {
                    clientId = OwnerClientId,
                    itemId = item.itemID
                });
            }

            return addedAllRemaining;
        }

        public List<InventoryItemSaveData> ExportSnapshot()
        {
            List<InventoryItemSaveData> snapshot = new();

            if (ServerGrid == null)
                return snapshot;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                ItemSlotData slot = ServerGrid[i];
                string itemId = slot.itemId.ToString();

                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                snapshot.Add(new InventoryItemSaveData
                {
                    itemId = itemId,
                    instanceId = $"{itemId}_{slot.gridX}_{slot.gridY}_{i}",
                    gridX = slot.gridX,
                    gridY = slot.gridY,
                    rotated = slot.rotated,
                    stackCount = slot.stackCount,
                    currentDurability = slot.currentDurability,
                    currentAmmo = slot.currentAmmo
                });
            }

            return snapshot;
        }

        public List<QuickSlotSaveData> ExportQuickSlotSnapshot()
        {
            List<QuickSlotSaveData> snapshot = new();

            if (QuickSlots == null)
                return snapshot;

            for (int i = 0; i < QuickSlots.Count; i++)
            {
                QuickSlotData slot = QuickSlots[i];
                string itemId = slot.itemId.ToString();

                int availableCount = GetItemCount(itemId);
                if (string.IsNullOrWhiteSpace(itemId) || slot.stackCount <= 0 || availableCount <= 0)
                    continue;

                snapshot.Add(new QuickSlotSaveData
                {
                    itemId = itemId,
                    instanceId = $"{itemId}_quickslot_{slot.slotIndex}",
                    slotIndex = slot.slotIndex,
                    stackCount = Mathf.Min(slot.stackCount, availableCount),
                    currentDurability = slot.currentDurability,
                    currentAmmo = slot.currentAmmo
                });
            }

            return snapshot;
        }

        public int ImportSnapshot(IReadOnlyList<InventoryItemSaveData> snapshot)
        {
            if (!IsServer)
                return 0;

            ServerGrid.Clear();

            if (snapshot == null || snapshot.Count == 0)
                return 0;

            EnsureItemDatabase();

            List<ItemSlotData> importedSlots = new(snapshot.Count);

            for (int i = 0; i < snapshot.Count; i++)
            {
                InventoryItemSaveData savedItem = snapshot[i];

                if (!TryCreateSlotFromSnapshot(savedItem, importedSlots, out ItemSlotData slot))
                    continue;

                ServerGrid.Add(slot);
                importedSlots.Add(slot);
            }

            return importedSlots.Count;
        }

        public int ImportQuickSlotSnapshot(IReadOnlyList<QuickSlotSaveData> snapshot)
        {
            if (!IsServer)
                return 0;

            QuickSlots.Clear();

            if (snapshot == null || snapshot.Count == 0)
                return 0;

            EnsureItemDatabase();

            int importedCount = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                QuickSlotSaveData savedItem = snapshot[i];
                if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.itemId))
                    continue;

                ItemDataSO item = itemDb?.GetById(savedItem.itemId);
                if (!IsQuickSlotItem(item))
                    continue;

                int availableCount = GetItemCount(savedItem.itemId);
                if (availableCount <= 0)
                    continue;

                SetQuickSlot(new QuickSlotData
                {
                    slotIndex = (byte)Mathf.Clamp(savedItem.slotIndex, 0, QUICK_SLOT_COUNT - 1),
                    itemId = savedItem.itemId,
                    stackCount = (ushort)Mathf.Clamp(savedItem.stackCount, 1, availableCount),
                    currentDurability = Mathf.Max(0f, savedItem.currentDurability),
                    currentAmmo = (ushort)Mathf.Clamp(savedItem.currentAmmo, 0, ushort.MaxValue)
                });

                importedCount++;
            }

            return importedCount;
        }

        /// <summary>
        /// 내구도와 장탄 수가 보존된 슬롯 데이터를 새 인벤토리 칸에 추가합니다.
        /// 기존 TryAddItem 흐름은 유지하고, 시체 루팅처럼 개별 아이템 상태가 필요한 경우에만 사용합니다.
        /// </summary>
        /// <param name="item">추가할 아이템 데이터입니다.</param>
        /// <param name="sourceSlot">보존할 슬롯 상태입니다.</param>
        /// <returns>추가에 성공하면 true입니다.</returns>
        public bool TryAddItemSlot(ItemDataSO item, ItemSlotData sourceSlot)
        {
            if (!IsServer || item == null || sourceSlot.stackCount <= 0)
                return false;

            if (CanMergeAsPlainStack(item, sourceSlot))
                return TryAddItem(item, sourceSlot.stackCount);

            List<ItemSlotData> simulatedSlots = new(ServerGrid.Count);
            for (int i = 0; i < ServerGrid.Count; i++)
                simulatedSlots.Add(ServerGrid[i]);

            int amount = Mathf.Clamp(sourceSlot.stackCount, 1, Mathf.Max(1, item.maxStackSize));
            if (!TryFindPlacement(simulatedSlots, item, amount, out ItemSlotData newSlot))
                return false;

            newSlot.currentDurability = Mathf.Max(0f, sourceSlot.currentDurability);
            newSlot.currentAmmo = sourceSlot.currentAmmo;

            ServerGrid.Add(newSlot);

            EventBus.Publish(new ItemAddedEvent
            {
                clientId = OwnerClientId,
                itemId = item.itemID
            });

            return true;
        }

        public bool CanAddItem(ItemDataSO item, int amount = 1)
        {
            if (!IsServer || item == null || amount <= 0) return false;

            List<ItemSlotData> simulatedSlots = new(ServerGrid.Count);
            for (int i = 0; i < ServerGrid.Count; i++)
                simulatedSlots.Add(ServerGrid[i]);

            int remainingAmount = amount;
            int maxStackSize = Mathf.Max(1, item.maxStackSize);
            FixedString64Bytes itemId = item.itemID;

            for (int i = 0; i < simulatedSlots.Count && remainingAmount > 0; i++)
            {
                ItemSlotData slot = simulatedSlots[i];
                if (!slot.itemId.Equals(itemId)) continue;
                if (slot.stackCount >= maxStackSize) continue;

                int mergeAmount = Mathf.Min(maxStackSize - slot.stackCount, remainingAmount);
                slot.stackCount += (ushort)mergeAmount;
                simulatedSlots[i] = slot;
                remainingAmount -= mergeAmount;
            }

            while (remainingAmount > 0)
            {
                int stackAmount = Mathf.Min(maxStackSize, remainingAmount);
                if (!TryFindPlacement(simulatedSlots, item, stackAmount, out ItemSlotData newSlot))
                    return false;

                simulatedSlots.Add(newSlot);
                remainingAmount -= stackAmount;
            }

            return true;
        }

        /// <summary>
        /// 내구도와 장탄 수가 보존된 슬롯 데이터를 추가할 수 있는지 검사합니다.
        /// </summary>
        /// <param name="item">검사할 아이템 데이터입니다.</param>
        /// <param name="sourceSlot">보존할 슬롯 상태입니다.</param>
        /// <returns>추가 가능하면 true입니다.</returns>
        public bool CanAddItemSlot(ItemDataSO item, ItemSlotData sourceSlot)
        {
            if (!IsServer || item == null || sourceSlot.stackCount <= 0)
                return false;

            if (CanMergeAsPlainStack(item, sourceSlot))
                return CanAddItem(item, sourceSlot.stackCount);

            List<ItemSlotData> simulatedSlots = new(ServerGrid.Count);
            for (int i = 0; i < ServerGrid.Count; i++)
                simulatedSlots.Add(ServerGrid[i]);

            int amount = Mathf.Clamp(sourceSlot.stackCount, 1, Mathf.Max(1, item.maxStackSize));
            return TryFindPlacement(simulatedSlots, item, amount, out _);
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            return GetItemCount(itemId) >= count;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            int total = 0;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                if (ServerGrid[i].itemId.ToString() == itemId)
                    total += ServerGrid[i].stackCount;
            }

            return total;
        }

        public bool HasQuickSlotItem(string itemId, int count)
        {
            if (QuickSlots == null || string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            for (int i = 0; i < QuickSlots.Count; i++)
            {
                QuickSlotData slot = QuickSlots[i];
                if (slot.itemId.ToString() == itemId)
                    return HasItem(itemId, count);
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

                if (slot.itemId.ToString() != itemId)
                    continue;

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

        [ServerRpc]
        public void DropInventorySlotServerRpc(byte gridX, byte gridY)
        {
            if (!IsServer)
                return;

            if (!TryGetSlotAt(gridX, gridY, out ItemSlotData slotToDrop))
                return;

            ItemDataSO item = ResolveItem(slotToDrop.itemId.ToString());
            if (item == null)
                return;

            GameObject prefab = ResolveDroppedLootItemPrefab(item);
            if (prefab == null)
            {
                Debug.LogWarning($"[GridInventory] Dropped loot prefab is missing. itemId={item.itemID}", this);
                return;
            }

            GameObject instance = CreateDroppedLootItem(item, Mathf.Max(1, slotToDrop.stackCount), prefab);
            if (instance == null)
                return;

            if (!TrySpawnDroppedLootItem(instance))
                return;

            if (!TryRemoveSlotAt(gridX, gridY, out _))
            {
                DestroyDroppedLootInstance(instance);
                return;
            }
        }

        [ServerRpc]
        public void DropEquipmentSlotServerRpc(EquipmentTargetSlot targetSlot)
        {
            if (!IsServer)
                return;

            equipment ??= GetComponent<EquipmentSlots>();
            if (equipment == null)
                return;

            if (!TryGetEquipmentItemId(targetSlot, out string itemId))
                return;

            ItemDataSO item = ResolveItem(itemId);
            if (item == null)
                return;

            GameObject prefab = ResolveDroppedLootItemPrefab(item);
            if (prefab == null)
            {
                Debug.LogWarning($"[GridInventory] Dropped loot prefab is missing. itemId={item.itemID}", this);
                return;
            }

            GameObject instance = CreateDroppedLootItem(item, 1, prefab);
            if (instance == null)
                return;

            if (!TrySpawnDroppedLootItem(instance))
                return;

            if (!equipment.TryRemoveEquipmentSlotForDrop(targetSlot, out _))
            {
                DestroyDroppedLootInstance(instance);
                return;
            }
        }

        public void RequestMoveEquipmentSlotToInventory(EquipmentTargetSlot targetSlot)
        {
            RequestMoveEquipmentSlotToInventory(targetSlot, byte.MaxValue, byte.MaxValue);
        }

        public void RequestMoveEquipmentSlotToInventory(EquipmentTargetSlot targetSlot, byte preferredGridX, byte preferredGridY)
        {
            if (targetSlot == EquipmentTargetSlot.None)
                return;

            if (IsSpawned)
            {
                MoveEquipmentSlotToInventoryRpc(targetSlot, preferredGridX, preferredGridY);
                return;
            }

            if (IsServer)
                TryMoveEquipmentSlotToInventory(targetSlot, preferredGridX, preferredGridY);
        }

        public bool TryMoveEquipmentSlotToInventoryOnServer(EquipmentTargetSlot targetSlot)
        {
            return TryMoveEquipmentSlotToInventoryOnServer(targetSlot, byte.MaxValue, byte.MaxValue);
        }

        public bool TryMoveEquipmentSlotToInventoryOnServer(EquipmentTargetSlot targetSlot, byte preferredGridX, byte preferredGridY)
        {
            if (!IsServer || targetSlot == EquipmentTargetSlot.None)
                return false;

            return TryMoveEquipmentSlotToInventory(targetSlot, preferredGridX, preferredGridY);
        }

        public void RequestMoveInventorySlotToEquipment(byte gridX, byte gridY, EquipmentTargetSlot targetSlot)
        {
            if (targetSlot == EquipmentTargetSlot.None)
                return;

            if (IsSpawned)
            {
                MoveInventorySlotToEquipmentRpc(gridX, gridY, targetSlot);
                return;
            }

            if (IsServer)
                TryMoveInventorySlotToEquipment(gridX, gridY, targetSlot);
        }

        public void RequestAssignQuickSlot(byte gridX, byte gridY, byte quickSlotIndex)
        {
            if (IsSpawned)
            {
                AssignQuickSlotRpc(gridX, gridY, quickSlotIndex);
                return;
            }

            if (IsServer)
                TryAssignQuickSlot(gridX, gridY, quickSlotIndex);
        }

        public void RequestUseQuickSlot(byte quickSlotIndex)
        {
            if (IsSpawned)
            {
                UseQuickSlotRpc(quickSlotIndex);
                return;
            }

            if (IsServer)
                TryUseQuickSlot(quickSlotIndex);
        }

        public void RequestMoveQuickSlotToInventory(byte quickSlotIndex, byte preferredGridX, byte preferredGridY)
        {
            if (IsSpawned)
            {
                MoveQuickSlotToInventoryRpc(quickSlotIndex, preferredGridX, preferredGridY);
                return;
            }

            if (IsServer)
                TryMoveQuickSlotToInventory(quickSlotIndex, preferredGridX, preferredGridY);
        }

        public bool TryAssignQuickSlotShortcutOnServer(ItemDataSO item, ItemSlotData sourceSlot, byte quickSlotIndex)
        {
            if (!IsServer || item == null || quickSlotIndex >= QUICK_SLOT_COUNT || !IsQuickSlotItem(item))
                return false;

            int availableCount = GetItemCount(item.itemID);
            if (availableCount <= 0)
                return false;

            SetQuickSlot(new QuickSlotData
            {
                slotIndex = quickSlotIndex,
                itemId = item.itemID,
                stackCount = (ushort)Mathf.Clamp(Mathf.Min(sourceSlot.stackCount, availableCount), 1, ushort.MaxValue),
                currentDurability = Mathf.Max(0f, sourceSlot.currentDurability),
                currentAmmo = sourceSlot.currentAmmo
            });

            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void MoveEquipmentSlotToInventoryRpc(EquipmentTargetSlot targetSlot, byte preferredGridX, byte preferredGridY, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryMoveEquipmentSlotToInventory(targetSlot, preferredGridX, preferredGridY);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void MoveInventorySlotToEquipmentRpc(byte gridX, byte gridY, EquipmentTargetSlot targetSlot, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryMoveInventorySlotToEquipment(gridX, gridY, targetSlot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AssignQuickSlotRpc(byte gridX, byte gridY, byte quickSlotIndex, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryAssignQuickSlot(gridX, gridY, quickSlotIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void UseQuickSlotRpc(byte quickSlotIndex, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryUseQuickSlot(quickSlotIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void MoveQuickSlotToInventoryRpc(byte quickSlotIndex, byte preferredGridX, byte preferredGridY, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryMoveQuickSlotToInventory(quickSlotIndex, preferredGridX, preferredGridY);
        }

        private bool TryMoveInventorySlotToEquipment(byte gridX, byte gridY, EquipmentTargetSlot targetSlot)
        {
            if (!IsServer || targetSlot == EquipmentTargetSlot.None)
                return false;

            equipment ??= GetComponent<EquipmentSlots>();
            if (equipment == null)
                return false;

            if (!TryGetSlotAt(gridX, gridY, out ItemSlotData sourceSlot))
                return false;

            ItemDataSO sourceItem = ResolveItem(sourceSlot.itemId.ToString());
            if (!equipment.CanEquipItemToSlot(sourceItem, targetSlot))
                return false;

            bool hasEquippedItem = TryBuildEquipmentInventorySlot(targetSlot, ResolveItem(GetEquipmentItemIdOrEmpty(targetSlot)), out ItemSlotData equippedSlot);
            ItemDataSO equippedItem = hasEquippedItem ? ResolveItem(equippedSlot.itemId.ToString()) : null;

            if (!TryRemoveSlotAt(gridX, gridY, out _))
                return false;

            if (hasEquippedItem && !equipment.TryRemoveEquipmentSlotForDrop(targetSlot, out _))
            {
                TryAddItemSlotAt(sourceItem, sourceSlot, gridX, gridY);
                return false;
            }

            if (!equipment.TryEquipItemToSlot(sourceItem, sourceSlot, targetSlot))
            {
                if (hasEquippedItem)
                    equipment.TryEquipItemToSlot(equippedItem, equippedSlot, targetSlot);

                TryAddItemSlotAt(sourceItem, sourceSlot, gridX, gridY);
                return false;
            }

            if (!hasEquippedItem)
                return true;

            if (TryAddItemSlotAt(equippedItem, equippedSlot, gridX, gridY))
                return true;

            Debug.LogError($"[GridInventory] Failed to place swapped equipment item into inventory. itemId={equippedItem.itemID}, slot={targetSlot}", this);
            return false;
        }

        private bool TryAssignQuickSlot(byte gridX, byte gridY, byte quickSlotIndex)
        {
            if (!IsServer || quickSlotIndex >= QUICK_SLOT_COUNT)
                return false;

            if (!TryGetSlotAt(gridX, gridY, out ItemSlotData sourceSlot))
                return false;

            ItemDataSO sourceItem = ResolveItem(sourceSlot.itemId.ToString());
            if (!IsQuickSlotItem(sourceItem))
                return false;

            SetQuickSlot(new QuickSlotData
            {
                slotIndex = quickSlotIndex,
                itemId = sourceSlot.itemId,
                stackCount = sourceSlot.stackCount,
                currentDurability = sourceSlot.currentDurability,
                currentAmmo = sourceSlot.currentAmmo
            });

            return true;
        }

        private bool TryMoveQuickSlotToInventory(byte quickSlotIndex, byte preferredGridX, byte preferredGridY)
        {
            if (!IsServer || !TryGetQuickSlot(quickSlotIndex, out QuickSlotData quickSlot))
                return false;

            ClearQuickSlot(quickSlotIndex);
            return true;
        }

        private bool TryUseQuickSlot(byte quickSlotIndex)
        {
            if (!IsServer || !TryGetQuickSlot(quickSlotIndex, out QuickSlotData quickSlot))
                return false;

            string itemId = quickSlot.itemId.ToString();
            if (!HasItem(itemId, 1))
            {
                ClearQuickSlot(quickSlotIndex);
                return false;
            }

            return TryUseMedicalItem(itemId);
        }

        private bool TryMoveEquipmentSlotToInventory(EquipmentTargetSlot targetSlot, byte preferredGridX, byte preferredGridY)
        {
            if (!IsServer)
                return false;

            equipment ??= GetComponent<EquipmentSlots>();
            if (equipment == null)
                return false;

            if (!TryGetEquipmentItemId(targetSlot, out string itemId))
                return false;

            ItemDataSO item = ResolveItem(itemId);
            if (item == null)
                return false;

            if (!TryBuildEquipmentInventorySlot(targetSlot, item, out ItemSlotData slot))
                return false;

            bool hasPreferredPosition = preferredGridX != byte.MaxValue && preferredGridY != byte.MaxValue;
            bool canAdd = hasPreferredPosition
                ? CanAddItemSlotAt(item, slot, preferredGridX, preferredGridY)
                : CanAddItemSlot(item, slot);

            if (!canAdd)
            {
                Debug.LogWarning($"[GridInventory] Cannot move equipment to inventory because no valid grid space exists. slot={targetSlot}, itemId={item.itemID}", this);
                return false;
            }

            if (!equipment.TryRemoveEquipmentSlotForDrop(targetSlot, out _))
                return false;

            bool added = hasPreferredPosition
                ? TryAddItemSlotAt(item, slot, preferredGridX, preferredGridY)
                : TryAddItemSlot(item, slot);

            if (added)
                return true;

            Debug.LogError($"[GridInventory] Failed to add equipment item after clearing equipment slot. itemId={item.itemID}, slot={targetSlot}", this);
            return false;
        }

        private bool TryBuildEquipmentInventorySlot(EquipmentTargetSlot targetSlot, ItemDataSO item, out ItemSlotData slot)
        {
            slot = default;

            if (item == null)
                return false;

            slot = new ItemSlotData
            {
                itemId = item.itemID,
                stackCount = 1,
                gridX = 0,
                gridY = 0,
                rotated = false,
                currentDurability = 0f,
                currentAmmo = 0
            };

            switch (targetSlot)
            {
                case EquipmentTargetSlot.Head:
                    slot.currentDurability = equipment.HelmetDurability.Value;
                    break;

                case EquipmentTargetSlot.Armor:
                    slot.currentDurability = equipment.ArmorDurability.Value;
                    break;

                case EquipmentTargetSlot.Primary1:
                    ApplyWeaponSlotState(item, equipment.Primary1State.Value, ref slot);
                    break;

                case EquipmentTargetSlot.Primary2:
                    ApplyWeaponSlotState(item, equipment.Primary2State.Value, ref slot);
                    break;

                case EquipmentTargetSlot.Secondary:
                    ApplyWeaponSlotState(item, equipment.SecondaryState.Value, ref slot);
                    break;

                case EquipmentTargetSlot.Backpack:
                case EquipmentTargetSlot.Melee:
                    break;

                default:
                    return false;
            }

            return true;
        }

        private static void ApplyWeaponSlotState(ItemDataSO item, WeaponState weaponState, ref ItemSlotData slot)
        {
            if (item is WeaponDataSO weapon)
                slot.currentDurability = Mathf.Max(0f, weapon.maxDurability);

            slot.currentAmmo = (ushort)Mathf.Clamp(weaponState.currentAmmo, 0, ushort.MaxValue);
        }

        private bool CanAddItemSlotAt(ItemDataSO item, ItemSlotData sourceSlot, byte gridX, byte gridY)
        {
            if (!IsServer || item == null || sourceSlot.stackCount <= 0)
                return false;

            List<ItemSlotData> simulatedSlots = new(ServerGrid.Count);
            for (int i = 0; i < ServerGrid.Count; i++)
                simulatedSlots.Add(ServerGrid[i]);

            return CanPlaceAt(simulatedSlots, gridX, gridY, item.gridSize, false);
        }

        private bool TryAddItemSlotAt(ItemDataSO item, ItemSlotData sourceSlot, byte gridX, byte gridY)
        {
            if (!CanAddItemSlotAt(item, sourceSlot, gridX, gridY))
                return false;

            sourceSlot.gridX = gridX;
            sourceSlot.gridY = gridY;
            sourceSlot.rotated = false;
            sourceSlot.stackCount = (ushort)Mathf.Clamp(sourceSlot.stackCount, 1, Mathf.Max(1, item.maxStackSize));

            ServerGrid.Add(sourceSlot);

            EventBus.Publish(new ItemAddedEvent
            {
                clientId = OwnerClientId,
                itemId = item.itemID
            });

            return true;
        }

        private bool TryRemoveSlotAt(byte gridX, byte gridY, out ItemSlotData removedSlot)
        {
            removedSlot = default;

            if (!IsServer || ServerGrid == null)
                return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                ItemSlotData slot = ServerGrid[i];
                if (slot.gridX != gridX || slot.gridY != gridY)
                    continue;

                removedSlot = slot;
                ServerGrid.RemoveAt(i);

                EventBus.Publish(new ItemRemovedEvent
                {
                    clientId = OwnerClientId,
                    itemId = slot.itemId
                });

                return true;
            }

            return false;
        }

        private bool TryGetSlotIndexAt(byte gridX, byte gridY, out int index, out ItemSlotData foundSlot)
        {
            index = -1;
            foundSlot = default;

            if (ServerGrid == null)
                return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                ItemSlotData slot = ServerGrid[i];
                if (slot.gridX != gridX || slot.gridY != gridY)
                    continue;

                index = i;
                foundSlot = slot;
                return true;
            }

            return false;
        }

        private bool TryGetSlotAt(byte gridX, byte gridY, out ItemSlotData foundSlot)
        {
            foundSlot = default;

            if (ServerGrid == null)
                return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                ItemSlotData slot = ServerGrid[i];
                if (slot.gridX != gridX || slot.gridY != gridY)
                    continue;

                foundSlot = slot;
                return true;
            }

            return false;
        }

        private GameObject CreateDroppedLootItem(ItemDataSO item, int amount, GameObject prefab)
        {
            if (!IsServer || item == null)
                return null;

            if (!IsValidDroppedLootItemPrefab(prefab))
            {
                Debug.LogError(
                    $"[GridInventory] 드롭 아이템 프리팹이 잘못되었습니다. " +
                    $"NetworkObject + LootInteractable이 있어야 하고 LootContainer는 없어야 합니다. prefab={(prefab != null ? prefab.name : "null")}",
                    this);
                return null;
            }

            Vector3 spawnPosition = ResolveDropPosition();
            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);

            if (instance.GetComponent<LootContainer>() != null)
            {
                Debug.LogError(
                    $"[GridInventory] 드롭 아이템으로 LootContainer 프리팹이 생성되었습니다. prefab={prefab.name}. 즉시 제거합니다.",
                    instance);

                Destroy(instance);
                return null;
            }

            if (!instance.TryGetComponent(out LootInteractable lootInteractable))
            {
                Debug.LogError($"[GridInventory] 드롭 아이템 프리팹에 LootInteractable이 없습니다. prefab={prefab.name}", instance);
                Destroy(instance);
                return null;
            }

            lootInteractable.Initialize(item, amount);
            return instance;
        }

        private bool TrySpawnDroppedLootItem(GameObject instance)
        {
            if (!IsServer || instance == null)
                return false;

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[GridInventory] Dropped loot prefab missing NetworkObject.", instance);
                Destroy(instance);
                return false;
            }

            try
            {
                networkObject.Spawn(destroyWithScene: true);
                if (instance.TryGetComponent(out LootInteractable lootInteractable))
                    lootInteractable.ForceRefreshWorldVisual();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GridInventory] Failed to spawn dropped loot. item will remain in inventory. reason={ex.Message}", instance);
                Destroy(instance);
                return false;
            }
        }

        private void DestroyDroppedLootInstance(GameObject instance)
        {
            if (instance == null)
                return;

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
                return;
            }

            Destroy(instance);
        }

        private Vector3 ResolveDropPosition()
        {
            Transform playerTransform = transform;
            Vector3 forward = playerTransform.forward;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;

            Vector3 fallback = playerTransform.position + forward.normalized * dropForwardDistance;
            Vector3 rayOrigin = fallback + Vector3.up * dropGroundRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, dropGroundRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * dropGroundOffset;

            return fallback + Vector3.up * dropGroundOffset;
        }

        private GameObject ResolveDroppedLootItemPrefab(ItemDataSO item)
        {
            if (droppedLootItemPrefab == null)
            {
                Debug.LogError(
                    $"[GridInventory] droppedLootItemPrefab is not assigned. Drop cancelled. itemId={(item != null ? item.itemID : "null")}",
                    this);
                return null;
            }

            if (!IsValidDroppedLootItemPrefab(droppedLootItemPrefab))
            {
                Debug.LogError(
                    $"[GridInventory] droppedLootItemPrefab is invalid. It must have NetworkObject + LootInteractable and must not have LootContainer. prefab={droppedLootItemPrefab.name}",
                    this);
                return null;
            }

            if (!IsNetworkPrefabRegistered(droppedLootItemPrefab))
            {
                Debug.LogError(
                    $"[GridInventory] droppedLootItemPrefab is not registered in NetworkPrefabsList. Drop cancelled. prefab={droppedLootItemPrefab.name}",
                    this);
                return null;
            }

            return droppedLootItemPrefab;
        }

        private static bool IsNetworkPrefabRegistered(GameObject prefab)
        {
            if (prefab == null)
                return false;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.NetworkConfig == null)
                return false;

            IReadOnlyList<NetworkPrefab> prefabs = networkManager.NetworkConfig.Prefabs?.Prefabs;
            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (prefabs[i]?.Prefab == prefab)
                        return true;
                }
            }

            List<NetworkPrefabsList> prefabLists = networkManager.NetworkConfig.Prefabs?.NetworkPrefabsLists;
            if (prefabLists == null)
                return false;

            for (int listIndex = 0; listIndex < prefabLists.Count; listIndex++)
            {
                NetworkPrefabsList prefabList = prefabLists[listIndex];
                if (prefabList == null)
                    continue;

                IReadOnlyList<NetworkPrefab> listPrefabs = prefabList.PrefabList;
                if (listPrefabs == null)
                    continue;

                for (int prefabIndex = 0; prefabIndex < listPrefabs.Count; prefabIndex++)
                {
                    if (listPrefabs[prefabIndex]?.Prefab == prefab)
                        return true;
                }
            }

            return false;
        }

        private static GameObject FindLootInteractableNetworkPrefab()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.NetworkConfig == null)
                return null;

            IReadOnlyList<NetworkPrefab> prefabs = networkManager.NetworkConfig.Prefabs?.Prefabs;
            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    GameObject prefab = prefabs[i]?.Prefab;
                    if (IsLootInteractablePrefab(prefab))
                        return prefab;
                }
            }

            List<NetworkPrefabsList> prefabLists = networkManager.NetworkConfig.Prefabs?.NetworkPrefabsLists;
            if (prefabLists == null)
                return null;

            for (int listIndex = 0; listIndex < prefabLists.Count; listIndex++)
            {
                NetworkPrefabsList prefabList = prefabLists[listIndex];
                if (prefabList == null || prefabList.PrefabList == null)
                    continue;

                for (int prefabIndex = 0; prefabIndex < prefabList.PrefabList.Count; prefabIndex++)
                {
                    GameObject prefab = prefabList.PrefabList[prefabIndex]?.Prefab;
                    if (IsLootInteractablePrefab(prefab))
                        return prefab;
                }
            }

            return null;
        }

        private static bool IsLootInteractablePrefab(GameObject prefab)
        {
            return IsValidDroppedLootItemPrefab(prefab);
        }

        private static bool IsValidDroppedLootItemPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;

            return prefab.GetComponent<NetworkObject>() != null &&
                   prefab.GetComponent<LootInteractable>() != null &&
                   prefab.GetComponent<LootContainer>() == null;
        }

        private bool TryGetEquipmentItemId(EquipmentTargetSlot targetSlot, out string itemId)
        {
            itemId = string.Empty;

            if (equipment == null)
                return false;

            itemId = targetSlot switch
            {
                EquipmentTargetSlot.Head => equipment.HeadSlotId.Value.ToString(),
                EquipmentTargetSlot.Backpack => equipment.BackpackSlotId.Value.ToString(),
                EquipmentTargetSlot.Armor => equipment.TorsoSlotId.Value.ToString(),
                EquipmentTargetSlot.Primary1 => equipment.Primary1Id.Value.ToString(),
                EquipmentTargetSlot.Primary2 => equipment.Primary2Id.Value.ToString(),
                EquipmentTargetSlot.Secondary => equipment.SecondaryId.Value.ToString(),
                EquipmentTargetSlot.Melee => equipment.MeleeId.Value.ToString(),
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(itemId);
        }

        private string GetEquipmentItemIdOrEmpty(EquipmentTargetSlot targetSlot)
        {
            return TryGetEquipmentItemId(targetSlot, out string itemId) ? itemId : string.Empty;
        }

        private bool TryGetQuickSlot(byte quickSlotIndex, out QuickSlotData quickSlot)
        {
            quickSlot = default;

            if (QuickSlots == null)
                return false;

            for (int i = 0; i < QuickSlots.Count; i++)
            {
                if (QuickSlots[i].slotIndex != quickSlotIndex)
                    continue;

                quickSlot = QuickSlots[i];
                return !quickSlot.IsEmpty;
            }

            return false;
        }

        private void SetQuickSlot(QuickSlotData quickSlot)
        {
            if (!IsServer || QuickSlots == null || quickSlot.slotIndex >= QUICK_SLOT_COUNT)
                return;

            for (int i = 0; i < QuickSlots.Count; i++)
            {
                if (QuickSlots[i].slotIndex != quickSlot.slotIndex)
                    continue;

                QuickSlots[i] = quickSlot;
                return;
            }

            QuickSlots.Add(quickSlot);
        }

        private void ClearQuickSlot(byte quickSlotIndex)
        {
            if (!IsServer || QuickSlots == null)
                return;

            for (int i = QuickSlots.Count - 1; i >= 0; i--)
            {
                if (QuickSlots[i].slotIndex == quickSlotIndex)
                    QuickSlots.RemoveAt(i);
            }
        }

        private bool TryConsumeQuickSlotItem(string itemId, int count)
        {
            if (!IsServer || QuickSlots == null || string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            for (int i = 0; i < QuickSlots.Count; i++)
            {
                QuickSlotData slot = QuickSlots[i];
                if (slot.itemId.ToString() != itemId || slot.stackCount < count)
                    continue;

                if (slot.stackCount == count)
                    QuickSlots.RemoveAt(i);
                else
                {
                    slot.stackCount -= (ushort)count;
                    QuickSlots[i] = slot;
                }

                return true;
            }

            return false;
        }

        private static ItemSlotData ToItemSlotData(QuickSlotData quickSlot)
        {
            return new ItemSlotData
            {
                itemId = quickSlot.itemId,
                stackCount = quickSlot.stackCount,
                gridX = 0,
                gridY = 0,
                rotated = false,
                currentDurability = quickSlot.currentDurability,
                currentAmmo = quickSlot.currentAmmo
            };
        }

        private bool CanAddQuickSlotItemAt(ItemDataSO item, QuickSlotData quickSlot, byte gridX, byte gridY)
        {
            return item != null && CanAddItemSlotAt(item, ToItemSlotData(quickSlot), gridX, gridY);
        }

        private bool TryAddQuickSlotItemAt(ItemDataSO item, QuickSlotData quickSlot, byte gridX, byte gridY)
        {
            return item != null && TryAddItemSlotAt(item, ToItemSlotData(quickSlot), gridX, gridY);
        }

        private static bool IsQuickSlotItem(ItemDataSO item)
        {
            if (item == null)
                return false;

            if (item is WeaponDataSO or ArmorDataSO or HelmetDataSO or BackpackDataSO)
                return false;

            return item.category is not ItemCategory.Weapon
                and not ItemCategory.Armor
                and not ItemCategory.Helmet
                and not ItemCategory.Backpack;
        }

        private ItemDataSO ResolveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            EnsureItemDatabase();
            return itemDb?.GetById(itemId);
        }

        public void RequestUseMedicalItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            if (IsSpawned)
            {
                UseMedicalItemRpc(new FixedString64Bytes(itemId));
                return;
            }

            if (IsServer)
                TryUseMedicalItem(itemId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void UseMedicalItemRpc(FixedString64Bytes itemId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            TryUseMedicalItem(itemId.ToString());
        }

        private bool TryUseMedicalItem(string itemId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId))
                return false;

            if (isUsingMedicalItem)
            {
                Debug.LogWarning("[GridInventory] 이미 의료 아이템을 사용 중입니다.", this);
                return false;
            }

            EnsureItemDatabase();

            ItemDataSO itemData = itemDb?.GetById(itemId);
            if (itemData == null || itemData.category != ItemCategory.Med)
                return false;

            if (!TryGetMedicalEffect(itemData, out MedicalUseEffect effect))
                return false;

            if (!CanApplyMedicalEffectNow(effect))
            {
                Debug.LogWarning($"[GridInventory] 현재 상태에서는 의료 아이템 효과를 적용할 수 없습니다. itemId={itemData.itemID}", this);
                return false;
            }

            bool hasInventoryItem = HasItem(itemId, 1) ||
                                    (!string.Equals(itemId, itemData.itemID, System.StringComparison.Ordinal) &&
                                     HasItem(itemData.itemID, 1));
            if (!hasInventoryItem)
            {
                Debug.LogWarning($"[GridInventory] 사용할 의료 아이템이 인벤토리에 없습니다. itemId={itemData.itemID}", this);
                return false;
            }

            bool consumed = ConsumeItem(itemId, 1);
            if (!consumed &&
                !string.Equals(itemId, itemData.itemID, System.StringComparison.Ordinal))
            {
                consumed = ConsumeItem(itemData.itemID, 1);
            }

            if (!consumed)
            {
                Debug.LogWarning($"[GridInventory] 의료 아이템을 소모하지 못했습니다. itemId={itemData.itemID}", this);
                return false;
            }

            isUsingMedicalItem = true;
            StartCoroutine(ApplyMedicalEffectRoutine(effect));
            return true;
        }

        private IEnumerator ApplyMedicalEffectRoutine(MedicalUseEffect effect)
        {
            if (!IsServer)
            {
                FinishMedicalUse();
                yield break;
            }

            // 아이템은 사용 입력 직후 소모하고, 효과도 즉시 시작한다.
            // useSeconds는 추후 사용 모션/캐스팅 UI가 생길 때 별도 진행 시간으로 연결할 수 있다.
            if (!CanApplyMedicalEffectNow(effect))
            {
                FinishMedicalUse();
                yield break;
            }

            PlayerHealthSystem health = GetComponent<PlayerHealthSystem>();

            if (effect.instantHeal > 0f && health != null)
                health.Heal(effect.instantHeal);

            if (effect.healDurationSeconds > 0f && effect.healPerSecond > 0f && health != null)
            {
                float elapsed = 0f;
                while (elapsed < effect.healDurationSeconds)
                {
                    float delta = Mathf.Min(Time.deltaTime, effect.healDurationSeconds - elapsed);
                    health.Heal(effect.healPerSecond * delta);
                    elapsed += delta;
                    yield return null;
                }
            }

            // 임시 효과도 사용 입력 직후 서버에서만 적용한다.
            if (effect.weightCapacityMultiplierBonus > 0f)
            {
                GetComponent<PlayerCarryWeightSystem>()?.ApplyTemporaryCapacityMultiplier(
                    effect.weightCapacityMultiplierBonus,
                    effect.durationSeconds);
            }

            if (effect.staminaCostMultiplierBonus > 0f)
            {
                GetComponent<PlayerStaminaSystem>()?.ApplyTemporaryConsumptionMultiplier(
                    effect.staminaCostMultiplierBonus,
                    effect.durationSeconds);
            }

            FinishMedicalUse();
        }

        private void FinishMedicalUse()
        {
            isUsingMedicalItem = false;
        }

        private bool CanApplyMedicalEffectNow(MedicalUseEffect effect)
        {
            PlayerHealthSystem health = GetComponent<PlayerHealthSystem>();
            bool canHeal =
                (effect.instantHeal > 0f || effect.healDurationSeconds > 0f && effect.healPerSecond > 0f) &&
                health != null &&
                health.IsAlive &&
                health.CurrentHP.Value < health.MaxHP;

            bool canApplyCarryWeightBuff =
                effect.durationSeconds > 0f &&
                effect.weightCapacityMultiplierBonus > 0f &&
                GetComponent<PlayerCarryWeightSystem>() != null;

            bool canApplyStaminaBuff =
                effect.durationSeconds > 0f &&
                effect.staminaCostMultiplierBonus > 0f &&
                GetComponent<PlayerStaminaSystem>() != null;

            return canHeal || canApplyCarryWeightBuff || canApplyStaminaBuff;
        }

        private static bool TryGetMedicalEffect(ItemDataSO itemData, out MedicalUseEffect effect)
        {
            effect = default;

            if (itemData is not MedicalItemDataSO medicalItem)
                return false;

            if (!medicalItem.HasAnyEffect)
                return false;

            effect = new MedicalUseEffect
            {
                useSeconds = medicalItem.useSeconds,
                healDurationSeconds = medicalItem.healDurationSeconds,
                healPerSecond = medicalItem.healPerSecond,
                instantHeal = medicalItem.instantHeal,
                durationSeconds = medicalItem.buffDurationSeconds,
                weightCapacityMultiplierBonus = medicalItem.weightCapacityMultiplierBonus,
                staminaCostMultiplierBonus = medicalItem.staminaCostMultiplierBonus
            };

            return true;
        }

        private struct MedicalUseEffect
        {
            public float useSeconds;
            public float healDurationSeconds;
            public float healPerSecond;
            public float instantHeal;
            public float durationSeconds;
            public float weightCapacityMultiplierBonus;
            public float staminaCostMultiplierBonus;
        }

        private bool CanPlaceAt(byte x, byte y, Vector2Int size, bool rotated)
        {
            int w = rotated ? size.y : size.x;
            int h = rotated ? size.x : size.y;

            if (!CanFitWithinActiveGrid(x, y, w, h))
                return false;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                var s = ServerGrid[i];

                Vector2Int existingSize = Vector2Int.one;

                if (itemDb != null)
                {
                    var so = itemDb.GetById(s.itemId.ToString());

                    if (so != null)
                        existingSize = so.gridSize;
                }

                int sw = s.rotated ? existingSize.y : existingSize.x;
                int sh = s.rotated ? existingSize.x : existingSize.y;

                bool overlap = !(x + w <= s.gridX || s.gridX + sw <= x ||
                                 y + h <= s.gridY || s.gridY + sh <= y);

                if (overlap)
                    return false;
            }

            return true;
        }

        private bool TryFindPlacement(
            List<ItemSlotData> simulatedSlots,
            ItemDataSO item,
            int amount,
            out ItemSlotData newSlot)
        {
            newSlot = default;

            for (byte y = 0; y < Height; y++)
            {
                for (byte x = 0; x < BASE_WIDTH; x++)
                {
                    if (!CanPlaceAt(simulatedSlots, x, y, item.gridSize, false))
                        continue;

                    newSlot = new ItemSlotData
                    {
                        itemId = item.itemID,
                        gridX = x,
                        gridY = y,
                        rotated = false,
                        stackCount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize),
                        currentDurability = (item is WeaponDataSO weapon) ? weapon.maxDurability : 0,
                        currentAmmo = 0,
                    };
                    return true;
                }
            }

            return false;
        }

        private bool CanPlaceAt(
            List<ItemSlotData> slots,
            byte x,
            byte y,
            Vector2Int size,
            bool rotated)
        {
            int w = rotated ? size.y : size.x;
            int h = rotated ? size.x : size.y;

            if (!CanFitWithinActiveGrid(x, y, w, h)) return false;

            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlotData s = slots[i];

                Vector2Int existingSize = Vector2Int.one;
                if (itemDb != null)
                {
                    ItemDataSO so = itemDb.GetById(s.itemId.ToString());
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

        // ----------- 장전 실행 처리 -----------

        private struct ReloadContext
        {
            public WeaponDataSO weapon;
            public WeaponState weaponState;
            public AmmoDataSO loadedAmmo;
            public int currentAmmo;
            public int maxAmmo;
        }

        private struct ReloadExecutionResult
        {
            public FixedString64Bytes weaponId;
            public FixedString64Bytes ammoId;
            public AmmoGrade grade;
            public int currentAmmo;
            public int maxAmmo;
            public ReloadCancelReason failureReason;
        }

        private void OnReloadExecuteRequested(ReloadExecuteRequestedEvent e)
        {
            if (!IsServer || e.clientId != OwnerClientId)
                return;

            ReloadExecutionResult result;

            bool success = e.changeGrade
                ? TryExecuteGradeChangeReload(e.targetGrade, out result)
                : TryExecuteCurrentGradeReload(out result);

            if (success)
            {
                EventBus.Publish(new ReloadCompletedEvent
                {
                    clientId = OwnerClientId,
                    weaponId = result.weaponId,
                    ammoId = result.ammoId,
                    grade = result.grade,
                    currentAmmo = result.currentAmmo,
                    maxAmmo = result.maxAmmo
                });

                return;
            }

            EventBus.Publish(new ReloadCancelledEvent
            {
                clientId = OwnerClientId,
                weaponId = result.weaponId,
                reason = (byte)result.failureReason
            });
        }

        private bool TryExecuteCurrentGradeReload(out ReloadExecutionResult result)
        {
            if (!TryGetCurrentReloadContext(out ReloadContext context, out result))
                return false;

            if (context.currentAmmo >= context.maxAmmo)
            {
                result.failureReason = ReloadCancelReason.FullMagazine;
                return false;
            }

            bool foundReloadAmmo = context.loadedAmmo != null
                ? TryFindAmmoByGrade(
                    context.weapon.ammoType,
                    context.loadedAmmo.grade,
                    out AmmoDataSO reloadAmmo,
                    out FixedString64Bytes reloadAmmoId,
                    out int availableCount)
                : TryFindAnyCompatibleAmmo(
                    context.weapon.ammoType,
                    out reloadAmmo,
                    out reloadAmmoId,
                    out availableCount);

            if (!foundReloadAmmo)
            {
                result.failureReason = ReloadCancelReason.NoAmmo;
                return false;
            }

            int loadAmount = Mathf.Min(context.maxAmmo - context.currentAmmo, availableCount);

            if (!TryConsumeAmmoForReload(reloadAmmoId, loadAmount, out int consumedAmount))
            {
                result.failureReason = ReloadCancelReason.NoAmmo;
                return false;
            }

            WeaponState nextState = context.weaponState;
            nextState.loadedAmmoId = reloadAmmoId;
            nextState.currentAmmo = context.currentAmmo + consumedAmount;

            if (!equipment.TryApplyCurrentWeaponState(nextState, WeaponAmmoChangeReason.Reloaded))
            {
                result.failureReason = ReloadCancelReason.Interrupted;
                return false;
            }

            FillReloadResult(ref result, reloadAmmo, reloadAmmoId, nextState.currentAmmo, context.maxAmmo);
            return true;
        }

        private bool TryExecuteGradeChangeReload(AmmoGrade targetGrade, out ReloadExecutionResult result)
        {
            if (!TryGetCurrentReloadContext(out ReloadContext context, out result))
                return false;

            if (!TryFindAmmoByGrade(
                    context.weapon.ammoType,
                    targetGrade,
                    out AmmoDataSO reloadAmmo,
                    out FixedString64Bytes reloadAmmoId,
                    out int availableCount))
            {
                result.failureReason = ReloadCancelReason.NoAmmo;
                return false;
            }

            if (context.currentAmmo > 0 && context.loadedAmmo == null)
            {
                result.failureReason = ReloadCancelReason.AmmoMismatch;
                return false;
            }

            if (context.currentAmmo > 0 &&
                !TryReturnAmmoFromMagazine(context.weaponState.loadedAmmoId, context.currentAmmo, out _))
            {
                result.failureReason = ReloadCancelReason.Interrupted;
                return false;
            }

            int loadAmount = Mathf.Min(context.maxAmmo, availableCount);

            if (!TryConsumeAmmoForReload(reloadAmmoId, loadAmount, out int consumedAmount))
            {
                result.failureReason = ReloadCancelReason.NoAmmo;
                return false;
            }

            WeaponState nextState = context.weaponState;
            nextState.loadedAmmoId = reloadAmmoId;
            nextState.currentAmmo = consumedAmount;

            if (!equipment.TryApplyCurrentWeaponState(nextState, WeaponAmmoChangeReason.AmmoGradeChanged))
            {
                result.failureReason = ReloadCancelReason.Interrupted;
                return false;
            }

            FillReloadResult(ref result, reloadAmmo, reloadAmmoId, nextState.currentAmmo, context.maxAmmo);
            return true;
        }

        private bool TryGetCurrentReloadContext(out ReloadContext context, out ReloadExecutionResult result)
        {
            context = default;
            result = new ReloadExecutionResult
            {
                weaponId = equipment != null ? equipment.CurrentEquipped.Value : default,
                failureReason = ReloadCancelReason.Interrupted
            };

            if (equipment == null)
                return false;

            WeaponDataSO weapon = equipment.CurrentWeaponData;

            if (weapon == null)
            {
                result.failureReason = ReloadCancelReason.NoWeapon;
                return false;
            }

            WeaponState weaponState = equipment.CurrentWeaponState;
            AmmoDataSO loadedAmmo = equipment.Lookup<AmmoDataSO>(weaponState.loadedAmmoId.ToString());

            context = new ReloadContext
            {
                weapon = weapon,
                weaponState = weaponState,
                loadedAmmo = loadedAmmo,
                currentAmmo = weaponState.currentAmmo,
                maxAmmo = weapon.magSize
            };

            result.weaponId = equipment.CurrentEquipped.Value;
            result.maxAmmo = weapon.magSize;

            return true;
        }

        private bool TryFindAmmoByGrade(
            AmmoType ammoType,
            AmmoGrade grade,
            out AmmoDataSO ammoData,
            out FixedString64Bytes ammoId,
            out int availableCount)
        {
            ammoData = null;
            ammoId = default;
            availableCount = 0;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                FixedString64Bytes slotItemId = ServerGrid[i].itemId;

                if (!TryGetAmmoData(slotItemId, out AmmoDataSO ammo))
                    continue;

                if (ammo.caliber != ammoType || ammo.grade != grade)
                    continue;

                ammoData = ammo;
                ammoId = slotItemId;
                availableCount = CountItemAmount(slotItemId);

                return availableCount > 0;
            }

            return false;
        }

        private bool TryFindAnyCompatibleAmmo(
            AmmoType ammoType,
            out AmmoDataSO ammoData,
            out FixedString64Bytes ammoId,
            out int availableCount)
        {
            ammoData = null;
            ammoId = default;
            availableCount = 0;

            AmmoGrade[] preferredGrades =
            {
                AmmoGrade.LP,
                AmmoGrade.BP,
                AmmoGrade.AP
            };

            for (int i = 0; i < preferredGrades.Length; i++)
            {
                if (TryFindAmmoByGrade(ammoType, preferredGrades[i], out ammoData, out ammoId, out availableCount))
                    return true;
            }

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                FixedString64Bytes slotItemId = ServerGrid[i].itemId;

                if (!TryGetAmmoData(slotItemId, out AmmoDataSO ammo))
                    continue;

                if (ammo.caliber != ammoType)
                    continue;

                ammoData = ammo;
                ammoId = slotItemId;
                availableCount = CountItemAmount(slotItemId);
                return availableCount > 0;
            }

            return false;
        }

        private bool TryConsumeAmmoForReload(
            FixedString64Bytes ammoId,
            int requestedAmount,
            out int consumedAmount)
        {
            consumedAmount = 0;

            if (requestedAmount <= 0 || ammoId.Length == 0)
                return false;

            string itemId = ammoId.ToString();

            if (!HasItem(itemId, requestedAmount))
                return false;

            if (!ConsumeItem(itemId, requestedAmount))
                return false;

            consumedAmount = requestedAmount;
            return true;
        }

        private bool TryReturnAmmoFromMagazine(
            FixedString64Bytes ammoId,
            int amount,
            out int returnedAmount)
        {
            returnedAmount = 0;

            if (amount <= 0)
                return true;

            if (ammoId.Length == 0)
                return false;

            if (!TryGetAmmoData(ammoId, out AmmoDataSO ammo))
                return false;

            returnedAmount += MergeIntoExistingStacks(ammo, amount, out int remaining);
            int maxStackSize = Mathf.Max(1, ammo.maxStackSize);

            while (remaining > 0)
            {
                int stackAmount = Mathf.Min(remaining, maxStackSize);

                if (!TryAddItemDataToNewSlot(ammo, stackAmount))
                    break;

                returnedAmount += stackAmount;
                remaining -= stackAmount;
            }

            return returnedAmount == amount;
        }

        private int MergeIntoExistingStacks(
            ItemDataSO item,
            int amount,
            out int remainingAmount)
        {
            int mergedAmount = 0;
            remainingAmount = Mathf.Max(0, amount);

            if (item == null || remainingAmount <= 0)
                return 0;

            int maxStackSize = Mathf.Max(1, item.maxStackSize);
            FixedString64Bytes itemId = item.itemID;

            for (int i = 0; i < ServerGrid.Count && remainingAmount > 0; i++)
            {
                ItemSlotData slot = ServerGrid[i];

                if (!slot.itemId.Equals(itemId))
                    continue;

                if (slot.stackCount >= maxStackSize)
                    continue;

                int mergeAmount = Mathf.Min(maxStackSize - slot.stackCount, remainingAmount);

                slot.stackCount += (ushort)mergeAmount;
                ServerGrid[i] = slot;

                mergedAmount += mergeAmount;
                remainingAmount -= mergeAmount;
            }

            return mergedAmount;
        }

        private bool TryAddRemainingAsNewStacks(ItemDataSO item, int amount)
        {
            int remainingAmount = Mathf.Max(0, amount);
            int maxStackSize = Mathf.Max(1, item.maxStackSize);

            while (remainingAmount > 0)
            {
                int stackAmount = Mathf.Min(maxStackSize, remainingAmount);

                if (!TryAddItemDataToNewSlot(item, stackAmount))
                    return false;

                remainingAmount -= stackAmount;
            }

            return true;
        }

        private bool CanMergeAsPlainStack(ItemDataSO item, ItemSlotData sourceSlot)
        {
            return item != null &&
                   item.maxStackSize > 1 &&
                   sourceSlot.currentDurability <= 0f &&
                   sourceSlot.currentAmmo == 0;
        }

        private bool TryAddItemDataToNewSlot(ItemDataSO item, int amount)
        {
            for (byte y = 0; y < Height; y++)
            {
                for (byte x = 0; x < BASE_WIDTH; x++)
                {
                    if (!CanPlaceAt(x, y, item.gridSize, false))
                        continue;

                    ServerGrid.Add(new ItemSlotData
                    {
                        itemId = item.itemID,
                        gridX = x,
                        gridY = y,
                        rotated = false,
                        stackCount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize),
                        currentDurability = (item is WeaponDataSO weapon) ? weapon.maxDurability : 0,
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

            return false;
        }

        private void FillReloadResult(
            ref ReloadExecutionResult result,
            AmmoDataSO ammo,
            FixedString64Bytes ammoId,
            int currentAmmo,
            int maxAmmo)
        {
            result.ammoId = ammoId;
            result.grade = ammo != null ? ammo.grade : default;
            result.currentAmmo = currentAmmo;
            result.maxAmmo = maxAmmo;
        }

        private int CountItemAmount(FixedString64Bytes itemId)
        {
            int total = 0;

            for (int i = 0; i < ServerGrid.Count; i++)
            {
                if (ServerGrid[i].itemId.Equals(itemId))
                    total += ServerGrid[i].stackCount;
            }

            return total;
        }

        private bool TryGetAmmoData(FixedString64Bytes ammoId, out AmmoDataSO ammo)
        {
            EnsureItemDatabase();

            ammo = itemDb?.GetById<AmmoDataSO>(ammoId.ToString());
            return ammo != null;
        }

        private void OnBackpackChanged(BackpackChangedEvent evt)
        {
            if (!IsServer || evt.clientId != OwnerClientId)
                return;

            activeSlotCount = GetCapacityByBackpackId(evt.newBackpackId.ToString());
        }

        private int GetCapacityByBackpackId(string backpackId)
        {
            int baseCapacity = BASE_WIDTH * BASE_HEIGHT;

            if (string.IsNullOrWhiteSpace(backpackId))
                return baseCapacity;

            if (itemDb == null)
                EnsureItemDatabase();

            BackpackDataSO backpack = itemDb?.GetById<BackpackDataSO>(backpackId);
            if (backpack == null)
                return baseCapacity;

            return Mathf.Clamp(baseCapacity + Mathf.Max(0, backpack.extraSlots), baseCapacity, 40);
        }

        private void EnsureItemDatabase()
        {
            if (itemDb == null)
                itemDb = ServiceLocator.Get<IItemDatabase>();
        }

        private bool TryCreateSlotFromSnapshot(
            InventoryItemSaveData savedItem,
            List<ItemSlotData> importedSlots,
            out ItemSlotData slot)
        {
            slot = default;

            if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.itemId))
                return false;

            ItemDataSO item = itemDb?.GetById(savedItem.itemId);
            if (item == null)
            {
                Debug.LogWarning($"[RaidLoadout] Missing ItemDataSO while importing inventory. itemId={savedItem.itemId}", this);
                return false;
            }

            if (savedItem.gridX < byte.MinValue || savedItem.gridX > byte.MaxValue ||
                savedItem.gridY < byte.MinValue || savedItem.gridY > byte.MaxValue)
            {
                Debug.LogWarning($"[RaidLoadout] Invalid inventory position. itemId={savedItem.itemId}, x={savedItem.gridX}, y={savedItem.gridY}", this);
                return false;
            }

            byte x = (byte)savedItem.gridX;
            byte y = (byte)savedItem.gridY;

            if (!CanPlaceAt(importedSlots, x, y, item.gridSize, savedItem.rotated))
            {
                Debug.LogWarning($"[RaidLoadout] Cannot place inventory snapshot item. itemId={savedItem.itemId}, x={x}, y={y}, rotated={savedItem.rotated}", this);
                return false;
            }

            slot = new ItemSlotData
            {
                itemId = savedItem.itemId,
                gridX = x,
                gridY = y,
                rotated = savedItem.rotated,
                stackCount = (ushort)Mathf.Clamp(savedItem.stackCount, 1, Mathf.Max(1, item.maxStackSize)),
                currentDurability = Mathf.Max(0f, savedItem.currentDurability),
                currentAmmo = (ushort)Mathf.Clamp(savedItem.currentAmmo, 0, ushort.MaxValue)
            };

            return true;
        }

        private bool CanFitWithinActiveGrid(byte x, byte y, int width, int height)
        {
            if (x + width > BASE_WIDTH)
                return false;

            for (int offsetY = 0; offsetY < height; offsetY++)
            {
                for (int offsetX = 0; offsetX < width; offsetX++)
                {
                    int cellIndex = (y + offsetY) * BASE_WIDTH + (x + offsetX);
                    if (cellIndex < 0 || cellIndex >= activeSlotCount)
                        return false;
                }
            }

            return true;
        }
    }
}

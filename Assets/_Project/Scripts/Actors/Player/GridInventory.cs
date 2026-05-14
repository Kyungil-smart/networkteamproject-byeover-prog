using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
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

        public NetworkList<ItemSlotData> ServerGrid;

        private EquipmentSlots equipment;
        private IItemDatabase itemDb;
        private int activeSlotCount = BASE_WIDTH * BASE_HEIGHT;

        [Header("월드 드롭")]
        [SerializeField] private GameObject droppedLootItemPrefab;
        [SerializeField] private float dropForwardDistance = 1.25f;
        [SerializeField] private float dropGroundRayHeight = 1.5f;
        [SerializeField] private float dropGroundRayDistance = 4f;

        public byte Width => BASE_WIDTH;
        public byte Height => (byte)Mathf.CeilToInt(activeSlotCount / (float)BASE_WIDTH);

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

            if (!TryRemoveSlotAt(gridX, gridY, out ItemSlotData removedSlot))
                return;

            SpawnDroppedLootItem(item, Mathf.Max(1, removedSlot.stackCount), prefab);
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

            if (!equipment.TryRemoveEquipmentSlotForDrop(targetSlot, out _))
                return;

            SpawnDroppedLootItem(item, 1, prefab);
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

        private void SpawnDroppedLootItem(ItemDataSO item, int amount, GameObject prefab)
        {
            if (!IsServer || item == null)
                return;

            Vector3 spawnPosition = ResolveDropPosition();
            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);

            if (instance.TryGetComponent(out LootInteractable lootInteractable))
            {
                lootInteractable.Initialize(item, amount);
            }
            else
            {
                ILootCarrier carrier = instance.GetComponent<ILootCarrier>();
                carrier?.Initialize(item);
            }

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                networkObject.Spawn(destroyWithScene: true);
                return;
            }

            Debug.LogError("[GridInventory] Dropped loot prefab missing NetworkObject.", instance);
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
                return hit.point + Vector3.up * 0.05f;

            return fallback;
        }

        private GameObject ResolveDroppedLootItemPrefab(ItemDataSO item)
        {
            if (droppedLootItemPrefab != null)
                return droppedLootItemPrefab;

            GameObject networkPrefab = FindLootInteractableNetworkPrefab();
            if (networkPrefab != null)
                return networkPrefab;

            LootSpawner spawner = FindFirstObjectByType<LootSpawner>(FindObjectsInactive.Include);
            if (spawner != null)
            {
                FieldInfo field = typeof(LootSpawner).GetField("lootItemPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(spawner) is GameObject spawnerPrefab)
                    return spawnerPrefab;
            }

            if (item != null &&
                item.worldPrefab != null &&
                item.worldPrefab.GetComponent<NetworkObject>() != null &&
                item.worldPrefab.GetComponent<ILootCarrier>() != null)
            {
                return item.worldPrefab;
            }

            return null;
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
            return prefab != null &&
                   prefab.GetComponent<NetworkObject>() != null &&
                   prefab.GetComponent<LootInteractable>() != null;
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

        private ItemDataSO ResolveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            EnsureItemDatabase();
            return itemDb?.GetById(itemId);
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

            AmmoGrade reloadGrade = context.loadedAmmo != null
                ? context.loadedAmmo.grade
                : AmmoGrade.LP;

            if (!TryFindAmmoByGrade(
                    context.weapon.ammoType,
                    reloadGrade,
                    out AmmoDataSO reloadAmmo,
                    out FixedString64Bytes reloadAmmoId,
                    out int availableCount))
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

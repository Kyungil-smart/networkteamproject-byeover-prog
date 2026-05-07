using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

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
            EventBus.Subscribe<ReloadExecuteRequestedEvent>(OnReloadExecuteRequested);

            if (itemDb == null)
            {
                Debug.LogError("[GridInventory] IItemDatabase 서비스가 등록되어 있지 않음. " +
                               "씬 ItemDatabase GameObject 확인.");
            }
        }

        public override void OnNetworkDespawn()
        {
            EventBus.Unsubscribe<ReloadExecuteRequestedEvent>(OnReloadExecuteRequested);
            base.OnNetworkDespawn();
        }

        // ----------- IInventory: Add / Has / Consume -----------

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

        private bool TryFindPlacement(
            List<ItemSlotData> simulatedSlots,
            ItemDataSO item,
            int amount,
            out ItemSlotData newSlot)
        {
            newSlot = default;

            for (byte y = 0; y < BASE_HEIGHT; y++)
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

            if (x + w > BASE_WIDTH || y + h > BASE_HEIGHT) return false;

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

        /// <summary>
        /// ReloadSystem이 장전 시간 종료 후 발행한 실제 장전 요청을 처리한다.
        /// 일반 장전과 탄종 등급 변경 장전을 분기하고, 결과에 따라 완료 또는 취소 이벤트를 발행한다.
        /// </summary>
        private void OnReloadExecuteRequested(ReloadExecuteRequestedEvent e)
        {
            if (!IsServer || e.clientId != OwnerClientId) return;

            ReloadExecutionResult result;
            // 탄등급 변경인지 확인
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

        /// <summary>
        /// 현재 장착 탄종 기준으로 일반 장전을 수행한다.
        /// 현재 장착 탄약이 없으면 기본 탄종인 LP만 탐색하며, BP/AP는 탄종 변경 요청이 있을 때만 사용한다.
        /// </summary>
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

        /// <summary>
        /// 요청된 탄약 등급으로 탄종 변경 장전을 수행한다.
        /// 기존 탄창에 탄약이 남아 있으면 인벤토리에 반환하고, 목표 등급 탄약으로 새 탄창 상태를 반영한다.
        /// </summary>
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

        /// <summary>
        /// 현재 장전 처리를 위한 무기, 탄창, 장착 탄약 정보를 수집한다.
        /// GridInventory가 장비와 소통할 수 있는 유일한 플레이어 인벤토리이므로, 장전 검증의 공통 진입점으로 사용한다.
        /// </summary>
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

        /// <summary>
        /// 현재 무기 구경과 요청된 탄약 등급에 맞는 탄약을 인벤토리에서 찾고 총 보유 수량을 계산한다.
        /// 일반 장전과 탄종 등급 변경 장전이 모두 이 함수를 통해 탄약 선택 규칙을 공유한다.
        /// </summary>
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
                if (!TryGetAmmoData(slotItemId, out AmmoDataSO ammo)) continue;
                if (ammo.caliber != ammoType || ammo.grade != grade) continue;

                ammoData = ammo;
                ammoId = slotItemId;
                availableCount = CountItemAmount(slotItemId);
                return availableCount > 0;
            }

            return false;
        }

        /// <summary>
        /// 지정된 탄약 ID를 인벤토리에서 요청 수량만큼 차감한다.
        /// 요청 수량을 모두 차감할 수 있을 때만 성공하며, 일부만 차감하는 상태는 만들지 않는다.
        /// </summary>
        private bool TryConsumeAmmoForReload(
            FixedString64Bytes ammoId,
            int requestedAmount,
            out int consumedAmount)
        {
            consumedAmount = 0;
            if (requestedAmount <= 0 || ammoId.Length == 0) return false;

            string itemId = ammoId.ToString();
            if (!HasItem(itemId, requestedAmount)) return false;
            if (!ConsumeItem(itemId, requestedAmount)) return false;

            consumedAmount = requestedAmount;
            return true;
        }

        /// <summary>
        /// 탄종 변경 장전으로 탄창에서 빠진 기존 탄약을 인벤토리에 반환한다.
        /// 탄약 스택 최대 수량에 맞춰 여러 슬롯으로 나누어 추가하고, 공간이 부족하면 반환된 수량만 보고한다.
        /// </summary>
        private bool TryReturnAmmoFromMagazine(
            FixedString64Bytes ammoId,
            int amount,
            out int returnedAmount)
        {
            returnedAmount = 0;
            if (amount <= 0) return true;
            if (ammoId.Length == 0) return false;
            if (!TryGetAmmoData(ammoId, out AmmoDataSO ammo)) return false;

            returnedAmount += MergeIntoExistingStacks(ammo, amount, out int remaining);
            int maxStackSize = Mathf.Max(1, ammo.maxStackSize);

            while (remaining > 0)
            {
                int stackAmount = Mathf.Min(remaining, maxStackSize);
                if (!TryAddItemDataToNewSlot(ammo, stackAmount)) break;

                returnedAmount += stackAmount;
                remaining -= stackAmount;
            }

            return returnedAmount == amount;
        }

        /// <summary>
        /// 같은 아이템 ID의 기존 스택에 먼저 수량을 병합한다.
        /// 병합 후에도 남은 수량은 remainingAmount로 돌려주며, TryAddItem의 첫 단계로 사용한다.
        /// </summary>
        private int MergeIntoExistingStacks(
            ItemDataSO item,
            int amount,
            out int remainingAmount)
        {
            int mergedAmount = 0;
            remainingAmount = Mathf.Max(0, amount);
            if (item == null || remainingAmount <= 0) return 0;

            int maxStackSize = Mathf.Max(1, item.maxStackSize);
            FixedString64Bytes itemId = item.itemID;

            for (int i = 0; i < ServerGrid.Count && remainingAmount > 0; i++)
            {
                ItemSlotData slot = ServerGrid[i];
                if (!slot.itemId.Equals(itemId)) continue;
                if (slot.stackCount >= maxStackSize) continue;

                int mergeAmount = Mathf.Min(maxStackSize - slot.stackCount, remainingAmount);
                slot.stackCount += (ushort)mergeAmount;
                ServerGrid[i] = slot;

                mergedAmount += mergeAmount;
                remainingAmount -= mergeAmount;
            }

            return mergedAmount;
        }

        /// <summary>
        /// 기존 스택 병합 후 남은 수량을 새 슬롯들에 나누어 배치한다.
        /// 모든 남은 수량을 배치했을 때만 true를 반환한다.
        /// </summary>
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

        /// <summary>
        /// 아이템을 새 그리드 슬롯 하나에 배치한다.
        /// 스택 병합은 이 함수 밖에서 처리하며, 여기서는 빈 위치 탐색과 슬롯 생성만 담당한다.
        /// </summary>
        private bool TryAddItemDataToNewSlot(ItemDataSO item, int amount)
        {
            for (byte y = 0; y < BASE_HEIGHT; y++)
            {
                for (byte x = 0; x < BASE_WIDTH; x++)
                {
                    if (!CanPlaceAt(x, y, item.gridSize, false)) continue;

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

            return false;
        }

        /// <summary>
        /// 장전 완료 이벤트에 들어갈 결과 정보를 채운다.
        /// 실제 탄창 상태 변경 이후의 탄약 ID, 등급, 탄 수를 한곳에서 정리한다.
        /// </summary>
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

        /// <summary>
        /// 인벤토리 안에서 특정 아이템 ID를 가진 모든 슬롯의 stackCount 합계를 계산한다.
        /// 장전 전 탄약 보유량 조회와 요청 수량 검증에 사용한다.
        /// </summary>
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

        /// <summary>
        /// 아이템 ID가 탄약 데이터인지 확인하고 AmmoDataSO로 반환한다.
        /// ItemDatabase가 아직 캐시되지 않은 경우 ServiceLocator에서 한 번 더 조회한다.
        /// </summary>
        private bool TryGetAmmoData(FixedString64Bytes ammoId, out AmmoDataSO ammo)
        {
            if (itemDb == null)
                itemDb = ServiceLocator.Get<IItemDatabase>();

            ammo = itemDb?.GetById<AmmoDataSO>(ammoId.ToString());
            return ammo != null;
        }
    }
}

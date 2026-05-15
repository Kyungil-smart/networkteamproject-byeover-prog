using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors
{
    public enum EquipmentTargetSlot : byte
    {
        None,
        Head,
        Backpack,
        Armor,
        Primary1,
        Primary2,
        Secondary,
        Melee
    }

    public enum WeaponAmmoChangeReason : byte
    {
        // 변경 사유가 지정되지 않았을 때 사용한다.
        Unknown,

        // 발사로 인해 현재 탄창 수량이 감소했을 때 사용한다.
        Fired,

        // 일반 장전으로 현재 탄창 수량이 증가했을 때 사용한다.
        Reloaded,

        // 장착 탄약의 등급이 변경되면서 탄약 ID 또는 탄창 수량이 바뀌었을 때 사용한다.
        AmmoGradeChanged,

        // 무기를 슬롯에 장착하면서 초기 탄약 상태가 설정되었을 때 사용한다.
        Equipped,

        // 무기를 슬롯에서 해제하면서 탄약 상태가 비워졌을 때 사용한다.
        Unequipped,

        // 서버 상태를 기준으로 클라이언트 표시 정보를 다시 맞췄을 때 사용한다.
        Synced
    }

    public class EquipmentSlots : NetworkBehaviour, IArmored
    {
        // ----------- 무기 런타임 상태 -----------

        [HideInInspector] public NetworkVariable<WeaponState> Primary1State  = new();
        [HideInInspector] public NetworkVariable<WeaponState> Primary2State  = new();
        [HideInInspector] public NetworkVariable<WeaponState> SecondaryState = new();

        // ----------- 슬롯 ID -----------

        public NetworkVariable<FixedString64Bytes> HeadSlotId      = new("");
        public NetworkVariable<FixedString64Bytes> TorsoSlotId     = new("");
        public NetworkVariable<FixedString64Bytes> BackpackSlotId  = new("");
        public NetworkVariable<FixedString64Bytes> Primary1Id      = new("");
        public NetworkVariable<FixedString64Bytes> Primary2Id      = new("");
        public NetworkVariable<FixedString64Bytes> SecondaryId     = new("");
        public NetworkVariable<FixedString64Bytes> MeleeId         = new("");
        public NetworkVariable<FixedString64Bytes> CurrentEquipped = new("");

        // ----------- 방어구 내구도 -----------

        public NetworkVariable<float> HelmetDurability = new(0f);
        public NetworkVariable<float> ArmorDurability  = new(0f);

        // ----------- 디버그 -----------

        [Header("디버그")]
        [Tooltip("장비 슬롯 변경 로그를 출력합니다. 일반 플레이 테스트나 최종 빌드 전에는 꺼두는 것을 권장합니다.")]
        [SerializeField] private bool showDebugLogs = false;

        // ----------- 서비스 -----------

        private IItemDatabase itemDb;

        // ----------- Lifecycle -----------

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            itemDb = ServiceLocator.Get<IItemDatabase>();

            if (itemDb == null)
            {
                Debug.LogError("[EquipmentSlots] IItemDatabase 서비스가 등록되어 있지 않음.");
            }
        }

        // ----------- Lookup -----------

        public ItemDataSO Lookup(string itemId) => itemDb?.GetById(itemId);
        public T Lookup<T>(string itemId) where T : ItemDataSO => itemDb?.GetById<T>(itemId);

        // ----------- 현재 무기 ----------

        public WeaponDataSO GetCurrentWeapon()
            => Lookup<WeaponDataSO>(CurrentEquipped.Value.ToString());

        public WeaponState CurrentWeaponState
        {
            get
            {
                FixedString64Bytes curId = CurrentEquipped.Value;

                // CurrentEquipped와 빈 슬롯 ID가 서로 같다고 판단되는 것을 막는다.
                // 비무장 상태에서는 탄약/장전 로직이 어떤 슬롯도 현재 무기로 오판하면 안 된다.
                if (curId.Length == 0)
                    return default;

                if (curId == Primary1Id.Value)  return Primary1State.Value;
                if (curId == Primary2Id.Value)  return Primary2State.Value;
                if (curId == SecondaryId.Value) return SecondaryState.Value;

                return default;
            }
        }

        public WeaponDataSO CurrentWeaponData => GetCurrentWeapon();

        public AmmoDataSO CurrentAmmoData
            => Lookup<AmmoDataSO>(CurrentWeaponState.loadedAmmoId.ToString());

        public List<EquipmentSaveData> ExportSnapshot()
        {
            return new List<EquipmentSaveData>
            {
                CreateEquipmentSaveData("Head", HeadSlotId.Value, default, HelmetDurability.Value),
                CreateEquipmentSaveData("Torso", TorsoSlotId.Value, default, ArmorDurability.Value),
                CreateEquipmentSaveData("Backpack", BackpackSlotId.Value, default, 0f),
                CreateEquipmentSaveData("Primary1", Primary1Id.Value, Primary1State.Value, 0f),
                CreateEquipmentSaveData("Primary2", Primary2Id.Value, Primary2State.Value, 0f),
                CreateEquipmentSaveData("Secondary", SecondaryId.Value, SecondaryState.Value, 0f),
                CreateEquipmentSaveData("Melee", MeleeId.Value, default, 0f)
            };
        }

        public int ImportSnapshot(
            IReadOnlyList<EquipmentSaveData> snapshot,
            string requestedCurrentEquippedItemId)
        {
            if (!IsServer)
                return 0;

            ClearAllSlots();

            if (snapshot == null || snapshot.Count == 0)
                return 0;

            int appliedCount = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (TryApplyEquipmentSnapshot(snapshot[i]))
                    appliedCount++;
            }

            CurrentEquipped.Value = ResolveCurrentEquipped(requestedCurrentEquippedItemId);

            return appliedCount;
        }

        /// <summary>
        /// 현재 장착된 무기가 어느 무기 슬롯에 해당하는지 확인한다.
        /// 장전과 발사처럼 현재 무기의 탄창 상태를 변경해야 하는 코드에서 공통 기준으로 사용한다.
        /// </summary>
        public bool TryGetCurrentWeaponSlot(out WeaponSlot slot)
        {
            FixedString64Bytes curId = CurrentEquipped.Value;

            // 빈 CurrentEquipped가 빈 Primary 슬롯과 같다고 판정되는 것을 방지한다.
            // 이 방어가 없으면 비무장 상태에서도 Primary1 장착으로 오판할 수 있다.
            if (curId.Length == 0)
            {
                slot = WeaponSlot.None;
                return false;
            }

            if (curId == Primary1Id.Value)
            {
                slot = WeaponSlot.Primary1;
                return true;
            }

            if (curId == Primary2Id.Value)
            {
                slot = WeaponSlot.Primary2;
                return true;
            }

            if (curId == SecondaryId.Value)
            {
                slot = WeaponSlot.Secondary;
                return true;
            }

            slot = WeaponSlot.None;
            return false;
        }

        /// <summary>
        /// 현재 장착 무기의 WeaponState를 서버 권한으로 갱신한다.
        /// 탄창 수량이나 장착 탄약 ID 변경은 이 함수를 통해 처리해 WeaponAmmoChangedEvent 발행을 보장한다.
        /// </summary>
        public bool TryApplyCurrentWeaponState(
            WeaponState nextState,
            WeaponAmmoChangeReason reason)
        {
            if (!IsServer) return false;
            if (!TryGetCurrentWeaponSlot(out WeaponSlot slot)) return false;

            WeaponDataSO weapon = CurrentWeaponData;
            if (weapon != null)
                nextState.currentAmmo = Mathf.Clamp(nextState.currentAmmo, 0, weapon.magSize);

            SetWeaponState(slot, nextState, reason);
            return true;
        }

        // ----------- IArmored -----------

        public HelmetDataSO GetEquippedHelmet()
            => Lookup<HelmetDataSO>(HeadSlotId.Value.ToString());

        public ArmorDataSO GetEquippedArmor()
            => Lookup<ArmorDataSO>(TorsoSlotId.Value.ToString());

        public float GetHelmetDurability() => HelmetDurability.Value;
        public float GetArmorDurability()  => ArmorDurability.Value;

        public void DamageHelmetDurability(float amount)
        {
            if (!IsServer) return;
            HelmetDurability.Value = Mathf.Max(0f, HelmetDurability.Value - amount);
        }

        public void DamageArmorDurability(float amount)
        {
            if (!IsServer) return;
            ArmorDurability.Value = Mathf.Max(0f, ArmorDurability.Value - amount);
        }

        // ----------- 탄약 소모 -----------

        public void ConsumeCurrentWeaponAmmo()
        {
            if (!IsServer) return;
            if (!TryGetCurrentWeaponSlot(out WeaponSlot slot)) return;

            WeaponState state = GetWeaponState(slot);
            if (state.currentAmmo <= 0) return;

            state.currentAmmo--;
            SetWeaponState(slot, state, WeaponAmmoChangeReason.Fired);
        }

        /// <summary>
        /// 지정한 무기 슬롯의 현재 WeaponState를 반환한다.
        /// 슬롯별 NetworkVariable 접근을 한곳에 모아 탄약 변경 로직이 같은 기준을 사용하게 한다.
        /// </summary>
        private WeaponState GetWeaponState(WeaponSlot slot)
        {
            return slot switch
            {
                WeaponSlot.Primary1 => Primary1State.Value,
                WeaponSlot.Primary2 => Primary2State.Value,
                WeaponSlot.Secondary => SecondaryState.Value,
                _ => default
            };
        }

        /// <summary>
        /// 지정한 무기 슬롯의 WeaponState를 변경하고 탄약 변경 이벤트를 발행한다.
        /// 발사, 일반 장전, 탄종 변경 장전 모두 이 함수로 들어와 변경 전/후 정보가 누락되지 않게 한다.
        /// </summary>
        private void SetWeaponState(
            WeaponSlot slot,
            WeaponState nextState,
            WeaponAmmoChangeReason reason)
        {
            WeaponState previousState = GetWeaponState(slot);

            switch (slot)
            {
                case WeaponSlot.Primary1:
                    Primary1State.Value = nextState;
                    break;
                case WeaponSlot.Primary2:
                    Primary2State.Value = nextState;
                    break;
                case WeaponSlot.Secondary:
                    SecondaryState.Value = nextState;
                    break;
                default:
                    return;
            }

            PublishWeaponAmmoChanged(slot, previousState, nextState, reason);
        }

        /// <summary>
        /// WeaponState 변경 전후 정보를 WeaponAmmoChangedEvent로 발행한다.
        /// UI와 관전 표시가 어느 플레이어의 어느 무기 탄약이 어떻게 바뀌었는지 알 수 있게 한다.
        /// </summary>
        private void PublishWeaponAmmoChanged(
            WeaponSlot slot,
            WeaponState previousState,
            WeaponState nextState,
            WeaponAmmoChangeReason reason)
        {
            WeaponDataSO weapon = CurrentWeaponData;
            AmmoDataSO previousAmmo = Lookup<AmmoDataSO>(previousState.loadedAmmoId.ToString());
            AmmoDataSO nextAmmo = Lookup<AmmoDataSO>(nextState.loadedAmmoId.ToString());

            EventBus.Publish(new WeaponAmmoChangedEvent
            {
                clientId = OwnerClientId,
                weaponId = CurrentEquipped.Value,
                weaponSlot = (byte)slot,
                beforeAmmoId = previousState.loadedAmmoId,
                afterAmmoId = nextState.loadedAmmoId,
                beforeGrade = previousAmmo != null ? previousAmmo.grade : default,
                afterGrade = nextAmmo != null ? nextAmmo.grade : default,
                beforeAmmo = previousState.currentAmmo,
                afterAmmo = nextState.currentAmmo,
                maxAmmo = weapon != null ? weapon.magSize : 0,
                reason = (byte)reason
            });
        }

        private EquipmentSaveData CreateEquipmentSaveData(
            string slotId,
            FixedString64Bytes itemId,
            WeaponState weaponState,
            float durability)
        {
            string itemIdText = itemId.ToString();

            return new EquipmentSaveData
            {
                slotId = slotId,
                itemId = itemIdText,
                instanceId = string.IsNullOrWhiteSpace(itemIdText) ? string.Empty : $"{slotId}_{itemIdText}",
                loadedAmmoId = weaponState.loadedAmmoId.ToString(),
                currentAmmo = weaponState.currentAmmo,
                currentDurability = durability
            };
        }

        private void ClearAllSlots()
        {
            HeadSlotId.Value = "";
            TorsoSlotId.Value = "";
            EquipBackpack(new FixedString64Bytes(""));
            Primary1Id.Value = "";
            Primary2Id.Value = "";
            SecondaryId.Value = "";
            MeleeId.Value = "";
            CurrentEquipped.Value = "";

            Primary1State.Value = default;
            Primary2State.Value = default;
            SecondaryState.Value = default;
            HelmetDurability.Value = 0f;
            ArmorDurability.Value = 0f;
        }

        private bool TryApplyEquipmentSnapshot(EquipmentSaveData savedItem)
        {
            if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.slotId))
                return false;

            FixedString64Bytes itemId = string.IsNullOrWhiteSpace(savedItem.itemId)
                ? new FixedString64Bytes("")
                : new FixedString64Bytes(savedItem.itemId);

            switch (savedItem.slotId)
            {
                case "Head":
                    HeadSlotId.Value = itemId;
                    HelmetDurability.Value = ResolveDurability<HelmetDataSO>(savedItem.itemId, savedItem.currentDurability);
                    return itemId.Length > 0;

                case "Torso":
                    TorsoSlotId.Value = itemId;
                    ArmorDurability.Value = ResolveDurability<ArmorDataSO>(savedItem.itemId, savedItem.currentDurability);
                    return itemId.Length > 0;

                case "Backpack":
                    EquipBackpack(itemId);
                    return itemId.Length > 0;

                case "Primary1":
                    Primary1Id.Value = itemId;
                    Primary1State.Value = CreateWeaponState(savedItem);
                    return itemId.Length > 0;

                case "Primary2":
                    Primary2Id.Value = itemId;
                    Primary2State.Value = CreateWeaponState(savedItem);
                    return itemId.Length > 0;

                case "Secondary":
                    SecondaryId.Value = itemId;
                    SecondaryState.Value = CreateWeaponState(savedItem);
                    return itemId.Length > 0;

                case "Melee":
                    MeleeId.Value = itemId;
                    return itemId.Length > 0;

                default:
                    Debug.LogWarning($"[RaidLoadout] Unknown equipment slotId={savedItem.slotId}, itemId={savedItem.itemId}", this);
                    return false;
            }
        }

        private WeaponState CreateWeaponState(EquipmentSaveData savedItem)
        {
            return new WeaponState
            {
                loadedAmmoId = string.IsNullOrWhiteSpace(savedItem.loadedAmmoId)
                    ? new FixedString64Bytes("")
                    : new FixedString64Bytes(savedItem.loadedAmmoId),
                currentAmmo = Mathf.Max(0, savedItem.currentAmmo)
            };
        }

        private float ResolveDurability<T>(string itemId, float savedDurability) where T : ItemDataSO
        {
            if (savedDurability > 0f)
                return savedDurability;

            if (string.IsNullOrWhiteSpace(itemId))
                return 0f;

            if (Lookup<T>(itemId) is HelmetDataSO helmet)
                return helmet.maxDurability;

            if (Lookup<T>(itemId) is ArmorDataSO armor)
                return armor.maxDurability;

            return 0f;
        }

        /// <summary>
        /// 저장 데이터가 요청한 현재 장착 무기를 복원할 수 있으면 그 무기를 사용한다.
        /// 요청값이 비어 있거나 실제 슬롯에 없으면 Primary1 → Primary2 → Secondary → Melee 순서로 대체 장착 무기를 선택한다.
        /// </summary>
        private FixedString64Bytes ResolveCurrentEquipped(string requestedItemId)
        {
            if (!string.IsNullOrWhiteSpace(requestedItemId))
            {
                FixedString64Bytes requested = new FixedString64Bytes(requestedItemId);

                if (IsEquippedIdValid(requested))
                    return requested;
            }

            return GetFirstAvailableWeaponId();
        }

        /// <summary>
        /// 서버 권한으로 무기 슬롯 ID와 WeaponState를 갱신한다.
        /// 슬롯 변경 후 CurrentEquipped가 실제 슬롯 상태와 어긋나지 않도록 즉시 보정한다.
        /// </summary>
        public void UpdateSlot(WeaponSlot slot, string itemId, WeaponState state)
        {
            if (!IsServer) return;

            FixedString64Bytes previousSlotId = GetWeaponSlotId(slot);
            FixedString64Bytes nextSlotId = string.IsNullOrWhiteSpace(itemId)
                ? new FixedString64Bytes("")
                : new FixedString64Bytes(itemId);

            if (showDebugLogs)
            {
                Debug.Log(
                    $"[EquipmentSlots] UpdateSlot: owner={OwnerClientId}, slot={slot}, itemId={nextSlotId}, ammo={state.currentAmmo}",
                    this);
            }

            switch (slot)
            {
                case WeaponSlot.Primary1:
                    Primary1Id.Value = nextSlotId;
                    Primary1State.Value = state;
                    break;

                case WeaponSlot.Primary2:
                    Primary2Id.Value = nextSlotId;
                    Primary2State.Value = state;
                    break;

                case WeaponSlot.Secondary:
                    SecondaryId.Value = nextSlotId;
                    SecondaryState.Value = state;
                    break;

                case WeaponSlot.Melee:
                    MeleeId.Value = nextSlotId;
                    break;

                default:
                    return;
            }

            ReconcileCurrentEquippedAfterSlotUpdate(previousSlotId, nextSlotId);
        }

        /// <summary>
        /// 무기 슬롯 변경 이후 CurrentEquipped가 실제 슬롯 상태와 일치하도록 보정한다.
        /// 현재 장착 중인 슬롯이 비워지면 Primary1 → Primary2 → Secondary → Melee 순서로 fallback을 선택한다.
        /// </summary>
        private void ReconcileCurrentEquippedAfterSlotUpdate(
            FixedString64Bytes previousSlotId,
            FixedString64Bytes nextSlotId)
        {
            FixedString64Bytes currentEquipped = CurrentEquipped.Value;

            // 아직 어떤 무기도 현재 장착으로 지정되지 않은 상태에서 새 무기가 들어오면 첫 장착 무기로 사용한다.
            if (currentEquipped.Length == 0)
            {
                if (nextSlotId.Length > 0)
                    CurrentEquipped.Value = nextSlotId;

                return;
            }

            bool updatedSlotWasCurrent =
                previousSlotId.Length > 0 &&
                currentEquipped == previousSlotId;

            // 현재 장착 중이던 슬롯이 교체되거나 비워진 경우, 새 ID 또는 fallback으로 현재 장착 무기를 정리한다.
            if (updatedSlotWasCurrent)
            {
                CurrentEquipped.Value = nextSlotId.Length > 0
                    ? nextSlotId
                    : GetFirstAvailableWeaponId();

                return;
            }

            // CurrentEquipped가 어느 슬롯에도 없는 오래된 ID라면 실제 슬롯 기준으로 다시 맞춘다.
            if (!IsEquippedIdValid(currentEquipped))
            {
                CurrentEquipped.Value = GetFirstAvailableWeaponId();
            }
        }

        /// <summary>
        /// 지정한 무기 슬롯의 현재 itemId를 반환한다.
        /// 슬롯 변경 전후 비교와 CurrentEquipped 정합성 보정에서 사용한다.
        /// </summary>
        private FixedString64Bytes GetWeaponSlotId(WeaponSlot slot)
        {
            return slot switch
            {
                WeaponSlot.Primary1 => Primary1Id.Value,
                WeaponSlot.Primary2 => Primary2Id.Value,
                WeaponSlot.Secondary => SecondaryId.Value,
                WeaponSlot.Melee => MeleeId.Value,
                _ => new FixedString64Bytes("")
            };
        }

        /// <summary>
        /// CurrentEquipped가 실제 장착 슬롯 중 하나를 가리키는지 확인한다.
        /// 빈 슬롯과 빈 CurrentEquipped가 같다고 처리되지 않도록 빈 값은 항상 유효하지 않은 값으로 본다.
        /// </summary>
        private bool IsEquippedIdValid(FixedString64Bytes equippedId)
        {
            if (equippedId.Length == 0)
                return false;

            return IsSameOccupiedSlot(Primary1Id.Value, equippedId) ||
                   IsSameOccupiedSlot(Primary2Id.Value, equippedId) ||
                   IsSameOccupiedSlot(SecondaryId.Value, equippedId) ||
                   IsSameOccupiedSlot(MeleeId.Value, equippedId);
        }

        private bool IsSameOccupiedSlot(FixedString64Bytes slotId, FixedString64Bytes itemId)
        {
            return slotId.Length > 0 && slotId == itemId;
        }

        /// <summary>
        /// 현재 장착 무기 fallback을 선택한다.
        /// 저장 복원, 슬롯 해제, stale CurrentEquipped 보정에서 같은 우선순위를 사용한다.
        /// </summary>
        private FixedString64Bytes GetFirstAvailableWeaponId()
        {
            if (Primary1Id.Value.Length > 0) return Primary1Id.Value;
            if (Primary2Id.Value.Length > 0) return Primary2Id.Value;
            if (SecondaryId.Value.Length > 0) return SecondaryId.Value;
            if (MeleeId.Value.Length > 0) return MeleeId.Value;

            return new FixedString64Bytes("");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void EquipWeaponSlotServerRpc(
            FixedString64Bytes itemId,
            WeaponSlot slot,
            FixedString64Bytes loadedAmmoId,
            ushort currentAmmo)
        {
            WeaponState state = new()
            {
                loadedAmmoId = loadedAmmoId,
                currentAmmo = currentAmmo
            };

            UpdateSlot(slot, itemId.ToString(), state);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClearWeaponSlotServerRpc(WeaponSlot slot)
        {
            UpdateSlot(slot, string.Empty, default);
        }

        // ----------- 장착 ServerRpc -----------

        [ServerRpc]
        public void EquipHelmetServerRpc(FixedString64Bytes helmetId)
        {
            HeadSlotId.Value = helmetId;
            var h = Lookup<HelmetDataSO>(helmetId.ToString());
            HelmetDurability.Value = h != null ? h.maxDurability : 0f;
        }

        [ServerRpc]
        public void EquipArmorServerRpc(FixedString64Bytes armorId)
        {
            TorsoSlotId.Value = armorId;
            var a = Lookup<ArmorDataSO>(armorId.ToString());
            ArmorDurability.Value = a != null ? a.maxDurability : 0f;
        }

        [ServerRpc]
        public void EquipBackpackServerRpc(FixedString64Bytes backpackId)
        {
            EquipBackpack(backpackId);
        }

        public void EquipBackpack(FixedString64Bytes backpackId)
        {
            if (!IsServer)
                return;

            FixedString64Bytes previousBackpackId = BackpackSlotId.Value;
            BackpackSlotId.Value = backpackId;

            EventBus.Publish(new BackpackChangedEvent
            {
                clientId = OwnerClientId,
                oldBackpackId = previousBackpackId,
                newBackpackId = backpackId
            });
        }

        public bool CanEquipItemToSlot(ItemDataSO item, EquipmentTargetSlot targetSlot)
        {
            if (item == null)
                return false;

            return targetSlot switch
            {
                EquipmentTargetSlot.Head => item is HelmetDataSO || item.category == ItemCategory.Helmet,
                EquipmentTargetSlot.Backpack => item is BackpackDataSO || item.category == ItemCategory.Backpack,
                EquipmentTargetSlot.Armor => item is ArmorDataSO || item.category == ItemCategory.Armor,
                EquipmentTargetSlot.Primary1 => IsPrimaryWeapon(item),
                EquipmentTargetSlot.Primary2 => IsPrimaryWeapon(item),
                EquipmentTargetSlot.Secondary => IsSecondaryWeapon(item),
                EquipmentTargetSlot.Melee => IsMeleeWeapon(item),
                _ => false
            };
        }

        public bool TryEquipItemToEmptySlot(ItemDataSO item, EquipmentTargetSlot targetSlot)
        {
            if (!IsServer || !CanEquipItemToSlot(item, targetSlot) || !IsEquipmentSlotEmpty(targetSlot))
                return false;

            return TryEquipItemToSlot(item, default, targetSlot);
        }

        public bool TryEquipItemToSlot(ItemDataSO item, ItemSlotData sourceSlot, EquipmentTargetSlot targetSlot)
        {
            if (!IsServer || !CanEquipItemToSlot(item, targetSlot))
                return false;

            FixedString64Bytes itemId = new(item.itemID);

            switch (targetSlot)
            {
                case EquipmentTargetSlot.Head:
                    HeadSlotId.Value = itemId;
                    HelmetDurability.Value = sourceSlot.currentDurability > 0f
                        ? sourceSlot.currentDurability
                        : item is HelmetDataSO helmet ? helmet.maxDurability : 0f;
                    return true;

                case EquipmentTargetSlot.Backpack:
                    EquipBackpack(itemId);
                    return true;

                case EquipmentTargetSlot.Armor:
                    TorsoSlotId.Value = itemId;
                    ArmorDurability.Value = sourceSlot.currentDurability > 0f
                        ? sourceSlot.currentDurability
                        : item is ArmorDataSO armor ? armor.maxDurability : 0f;
                    return true;

                case EquipmentTargetSlot.Primary1:
                    UpdateSlot(WeaponSlot.Primary1, item.itemID, CreateWeaponStateFromInventorySlot(item, sourceSlot));
                    return true;

                case EquipmentTargetSlot.Primary2:
                    UpdateSlot(WeaponSlot.Primary2, item.itemID, CreateWeaponStateFromInventorySlot(item, sourceSlot));
                    return true;

                case EquipmentTargetSlot.Secondary:
                    UpdateSlot(WeaponSlot.Secondary, item.itemID, CreateWeaponStateFromInventorySlot(item, sourceSlot));
                    return true;

                case EquipmentTargetSlot.Melee:
                    UpdateSlot(WeaponSlot.Melee, item.itemID, default);
                    return true;

                default:
                    return false;
            }
        }

        public bool TryRemoveEquipmentSlotForDrop(EquipmentTargetSlot targetSlot, out string itemId)
        {
            itemId = string.Empty;

            if (!IsServer)
                return false;

            switch (targetSlot)
            {
                case EquipmentTargetSlot.Head:
                    itemId = HeadSlotId.Value.ToString();
                    if (string.IsNullOrWhiteSpace(itemId))
                        return false;

                    HeadSlotId.Value = "";
                    HelmetDurability.Value = 0f;
                    return true;

                case EquipmentTargetSlot.Backpack:
                    itemId = BackpackSlotId.Value.ToString();
                    if (string.IsNullOrWhiteSpace(itemId))
                        return false;

                    EquipBackpack(new FixedString64Bytes(""));
                    return true;

                case EquipmentTargetSlot.Armor:
                    itemId = TorsoSlotId.Value.ToString();
                    if (string.IsNullOrWhiteSpace(itemId))
                        return false;

                    TorsoSlotId.Value = "";
                    ArmorDurability.Value = 0f;
                    return true;

                case EquipmentTargetSlot.Primary1:
                    return TryClearWeaponSlotForDrop(WeaponSlot.Primary1, out itemId);

                case EquipmentTargetSlot.Primary2:
                    return TryClearWeaponSlotForDrop(WeaponSlot.Primary2, out itemId);

                case EquipmentTargetSlot.Secondary:
                    return TryClearWeaponSlotForDrop(WeaponSlot.Secondary, out itemId);

                case EquipmentTargetSlot.Melee:
                    return TryClearWeaponSlotForDrop(WeaponSlot.Melee, out itemId);

                default:
                    return false;
            }
        }

        public bool IsEquipmentSlotEmpty(EquipmentTargetSlot targetSlot)
        {
            return targetSlot switch
            {
                EquipmentTargetSlot.Head => HeadSlotId.Value.Length == 0,
                EquipmentTargetSlot.Backpack => BackpackSlotId.Value.Length == 0,
                EquipmentTargetSlot.Armor => TorsoSlotId.Value.Length == 0,
                EquipmentTargetSlot.Primary1 => Primary1Id.Value.Length == 0,
                EquipmentTargetSlot.Primary2 => Primary2Id.Value.Length == 0,
                EquipmentTargetSlot.Secondary => SecondaryId.Value.Length == 0,
                EquipmentTargetSlot.Melee => MeleeId.Value.Length == 0,
                _ => false
            };
        }

        private static bool IsPrimaryWeapon(ItemDataSO item)
        {
            return item is WeaponDataSO weapon &&
                   weapon.weaponCategory != WeaponCategory.Handgun &&
                   weapon.weaponCategory != WeaponCategory.Melee;
        }

        private static bool IsSecondaryWeapon(ItemDataSO item)
        {
            return item is WeaponDataSO weapon && weapon.weaponCategory == WeaponCategory.Handgun;
        }

        private static bool IsMeleeWeapon(ItemDataSO item)
        {
            return item is WeaponDataSO weapon && weapon.weaponCategory == WeaponCategory.Melee;
        }

        private static WeaponState CreateInitialWeaponState(ItemDataSO item)
        {
            return item is WeaponDataSO weapon
                ? new WeaponState { loadedAmmoId = "", currentAmmo = 0 }
                : default;
        }

        private static WeaponState CreateWeaponStateFromInventorySlot(ItemDataSO item, ItemSlotData sourceSlot)
        {
            return item is WeaponDataSO
                ? new WeaponState
                {
                    loadedAmmoId = "",
                    currentAmmo = Mathf.Clamp(sourceSlot.currentAmmo, 0, ushort.MaxValue)
                }
                : default;
        }

        private bool TryClearWeaponSlotForDrop(WeaponSlot weaponSlot, out string itemId)
        {
            itemId = GetWeaponSlotId(weaponSlot).ToString();
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            UpdateSlot(weaponSlot, string.Empty, default);
            return true;
        }

        [ServerRpc]
        public void SwitchToServerRpc(WeaponSlot slot)
        {
            CurrentEquipped.Value = slot switch
            {
                WeaponSlot.Primary1  => Primary1Id.Value,
                WeaponSlot.Primary2  => Primary2Id.Value,
                WeaponSlot.Secondary => SecondaryId.Value,
                WeaponSlot.Melee     => MeleeId.Value,
                _ => CurrentEquipped.Value,
            };
        }
    }

    public enum WeaponSlot : byte
    {
        None,
        Primary1,
        Primary2,
        Secondary,
        Melee
    }
}

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
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
        [HideInInspector] public NetworkVariable<FixedString64Bytes> HeadSlotId      = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> TorsoSlotId     = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> Primary1Id      = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> Primary2Id      = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> SecondaryId     = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> MeleeId         = new("");
        [HideInInspector] public NetworkVariable<FixedString64Bytes> CurrentEquipped = new("");

        // ----------- 방어구 내구도 -----------

        public NetworkVariable<float> HelmetDurability = new(0f);
        public NetworkVariable<float> ArmorDurability  = new(0f);

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
                if (curId == Primary1Id.Value)  return Primary1State.Value;
                if (curId == Primary2Id.Value)  return Primary2State.Value;
                if (curId == SecondaryId.Value) return SecondaryState.Value;
                return default;
            }
        }

        public WeaponDataSO CurrentWeaponData => GetCurrentWeapon();

        public AmmoDataSO CurrentAmmoData
            => Lookup<AmmoDataSO>(CurrentWeaponState.loadedAmmoId.ToString());

        /// <summary>
        /// 현재 장착된 무기가 어느 무기 슬롯에 해당하는지 확인한다.
        /// 장전과 발사처럼 현재 무기의 탄창 상태를 변경해야 하는 코드에서 공통 기준으로 사용한다.
        /// </summary>
        public bool TryGetCurrentWeaponSlot(out WeaponSlot slot)
        {
            FixedString64Bytes curId = CurrentEquipped.Value;

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

        // 임시 슬롯 업데이트함수 
        public void UpdateSlot(WeaponSlot slot, string itemId, WeaponState state)
        {
            if (!IsServer) return;

            Debug.Log($"[EquipmentSlots] UpdateSlot: owner={OwnerClientId}, slot={slot}, itemID={itemId}, ammo={state.currentAmmo}", this);

            switch (slot)
            {
                case WeaponSlot.Primary1:
                    Primary1Id.Value = itemId;
                    Primary1State.Value = state;
                    break;
                case WeaponSlot.Primary2:
                    Primary2Id.Value = itemId;
                    Primary2State.Value = state;
                    break;
                case WeaponSlot.Secondary:
                    SecondaryId.Value = itemId;
                    SecondaryState.Value = state;
                    break;
                case WeaponSlot.Melee:
                    MeleeId.Value = itemId;
                    break;
            }
            
            if (CurrentEquipped.Value.Length == 0)
            {
                CurrentEquipped.Value = itemId;
            }
        }

        [ServerRpc(RequireOwnership = false)]
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

        [ServerRpc(RequireOwnership = false)]
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

    public enum WeaponSlot : byte { None, Primary1, Primary2, Secondary, Melee }
}

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    
    public class EquipmentSlots : NetworkBehaviour, IArmored
    {
        // ----------- 무기 런타임 상태 -----------

        public NetworkVariable<WeaponState> Primary1State  = new();
        public NetworkVariable<WeaponState> Primary2State  = new();
        public NetworkVariable<WeaponState> SecondaryState = new();

        // ----------- 슬롯 ID -----------

        public NetworkVariable<FixedString64Bytes> HeadSlotId      = new("");
        public NetworkVariable<FixedString64Bytes> TorsoSlotId     = new("");
        public NetworkVariable<FixedString64Bytes> Primary1Id      = new("");
        public NetworkVariable<FixedString64Bytes> Primary2Id      = new("");
        public NetworkVariable<FixedString64Bytes> SecondaryId     = new("");
        public NetworkVariable<FixedString64Bytes> MeleeId         = new("");
        public NetworkVariable<FixedString64Bytes> CurrentEquipped = new("");

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

            FixedString64Bytes curId = CurrentEquipped.Value;

            if (curId == Primary1Id.Value)
                Primary1State.Value = DecreaseAmmo(Primary1State.Value);
            else if (curId == Primary2Id.Value)
                Primary2State.Value = DecreaseAmmo(Primary2State.Value);
            else if (curId == SecondaryId.Value)
                SecondaryState.Value = DecreaseAmmo(SecondaryState.Value);
        }

        private WeaponState DecreaseAmmo(WeaponState state)
        {
            if (state.currentAmmo > 0) state.currentAmmo--;
            return state;
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
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어 장비 (헬멧 + 상체 아머 + 4개 무기 + 6개 퀵슬롯).
    /// v1.1 §4.4 — 배낭 슬롯 제거.
    /// IArmored를 구현하여 DamageSystem이 구체 EquipmentSlots 타입에 결합되지 않고
    /// 방어구를 질의할 수 있게 한다.
    /// </summary>
    public class EquipmentSlots : NetworkBehaviour, IArmored
    {
        [Header("SO Database (look up by ID)")]
        [SerializeField] private ItemDataSO[] itemDatabase;

        // 총알을 포함한 장착 무기 슬롯 정보
        public NetworkVariable<WeaponState> Primary1State = new();
        public NetworkVariable<WeaponState> Primary2State = new();
        public NetworkVariable<WeaponState> SecondaryState = new();
        
        public NetworkVariable<FixedString64Bytes> HeadSlotId      = new("");
        public NetworkVariable<FixedString64Bytes> TorsoSlotId     = new("");
        public NetworkVariable<FixedString64Bytes> Primary1Id      = new("");
        public NetworkVariable<FixedString64Bytes> Primary2Id      = new("");
        public NetworkVariable<FixedString64Bytes> SecondaryId     = new("");
        public NetworkVariable<FixedString64Bytes> MeleeId         = new("");
        public NetworkVariable<FixedString64Bytes> CurrentEquipped = new("");

        public NetworkVariable<float> HelmetDurability = new(0f);
        public NetworkVariable<float> ArmorDurability  = new(0f);

        private Dictionary<string, ItemDataSO> dbCache;

        private void Awake()
        {
            dbCache = new Dictionary<string, ItemDataSO>();
            if (itemDatabase != null)
            {
                foreach (var so in itemDatabase)
                {
                    if (so == null || string.IsNullOrEmpty(so.itemID)) continue;
                    dbCache[so.itemID] = so;
                }
            }
        }

        public ItemDataSO Lookup(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            return dbCache.TryGetValue(itemId, out var so) ? so : null;
        }

        public WeaponDataSO GetCurrentWeapon() => Lookup(CurrentEquipped.Value.ToString()) as WeaponDataSO;
        /// <summary>
        /// 현재 손에 들고 있는 무기의 런타임 상태(잔탄/탄종)를 반환합니다.
        /// </summary>
        public WeaponState CurrentWeaponState
        {
            get
            {
                FixedString64Bytes curId = CurrentEquipped.Value;
                if (curId == Primary1Id.Value) return Primary1State.Value;
                if (curId == Primary2Id.Value) return Primary2State.Value;
                if (curId == SecondaryId.Value) return SecondaryState.Value;
                return default;
            }
        }
        /// <summary>
        /// 현재 무기의 정적 데이터(SO)를 반환합니다.
        /// </summary>
        public WeaponDataSO CurrentWeaponData => GetCurrentWeapon();

        /// <summary>
        /// 현재 무기에 장전된 탄약의 정적 데이터(SO)를 반환합니다.
        /// </summary>
        public AmmoDataSO CurrentAmmoData => 
            Lookup(CurrentWeaponState.loadedAmmoId.ToString()) as AmmoDataSO;

        public HelmetDataSO GetEquippedHelmet() => Lookup(HeadSlotId.Value.ToString()) as HelmetDataSO;
        public ArmorDataSO GetEquippedArmor() => Lookup(TorsoSlotId.Value.ToString()) as ArmorDataSO;
        public float GetHelmetDurability() => HelmetDurability.Value;
        public float GetArmorDurability() => ArmorDurability.Value;

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

        [ServerRpc]
        public void EquipHelmetServerRpc(FixedString64Bytes helmetId)
        {
            HeadSlotId.Value = helmetId;
            var h = Lookup(helmetId.ToString()) as HelmetDataSO;
            HelmetDurability.Value = h != null ? h.maxDurability : 0f;
        }

        [ServerRpc]
        public void EquipArmorServerRpc(FixedString64Bytes armorId)
        {
            TorsoSlotId.Value = armorId;
            var a = Lookup(armorId.ToString()) as ArmorDataSO;
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

using Unity.Collections;
using Unity.Netcode;

using DeadZone.Actors.UI;
using DeadZone.Core;

namespace DeadZone.Actors
{
    public class WeaponSwitching : NetworkBehaviour
    {
        private EquipmentSlots equipment;

        private void Awake()
        {
            equipment = GetComponent<EquipmentSlots>();
        }

        public void RequestEquip(WeaponSlot slot)
        {
            if (!IsOwner || equipment == null)
                return;

            if (!TryGetWeaponData(slot, out WeaponDataSO weaponData))
                return;

            equipment.SwitchToServerRpc(slot);

            HudActionMessageUI.Instance?.ShowMessage(GetWeaponDisplayName(weaponData));
        }

        private bool TryGetWeaponData(WeaponSlot slot, out WeaponDataSO weaponData)
        {
            weaponData = null;

            if (equipment == null)
                return false;

            FixedString64Bytes itemId = slot switch
            {
                WeaponSlot.Secondary => equipment.SecondaryId.Value,
                WeaponSlot.Primary1 => equipment.Primary1Id.Value,
                WeaponSlot.Primary2 => equipment.Primary2Id.Value,
                WeaponSlot.Melee => equipment.MeleeId.Value,
                _ => default
            };

            if (itemId.Length == 0)
                return false;

            weaponData = equipment.Lookup<WeaponDataSO>(itemId.ToString());
            return weaponData != null;
        }

        private static string GetWeaponDisplayName(WeaponDataSO weaponData)
        {
            if (weaponData == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(weaponData.displayName))
                return weaponData.displayName;

            if (!string.IsNullOrWhiteSpace(weaponData.itemID))
                return weaponData.itemID;

            return "무기";
        }
    }
}
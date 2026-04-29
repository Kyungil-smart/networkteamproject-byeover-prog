using Unity.Netcode;

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
            if (!IsOwner || equipment == null) return;
            equipment.SwitchToServerRpc(slot);
        }
    }
}
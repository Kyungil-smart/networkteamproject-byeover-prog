using UnityEngine;
using Unity.Netcode;


namespace DeadZone.Actors
{
    public class ReloadSystem : NetworkBehaviour
    {
        [SerializeField] private float defaultReloadTime = 2.5f;
        private bool isReloading;

        public bool IsReloading => isReloading;

        public void TryReload()
        {
            if (!IsOwner || isReloading) return;
            StartCoroutine(ReloadRoutine());
        }

        private System.Collections.IEnumerator ReloadRoutine()
        {
            isReloading = true;
            yield return new WaitForSeconds(defaultReloadTime);
            isReloading = false;
        }
    }

    public class ADSSystem : NetworkBehaviour
    {
        public bool IsADS { get; private set; }
        public void SetADS(bool aiming) { if (IsOwner) IsADS = aiming; }
    }

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

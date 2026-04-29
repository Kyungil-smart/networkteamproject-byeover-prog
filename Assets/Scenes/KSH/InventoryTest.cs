using Unity.Netcode;
using UnityEngine;
using DeadZone.Actors;
using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 인벤토리 없이 EquipmentSlots에 아이템을 직접 주입하는 테스트 도구입니다.
    /// </summary>
    public class EquipmentDebugTester : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private EquipmentSlots targetEquipment;

        [Header("Test Item")]
        [SerializeField] private ItemDataSO itemToEquip;
        [SerializeField] private WeaponSlot targetSlot;
        
        [Header("Initial State (For Weapons)")]
        [SerializeField] private AmmoDataSO defaultAmmo;
        [SerializeField] private int initialAmmo = 30;


        void Awake()
        {
            if (targetEquipment == null) targetEquipment = GetComponent<EquipmentSlots>();
        }
        /// <summary>
        /// 인스펙터의 컴포넌트 메뉴에서 'Equip Item'을 클릭하거나 
        /// 아래 버튼용 함수를 통해 실행합니다.
        /// </summary>
        [ContextMenu("Equip Item")]
        public void EquipTestItem()
        {
            if (targetEquipment == null || itemToEquip == null)
            {
                Debug.LogError("대상 EquipmentSlots 또는 아이템이 비어있습니다.");
                return;
            }

            // 서버 권위 변수를 수정해야 하므로 서버인지 확인
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("디버그 주입은 서버(Host)에서만 가능합니다.");
                return;
            }

            if (itemToEquip is WeaponDataSO weapon)
            {
                InjectWeapon(weapon);
            }
            else if (itemToEquip is HelmetDataSO helmet)
            {
                targetEquipment.EquipHelmetServerRpc(helmet.itemID);
            }
            else if (itemToEquip is ArmorDataSO armor)
            {
                targetEquipment.EquipArmorServerRpc(armor.itemID);
            }
        }

        private void InjectWeapon(WeaponDataSO weapon)
        {
            // 1. WeaponState 생성
            WeaponState newState = new WeaponState
            {
                currentAmmo = initialAmmo,
                loadedAmmoId = defaultAmmo != null ? defaultAmmo.itemID : ""
            };
            targetEquipment.UpdateSlot(targetSlot, weapon.itemID, newState);
            
            Debug.Log($"[Debug] {targetSlot} 슬롯에 {weapon.itemID} 주입 완료.");
        }
    }
}
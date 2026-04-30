using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 인벤토리에서 선택한 아이템을 기존 EquipmentSlots에 반영하는 연결용 스크립트입니다.
    /// 기존 EquipmentSlots.cs / GridInventory.cs는 수정하지 않습니다.
    /// UI 버튼에서 이 스크립트의 EquipItemFromInventoryServerRpc를 호출하면 장비 슬롯 정보가 최신화됩니다.
    /// </summary>
    public class KHWEquipmentSlotsBridge : NetworkBehaviour
    {
        [Header("아이템 데이터베이스")]
        [Tooltip("총기/탄약/헬멧/방어구를 itemID로 찾는 Pool SO")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("기존 컴포넌트 연결")]
        [Tooltip("비워두면 같은 PlayerPrefab 안에서 자동으로 찾습니다.")]
        [SerializeField] private EquipmentSlots equipmentSlots;

        [Tooltip("비워두면 같은 PlayerPrefab 안에서 IInventory 구현 컴포넌트를 자동으로 찾습니다.")]
        [SerializeField] private MonoBehaviour inventoryComponent;

        [Header("장착 규칙")]
        [Tooltip("체크하면 장착 성공 시 인벤토리에서 해당 아이템 1개를 제거합니다.")]
        [SerializeField] private bool consumeFromInventoryOnEquip = false;

        [Tooltip("무기 장착 후 바로 손에 들 무기로 변경합니다.")]
        [SerializeField] private bool autoSwitchToEquippedWeapon = true;

        [Tooltip("무기 장착 시 기본 잔탄을 무기 탄창 크기로 채웁니다. 실제 장전 시스템을 붙이면 false 권장.")]
        [SerializeField] private bool fillAmmoToMagazineOnEquip = false;

        private IInventory Inventory
        {
            get
            {
                return inventoryComponent as IInventory;
            }
        }

        private void Awake()
        {
            if (equipmentSlots == null)
            {
                equipmentSlots = GetComponent<EquipmentSlots>();
            }

            if (inventoryComponent == null)
            {
                inventoryComponent = GetComponent<MonoBehaviour>();
                MonoBehaviour[] components = GetComponents<MonoBehaviour>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is IInventory)
                    {
                        inventoryComponent = components[i];
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// UI에서 호출할 통합 장착 함수입니다.
        /// 코드 역할: itemID를 보고 Weapon/Helmet/Armor인지 판별한 뒤 EquipmentSlots의 NetworkVariable을 최신화합니다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void EquipItemFromInventoryServerRpc(FixedString64Bytes itemId, WeaponSlot weaponSlot, ServerRpcParams rpcParams = default)
        {
            if (!IsOwnerRequest(rpcParams)) return;
            if (equipmentSlots == null || scriptObjectPool == null) return;

            string id = itemId.ToString();
            ItemDataSO item = scriptObjectPool.Lookup(id);
            if (item == null)
            {
                Debug.LogWarning("[KHWEquipmentSlotsBridge] Pool에서 아이템을 찾지 못했습니다: " + id);
                return;
            }

            IInventory inventory = Inventory;
            if (inventory != null && !inventory.HasItem(id, 1))
            {
                Debug.Log("[KHWEquipmentSlotsBridge] 인벤토리에 장착할 아이템이 없습니다: " + id);
                return;
            }

            if (item is WeaponDataSO)
            {
                EquipWeapon(item as WeaponDataSO, weaponSlot);
            }
            else if (item is HelmetDataSO)
            {
                EquipHelmet(item as HelmetDataSO);
            }
            else if (item is ArmorDataSO)
            {
                EquipArmor(item as ArmorDataSO);
            }
            else
            {
                Debug.Log("[KHWEquipmentSlotsBridge] 장착 가능한 아이템 타입이 아닙니다: " + id);
                return;
            }

            if (consumeFromInventoryOnEquip && inventory != null)
            {
                inventory.ConsumeItem(id, 1);
            }
        }

        private bool IsOwnerRequest(ServerRpcParams rpcParams)
        {
            if (NetworkObject == null) return false;
            return rpcParams.Receive.SenderClientId == OwnerClientId;
        }

        private void EquipHelmet(HelmetDataSO helmet)
        {
            if (helmet == null) return;

            equipmentSlots.HeadSlotId.Value = new FixedString64Bytes(helmet.itemID);
            equipmentSlots.HelmetDurability.Value = helmet.maxDurability;
        }

        private void EquipArmor(ArmorDataSO armor)
        {
            if (armor == null) return;

            equipmentSlots.TorsoSlotId.Value = new FixedString64Bytes(armor.itemID);
            equipmentSlots.ArmorDurability.Value = armor.maxDurability;
        }

        private void EquipWeapon(WeaponDataSO weapon, WeaponSlot slot)
        {
            if (weapon == null) return;

            FixedString64Bytes weaponId = new FixedString64Bytes(weapon.itemID);

            if (slot == WeaponSlot.Primary1)
            {
                equipmentSlots.Primary1Id.Value = weaponId;
                UpdateWeaponRuntimeStateByReflection("Primary1State", weapon, "");
            }
            else if (slot == WeaponSlot.Primary2)
            {
                equipmentSlots.Primary2Id.Value = weaponId;
                UpdateWeaponRuntimeStateByReflection("Primary2State", weapon, "");
            }
            else if (slot == WeaponSlot.Secondary)
            {
                equipmentSlots.SecondaryId.Value = weaponId;
                UpdateWeaponRuntimeStateByReflection("SecondaryState", weapon, "");
            }
            else if (slot == WeaponSlot.Melee)
            {
                equipmentSlots.MeleeId.Value = weaponId;
            }
            else
            {
                Debug.LogWarning("[KHWEquipmentSlotsBridge] 무기 슬롯이 None입니다. Primary1/Primary2/Secondary/Melee 중 하나를 선택하세요.");
                return;
            }

            if (autoSwitchToEquippedWeapon)
            {
                equipmentSlots.CurrentEquipped.Value = weaponId;
            }
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// WeaponState 타입을 직접 수정하지 않고도 EquipmentSlots의 Primary1State/Primary2State/SecondaryState를 갱신합니다.
        /// 코드 역할: 기존 WeaponState 구조체 위치가 프로젝트마다 달라도, 필드 이름이 currentAmmo / loadedAmmoId이면 반영합니다.
        /// </summary>
        private void UpdateWeaponRuntimeStateByReflection(string fieldName, WeaponDataSO weapon, string loadedAmmoId)
        {
            FieldInfo fieldInfo = typeof(EquipmentSlots).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (fieldInfo == null) return;

            object networkVariable = fieldInfo.GetValue(equipmentSlots);
            if (networkVariable == null) return;

            PropertyInfo valueProperty = networkVariable.GetType().GetProperty("Value");
            if (valueProperty == null) return;

            object state = valueProperty.GetValue(networkVariable, null);
            if (state == null)
            {
                state = System.Activator.CreateInstance(valueProperty.PropertyType);
            }

            FieldInfo ammoField = state.GetType().GetField("currentAmmo");
            if (ammoField != null)
            {
                int ammoValue = fillAmmoToMagazineOnEquip ? weapon.magSize : 0;
                SetNumberField(ammoField, state, ammoValue);
            }

            FieldInfo loadedAmmoField = state.GetType().GetField("loadedAmmoId");
            if (loadedAmmoField != null)
            {
                loadedAmmoField.SetValue(state, new FixedString64Bytes(loadedAmmoId));
            }

            valueProperty.SetValue(networkVariable, state, null);
        }

        private void SetNumberField(FieldInfo fieldInfo, object target, int value)
        {
            if (fieldInfo.FieldType == typeof(int))
            {
                fieldInfo.SetValue(target, value);
            }
            else if (fieldInfo.FieldType == typeof(ushort))
            {
                fieldInfo.SetValue(target, (ushort)Mathf.Clamp(value, 0, ushort.MaxValue));
            }
            else if (fieldInfo.FieldType == typeof(byte))
            {
                fieldInfo.SetValue(target, (byte)Mathf.Clamp(value, 0, byte.MaxValue));
            }
        }
    }
}

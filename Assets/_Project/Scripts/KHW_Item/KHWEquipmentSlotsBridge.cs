// ============================================================================
// KHWEquipmentSlotsBridge.cs
// 목적: 기존 EquipmentSlots.cs를 수정하지 않고, KHW 아이템 장착 요청을 EquipmentSlots의
//       NetworkVariable 값으로 반영하는 브릿지입니다.
// 패턴: Bridge Adapter + ServerRpc + Reflection 호환 처리.
// 적용: PlayerPrefab Root에 붙입니다.
// ============================================================================
using DeadZone.Actors;
using DeadZone.Core;
using System;
using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [KHW 추가 스크립트]
/// 기존 EquipmentSlots.cs를 수정하지 않고, 인벤토리/테스트 UI에서 장착 요청이 들어왔을 때
/// EquipmentSlots의 NetworkVariable 값을 최신화하는 브릿지입니다.
/// </summary>
public class KHWEquipmentSlotsBridge : NetworkBehaviour
{
    [Header("연결 대상")]
    [Tooltip("같은 PlayerPrefab에 있는 기존 EquipmentSlots 컴포넌트입니다.")]
    [SerializeField] private EquipmentSlots equipmentSlots;

    [Tooltip("itemID로 무기/탄약/헬멧/방어구 SO를 찾는 KHW 데이터베이스입니다.")]
    [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

    [Header("자동 찾기")]
    [Tooltip("체크하면 Awake에서 같은 오브젝트의 EquipmentSlots를 자동으로 찾습니다.")]
    [SerializeField] private bool autoFindOnAwake = true;

    [Header("무기 기본 장전값")]
    [Tooltip("무기 슬롯 장착 테스트 시 기본으로 장전할 탄약 ID입니다. 비워도 됩니다.")]
    [SerializeField] private string defaultLoadedAmmoId = "";

    [Tooltip("무기 슬롯 장착 테스트 시 기본 잔탄 수입니다.")]
    [SerializeField] private ushort defaultCurrentAmmo = 0;

    private void Awake()
    {
        if (autoFindOnAwake && equipmentSlots == null)
        {
            equipmentSlots = GetComponent<EquipmentSlots>();
        }
    }

    public void EquipByItemIdFromButton(string itemId)
    {
        // [KHW 추가 기능]
        // UI Button OnClick에서 문자열 itemID를 넣어 호출할 수 있는 테스트용 함수입니다.
        EquipItemServerRpc(new FixedString64Bytes(itemId), WeaponSlot.Primary1, new FixedString64Bytes(defaultLoadedAmmoId), defaultCurrentAmmo);
    }

    public void EquipWeaponToPrimary1(string itemId)
    {
        EquipItemServerRpc(new FixedString64Bytes(itemId), WeaponSlot.Primary1, new FixedString64Bytes(defaultLoadedAmmoId), defaultCurrentAmmo);
    }

    public void EquipWeaponToPrimary2(string itemId)
    {
        EquipItemServerRpc(new FixedString64Bytes(itemId), WeaponSlot.Primary2, new FixedString64Bytes(defaultLoadedAmmoId), defaultCurrentAmmo);
    }

    public void EquipWeaponToSecondary(string itemId)
    {
        EquipItemServerRpc(new FixedString64Bytes(itemId), WeaponSlot.Secondary, new FixedString64Bytes(defaultLoadedAmmoId), defaultCurrentAmmo);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EquipItemServerRpc(FixedString64Bytes itemId, WeaponSlot weaponSlot, FixedString64Bytes loadedAmmoId, ushort currentAmmo)
    {
        // [KHW 추가 기능]
        // 서버에서만 EquipmentSlots NetworkVariable을 갱신합니다.
        // 기존 EquipmentSlots.cs는 수정하지 않고 public NetworkVariable 값만 변경합니다.
        if (!IsServer) return;
        if (equipmentSlots == null) return;
        if (scriptObjectPool == null) return;

        string id = itemId.ToString();
        ItemDataSO item = scriptObjectPool.Lookup(id);
        if (item == null) return;

        WeaponDataSO weapon = item as WeaponDataSO;
        if (weapon != null)
        {
            EquipWeaponInternal(itemId, weaponSlot, loadedAmmoId, currentAmmo);
            return;
        }

        HelmetDataSO helmet = item as HelmetDataSO;
        if (helmet != null)
        {
            equipmentSlots.HeadSlotId.Value = itemId;
            equipmentSlots.HelmetDurability.Value = helmet.maxDurability;
            return;
        }

        ArmorDataSO armor = item as ArmorDataSO;
        if (armor != null)
        {
            equipmentSlots.TorsoSlotId.Value = itemId;
            equipmentSlots.ArmorDurability.Value = armor.maxDurability;
            return;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SwitchWeaponSlotServerRpc(WeaponSlot weaponSlot)
    {
        // [KHW 추가 기능]
        // 기존 EquipmentSlots.SwitchToServerRpc를 직접 수정하지 않고 동일한 결과를 NetworkVariable로 갱신합니다.
        if (!IsServer) return;
        if (equipmentSlots == null) return;

        if (weaponSlot == WeaponSlot.Primary1)
        {
            equipmentSlots.CurrentEquipped.Value = equipmentSlots.Primary1Id.Value;
        }
        else if (weaponSlot == WeaponSlot.Primary2)
        {
            equipmentSlots.CurrentEquipped.Value = equipmentSlots.Primary2Id.Value;
        }
        else if (weaponSlot == WeaponSlot.Secondary)
        {
            equipmentSlots.CurrentEquipped.Value = equipmentSlots.SecondaryId.Value;
        }
        else if (weaponSlot == WeaponSlot.Melee)
        {
            equipmentSlots.CurrentEquipped.Value = equipmentSlots.MeleeId.Value;
        }
    }

    private void EquipWeaponInternal(FixedString64Bytes weaponId, WeaponSlot weaponSlot, FixedString64Bytes loadedAmmoId, ushort currentAmmo)
    {
        if (weaponSlot == WeaponSlot.Primary1)
        {
            equipmentSlots.Primary1Id.Value = weaponId;
            TrySetWeaponStateByReflection("Primary1State", loadedAmmoId, currentAmmo);
        }
        else if (weaponSlot == WeaponSlot.Primary2)
        {
            equipmentSlots.Primary2Id.Value = weaponId;
            TrySetWeaponStateByReflection("Primary2State", loadedAmmoId, currentAmmo);
        }
        else if (weaponSlot == WeaponSlot.Secondary)
        {
            equipmentSlots.SecondaryId.Value = weaponId;
            TrySetWeaponStateByReflection("SecondaryState", loadedAmmoId, currentAmmo);
        }
        else if (weaponSlot == WeaponSlot.Melee)
        {
            equipmentSlots.MeleeId.Value = weaponId;
        }

        if (string.IsNullOrEmpty(equipmentSlots.CurrentEquipped.Value.ToString()))
        {
            equipmentSlots.CurrentEquipped.Value = weaponId;
        }
    }

    private void TrySetWeaponStateByReflection(string fieldName, FixedString64Bytes loadedAmmoId, ushort currentAmmo)
    {
        // [KHW 추가 기능]
        // 사용자가 제공한 EquipmentSlots 최신안에는 Primary1State 같은 WeaponState NetworkVariable이 있습니다.
        // 하지만 현재 _Project.zip에는 없을 수 있으므로, 컴파일 오류를 피하기 위해 Reflection으로 있을 때만 갱신합니다.
        FieldInfo field = equipmentSlots.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return;

        object networkVariable = field.GetValue(equipmentSlots);
        if (networkVariable == null) return;

        Type networkVariableType = networkVariable.GetType();
        Type[] genericArgs = networkVariableType.GetGenericArguments();
        if (genericArgs == null || genericArgs.Length == 0) return;

        Type weaponStateType = genericArgs[0];
        object weaponState = Activator.CreateInstance(weaponStateType);

        FieldInfo currentAmmoField = weaponStateType.GetField("currentAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (currentAmmoField != null)
        {
            SetNumericField(currentAmmoField, weaponState, currentAmmo);
        }

        FieldInfo loadedAmmoField = weaponStateType.GetField("loadedAmmoId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (loadedAmmoField != null)
        {
            loadedAmmoField.SetValue(weaponState, loadedAmmoId);
        }

        PropertyInfo valueProperty = networkVariableType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty != null)
        {
            valueProperty.SetValue(networkVariable, weaponState, null);
        }
    }

    private void SetNumericField(FieldInfo field, object target, ushort value)
    {
        Type fieldType = field.FieldType;

        if (fieldType == typeof(byte))
        {
            field.SetValue(target, (byte)Mathf.Clamp(value, 0, byte.MaxValue));
        }
        else if (fieldType == typeof(ushort))
        {
            field.SetValue(target, value);
        }
        else if (fieldType == typeof(int))
        {
            field.SetValue(target, (int)value);
        }
        else if (fieldType == typeof(uint))
        {
            field.SetValue(target, (uint)value);
        }
    }
}

using Sirenix.OdinInspector;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

using OdinReadOnly = Sirenix.OdinInspector.ReadOnlyAttribute;

namespace DeadZone.Actors
{
    /// <summary>
    /// 로컬 플레이어가 장착한 무기의 탄약 상태를 표시한다.
    /// 실제 탄약 수량은 EquipmentSlots의 NetworkVariable 상태를 기준으로 읽는다.
    /// </summary>
    public class WeaponStatsUI : MonoBehaviour
    {
        [BoxGroup("참조")]
        [SerializeField] private Image weaponIcon;

        [BoxGroup("참조")]
        [SerializeField] private TMP_Text weaponNameText;

        [BoxGroup("참조")]
        [SerializeField] private TMP_Text ammoText;

        [BoxGroup("참조")]
        [SerializeField] private TMP_Text durabilityText;

        [BoxGroup("참조")]
        [SerializeField] private Image durabilityFill;

        [BoxGroup("설정")]
        [SerializeField, Tooltip("비어 있으면 NetworkManager의 LocalClient PlayerObject에서 EquipmentSlots를 자동으로 찾습니다.")]
        private EquipmentSlots equipmentSlots;

        [BoxGroup("설정")]
        [SerializeField, Tooltip("로컬 PlayerObject가 늦게 생성되는 씬 전환 흐름을 위해 바인딩을 자동 재시도합니다.")]
        private bool autoBindLocalEquipment = true;

        [BoxGroup("설정")]
        [SerializeField, Tooltip("내구도 런타임 값이 연결되지 않은 상태에서 무기 SO의 최대 내구도만 표시할지 여부입니다.")]
        private bool showWeaponMaxDurabilityWhenRuntimeMissing;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private string currentWeaponId;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private string currentLoadedAmmoId;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private int currentAmmo;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private int maxAmmo;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private float currentDurability;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private float maxDurability;

        [TitleGroup("디버그")]
        [ShowInInspector, OdinReadOnly] private bool hasRuntimeDurability;

        private Sprite currentWeaponIcon;
        private bool isSubscribedToEquipment;

        private void Awake()
        {
            ClearWeaponDisplay();
            RefreshUI();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            EventBus.Subscribe<WeaponAmmoChangedEvent>(OnWeaponAmmoChanged);
            EventBus.Subscribe<ReloadCompletedEvent>(OnReloadCompleted);
            EventBus.Subscribe<ReloadCancelledEvent>(OnReloadCancelled);

            if (equipmentSlots != null)
            {
                BindEquipmentSlots(equipmentSlots);
            }
            else
            {
                TryBindLocalEquipment();
            }

            RefreshFromEquipmentSlots();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
            EventBus.Unsubscribe<WeaponAmmoChangedEvent>(OnWeaponAmmoChanged);
            EventBus.Unsubscribe<ReloadCompletedEvent>(OnReloadCompleted);
            EventBus.Unsubscribe<ReloadCancelledEvent>(OnReloadCancelled);

            UnbindEquipmentSlots();
        }

        private void Update()
        {
            if (autoBindLocalEquipment && equipmentSlots == null)
            {
                TryBindLocalEquipment();
            }
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton == null
                || clientId == NetworkManager.Singleton.LocalClientId;
        }

        private bool TryBindLocalEquipment()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
                return false;

            NetworkObject playerObject = networkManager.LocalClient?.PlayerObject;

            if (playerObject == null &&
                networkManager.ConnectedClients.TryGetValue(networkManager.LocalClientId, out NetworkClient localClient))
            {
                playerObject = localClient.PlayerObject;
            }

            if (playerObject == null)
                return false;

            EquipmentSlots found = playerObject.GetComponent<EquipmentSlots>();
            if (found == null)
                found = playerObject.GetComponentInChildren<EquipmentSlots>(true);

            if (found == null)
                return false;

            BindEquipmentSlots(found);
            return true;
        }

        private void BindEquipmentSlots(EquipmentSlots slots)
        {
            if (slots == null)
                return;

            if (equipmentSlots == slots && isSubscribedToEquipment)
            {
                RefreshFromEquipmentSlots();
                return;
            }

            UnbindEquipmentSlots();

            equipmentSlots = slots;
            equipmentSlots.CurrentEquipped.OnValueChanged += OnCurrentEquippedChanged;
            equipmentSlots.Primary1Id.OnValueChanged += OnEquippedSlotIdChanged;
            equipmentSlots.Primary2Id.OnValueChanged += OnEquippedSlotIdChanged;
            equipmentSlots.SecondaryId.OnValueChanged += OnEquippedSlotIdChanged;
            equipmentSlots.Primary1State.OnValueChanged += OnWeaponStateChanged;
            equipmentSlots.Primary2State.OnValueChanged += OnWeaponStateChanged;
            equipmentSlots.SecondaryState.OnValueChanged += OnWeaponStateChanged;
            isSubscribedToEquipment = true;

            RefreshFromEquipmentSlots();
        }

        private void UnbindEquipmentSlots()
        {
            if (equipmentSlots == null || !isSubscribedToEquipment)
                return;

            equipmentSlots.CurrentEquipped.OnValueChanged -= OnCurrentEquippedChanged;
            equipmentSlots.Primary1Id.OnValueChanged -= OnEquippedSlotIdChanged;
            equipmentSlots.Primary2Id.OnValueChanged -= OnEquippedSlotIdChanged;
            equipmentSlots.SecondaryId.OnValueChanged -= OnEquippedSlotIdChanged;
            equipmentSlots.Primary1State.OnValueChanged -= OnWeaponStateChanged;
            equipmentSlots.Primary2State.OnValueChanged -= OnWeaponStateChanged;
            equipmentSlots.SecondaryState.OnValueChanged -= OnWeaponStateChanged;
            isSubscribedToEquipment = false;
        }

        private void OnCurrentEquippedChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshFromEquipmentSlots();
        }

        private void OnEquippedSlotIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshFromEquipmentSlots();
        }

        private void OnWeaponStateChanged(WeaponState previousValue, WeaponState newValue)
        {
            RefreshFromEquipmentSlots();
        }

        private void OnWeaponAmmoChanged(WeaponAmmoChangedEvent e)
        {
            if (!IsLocalClient(e.clientId))
                return;

            ApplyAmmoStateFromEvent(e.weaponId.ToString(), e.afterAmmoId.ToString(), e.afterAmmo, e.maxAmmo);
        }

        private void OnReloadCompleted(ReloadCompletedEvent e)
        {
            if (!IsLocalClient(e.clientId))
                return;

            ApplyAmmoStateFromEvent(e.weaponId.ToString(), e.ammoId.ToString(), e.currentAmmo, e.maxAmmo);
        }

        private void OnReloadCancelled(ReloadCancelledEvent e)
        {
            if (!IsLocalClient(e.clientId))
                return;

            RefreshFromEquipmentSlots();
        }

        private void OnWeaponFired(WeaponFiredEvent e)
        {
            if (!IsLocalClient(e.shooterClientId))
                return;

            // 발사 이벤트는 사운드/피드백용 정보가 중심이다.
            // 실제 탄약 수량은 서버가 갱신한 EquipmentSlots 상태를 기준으로 다시 읽는다.
            RefreshFromEquipmentSlots();
        }

        public void SetWeapon(WeaponDataSO weaponData, string fallbackWeaponName, int ammo, float durability)
        {
            string weaponId = weaponData != null && !string.IsNullOrEmpty(weaponData.itemID)
                ? weaponData.itemID
                : fallbackWeaponName;

            int resolvedMaxAmmo = weaponData != null
                ? Mathf.Max(0, weaponData.magSize)
                : Mathf.Max(0, ammo);

            ApplyWeaponDisplay(weaponData, weaponId, currentLoadedAmmoId, currentAmmo, resolvedMaxAmmo);

            if (durability > 0f)
            {
                hasRuntimeDurability = true;
                maxDurability = durability;
                currentDurability = durability;
            }
            else
            {
                ClearDurabilityDisplay(weaponData);
            }

            RefreshUI();
        }

        private void RefreshFromEquipmentSlots()
        {
            if (equipmentSlots == null)
            {
                ClearWeaponDisplay();
                RefreshUI();
                return;
            }

            FixedString64Bytes equippedId = equipmentSlots.CurrentEquipped.Value;
            string equippedIdText = equippedId.ToString();

            if (string.IsNullOrWhiteSpace(equippedIdText))
            {
                ClearWeaponDisplay();
                RefreshUI();
                return;
            }

            WeaponDataSO weaponData = equipmentSlots.CurrentWeaponData;
            WeaponState state = equipmentSlots.CurrentWeaponState;
            int resolvedMaxAmmo = weaponData != null ? Mathf.Max(0, weaponData.magSize) : 0;

            ApplyWeaponDisplay(
                weaponData,
                equippedIdText,
                state.loadedAmmoId.ToString(),
                state.currentAmmo,
                resolvedMaxAmmo);

            ClearDurabilityDisplay(weaponData);
            RefreshUI();
        }

        private void ApplyAmmoStateFromEvent(string weaponId, string loadedAmmoId, int ammo, int max)
        {
            if (equipmentSlots != null)
            {
                string equippedId = equipmentSlots.CurrentEquipped.Value.ToString();
                if (!string.IsNullOrWhiteSpace(equippedId) && equippedId != weaponId)
                    return;
            }

            currentWeaponId = weaponId;
            currentLoadedAmmoId = loadedAmmoId;
            currentAmmo = Mathf.Max(0, ammo);
            maxAmmo = Mathf.Max(0, max);

            RefreshUI();
        }

        private void ApplyWeaponDisplay(
            WeaponDataSO weaponData,
            string fallbackWeaponId,
            string loadedAmmoId,
            int ammo,
            int max)
        {
            currentWeaponId = weaponData != null && !string.IsNullOrEmpty(weaponData.itemID)
                ? weaponData.itemID
                : fallbackWeaponId;

            currentLoadedAmmoId = loadedAmmoId ?? string.Empty;
            currentWeaponIcon = weaponData != null ? weaponData.icon : null;
            currentAmmo = Mathf.Max(0, ammo);
            maxAmmo = Mathf.Max(0, max);

            string displayName = weaponData != null && !string.IsNullOrEmpty(weaponData.displayName)
                ? weaponData.displayName
                : fallbackWeaponId;

            if (weaponNameText != null)
                weaponNameText.text = string.IsNullOrWhiteSpace(displayName) ? "-" : displayName;
        }

        private void ClearWeaponDisplay()
        {
            currentWeaponId = string.Empty;
            currentLoadedAmmoId = string.Empty;
            currentWeaponIcon = null;
            currentAmmo = 0;
            maxAmmo = 0;
            hasRuntimeDurability = false;
            currentDurability = 0f;
            maxDurability = 0f;

            if (weaponNameText != null)
                weaponNameText.text = "-";
        }

        private void ClearDurabilityDisplay(WeaponDataSO weaponData)
        {
            hasRuntimeDurability = false;

            if (showWeaponMaxDurabilityWhenRuntimeMissing && weaponData != null)
            {
                maxDurability = Mathf.Max(0f, weaponData.maxDurability);
                currentDurability = maxDurability;
                return;
            }

            maxDurability = 0f;
            currentDurability = 0f;
        }

        private void RefreshUI()
        {
            if (weaponIcon != null)
            {
                weaponIcon.sprite = currentWeaponIcon;
                weaponIcon.enabled = weaponIcon.sprite != null;
            }

            if (ammoText != null)
            {
                ammoText.text = maxAmmo > 0
                    ? $"{currentAmmo} / {maxAmmo}"
                    : $"{currentAmmo} / -";
            }

            if (durabilityText != null)
            {
                durabilityText.text = hasRuntimeDurability || showWeaponMaxDurabilityWhenRuntimeMissing
                    ? Mathf.CeilToInt(currentDurability).ToString()
                    : "-";
            }

            if (durabilityFill != null)
            {
                durabilityFill.fillAmount = hasRuntimeDurability && maxDurability > 0f
                    ? Mathf.Clamp01(currentDurability / maxDurability)
                    : 0f;
            }
        }

#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("표시 상태 새로고침")]
        private void TestRefreshFromEquipment()
        {
            RefreshFromEquipmentSlots();
        }
#endif
    }
}

using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 현재 무기의 탄약, 내구도, 아이콘을 표시한다.
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
        [MinValue(1), SerializeField] private int maxAmmo = 30;

        [BoxGroup("설정")]
        [MinValue(0f), SerializeField] private float maxDurability = 100f;

        [BoxGroup("설정")]
        [MinValue(0f), SerializeField] private float durabilityCostPerShot = 1f;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private int currentAmmo;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private float currentDurability;

        private string currentWeaponId;
        private Sprite currentWeaponIcon;
        private int lastProcessedFireFrame = -1;

        private void Awake()
        {
            currentAmmo = maxAmmo;
            currentDurability = maxDurability;
            RefreshUI();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            RefreshUI();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton == null
                || clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void OnWeaponFired(WeaponFiredEvent e)
        {
            Debug.Log(
                $"[WeaponStatsUI] Fired / shooter:{e.shooterClientId}, local:{NetworkManager.Singleton?.LocalClientId}, " +
                $"weaponId:{e.weaponId}, weaponData:{e.weaponData}, cost:{durabilityCostPerShot}",
                this
            );

            if (!IsLocalClient(e.shooterClientId))
            {
                return;
            }

            if (lastProcessedFireFrame == Time.frameCount)
            {
                return;
            }

            lastProcessedFireFrame = Time.frameCount;

            string weaponId = e.weaponData != null && !string.IsNullOrEmpty(e.weaponData.itemID)
                ? e.weaponData.itemID
                : e.weaponId.ToString();

            if (currentWeaponId != weaponId)
                SetWeapon(e.weaponData, weaponId, e.maxAmmo, e.maxDurability);

            currentAmmo = Mathf.Max(0, currentAmmo - 1);
            currentDurability = Mathf.Max(0f, currentDurability - durabilityCostPerShot);

            Debug.Log($"[WeaponStatsUI] Ammo:{currentAmmo}, Durability:{currentDurability}/{maxDurability}", this);

            RefreshUI();
        }

        public void SetWeapon(WeaponDataSO weaponData, string fallbackWeaponName, int ammo, float durability)
        {
            currentWeaponId = weaponData != null ? weaponData.itemID : fallbackWeaponName;
            currentWeaponIcon = weaponData != null ? weaponData.icon : null;

            string displayName = weaponData != null && !string.IsNullOrEmpty(weaponData.displayName)
                ? weaponData.displayName
                : fallbackWeaponName;

            if (weaponNameText != null) weaponNameText.text = displayName;

            maxAmmo = Mathf.Max(1, weaponData != null ? weaponData.magSize : ammo);
            maxDurability = Mathf.Max(0f, weaponData != null ? weaponData.maxDurability : durability);
            currentAmmo = maxAmmo;
            currentDurability = maxDurability;
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (weaponIcon != null)
            {
                weaponIcon.sprite = currentWeaponIcon;
                weaponIcon.enabled = weaponIcon.sprite != null;
            }

            if (ammoText != null) ammoText.text = $"{currentAmmo} / {maxAmmo}";
            if (durabilityText != null) durabilityText.text = Mathf.CeilToInt(currentDurability).ToString();

            if (durabilityFill != null)
            {
                durabilityFill.fillAmount = maxDurability > 0f
                    ? Mathf.Clamp01(currentDurability / maxDurability)
                    : 0f;
            }
        }

#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("무기 화면 초기화")]
        private void TestResetWeapon()
        {
            currentAmmo = maxAmmo;
            currentDurability = maxDurability;
            RefreshUI();
        }

        [TitleGroup("디버그")]
        [Button("발사 테스트")]
        private void TestFire()
        {
            currentAmmo = Mathf.Max(0, currentAmmo - 1);
            currentDurability = Mathf.Max(0f, currentDurability - durabilityCostPerShot);
            RefreshUI();
        }
#endif
    }
}

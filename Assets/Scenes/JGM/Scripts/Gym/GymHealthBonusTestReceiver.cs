using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 헬스장 체력 보너스가 플레이어 최대 체력에 어떻게 적용되는지 확인하는 테스트용 리시버입니다.
    /// 실제 PlayerStats가 완성되면 이 스크립트는 제거하고 PlayerStats 쪽에서 보너스를 적용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class GymHealthBonusTestReceiver : MonoBehaviour
    {
        [Header("헬스장 보너스")]
        [SerializeField]
        [Tooltip("헬스장 레벨에 따른 최대 체력 보너스를 계산하는 컨트롤러입니다.")]
        private GymHealthBonusController healthBonusController;

        [Header("테스트 플레이어 체력")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트용 기본 최대 체력입니다.")]
        private int baseMaxHealth = 100;

        [SerializeField]
        [Tooltip("최대 체력이 변경될 때 현재 체력을 최대 체력으로 채울지 여부입니다.")]
        private bool fillHealthWhenMaxHealthChanged = true;

        [Header("적용 결과 확인")]
        [SerializeField]
        [Tooltip("현재 헬스장 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentGymLevel = 1;

        [SerializeField]
        [Tooltip("헬스장 레벨로 적용된 최대 체력 보너스입니다. 런타임 확인용 값입니다.")]
        private int currentHealthBonus;

        [SerializeField]
        [Tooltip("기본 최대 체력과 헬스장 보너스를 더한 최종 최대 체력입니다. 런타임 확인용 값입니다.")]
        private int currentMaxHealth;

        [SerializeField]
        [Tooltip("테스트용 현재 체력입니다. 런타임 확인용 값입니다.")]
        private int currentHealth;

        [Header("로그")]
        [SerializeField]
        [Tooltip("체력 보너스 적용 결과를 Console에 출력할지 여부입니다.")]
        private bool logHealthChanged = true;

        public int BaseMaxHealth => baseMaxHealth;
        public int CurrentGymLevel => currentGymLevel;
        public int CurrentHealthBonus => currentHealthBonus;
        public int CurrentMaxHealth => currentMaxHealth;
        public int CurrentHealth => currentHealth;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            ApplyHealthBonus();
        }

        private void OnEnable()
        {
            SubscribeHealthBonusChanged();
            ApplyHealthBonus();
        }

        private void OnDisable()
        {
            UnsubscribeHealthBonusChanged();
        }

        private void OnValidate()
        {
            if (baseMaxHealth < 1)
                baseMaxHealth = 1;

            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                currentHealthBonus = 0;
                currentMaxHealth = baseMaxHealth;
                currentHealth = baseMaxHealth;
            }
        }

        private void FindRequiredComponents()
        {
            if (healthBonusController == null)
                healthBonusController = GetComponent<GymHealthBonusController>();
        }

        private void SubscribeHealthBonusChanged()
        {
            if (healthBonusController == null)
                return;

            healthBonusController.OnHealthBonusChanged -= HandleHealthBonusChanged;
            healthBonusController.OnHealthBonusChanged += HandleHealthBonusChanged;
        }

        private void UnsubscribeHealthBonusChanged()
        {
            if (healthBonusController == null)
                return;

            healthBonusController.OnHealthBonusChanged -= HandleHealthBonusChanged;
        }

        private void HandleHealthBonusChanged(int gymLevel, int maxHealthBonus)
        {
            ApplyHealthBonus();
        }

        public void ApplyHealthBonus()
        {
            if (healthBonusController == null)
            {
                Debug.LogWarning("[GymHealthBonusTestReceiver] GymHealthBonusController가 연결되어 있지 않습니다.", this);
                return;
            }

            currentGymLevel = healthBonusController.CurrentGymLevel;
            currentHealthBonus = healthBonusController.GetMaxHealthBonus();
            currentMaxHealth = baseMaxHealth + currentHealthBonus;

            if (fillHealthWhenMaxHealthChanged)
                currentHealth = currentMaxHealth;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0, currentMaxHealth);

            if (logHealthChanged)
            {
                Debug.Log(
                    $"[GymHealthBonusTestReceiver] 헬스장 Lv.{currentGymLevel} / 기본 체력 {baseMaxHealth} / 보너스 +{currentHealthBonus} / 최종 최대 체력 {currentMaxHealth} / 현재 체력 {currentHealth}",
                    this
                );
            }
        }

#if UNITY_EDITOR
        [ContextMenu("체력 보너스 다시 적용")]
        private void DebugApplyHealthBonus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GymHealthBonusTestReceiver] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            ApplyHealthBonus();
        }
#endif
    }
}
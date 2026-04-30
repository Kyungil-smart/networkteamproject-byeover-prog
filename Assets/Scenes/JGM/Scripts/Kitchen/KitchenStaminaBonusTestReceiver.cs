using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 주방 스테미너 보너스가 플레이어 최대 스테미너에 어떻게 적용되는지 확인하는 테스트용 리시버입니다.
    /// 실제 PlayerStats가 완성되면 이 스크립트는 제거하고 PlayerStats 쪽에서 보너스를 적용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class KitchenStaminaBonusTestReceiver : MonoBehaviour
    {
        [Header("주방 보너스")]
        [SerializeField]
        [Tooltip("주방 레벨에 따른 최대 스테미너 보너스를 계산하는 컨트롤러입니다.")]
        private KitchenStaminaBonusController staminaBonusController;

        [Header("테스트 플레이어 스테미너")]
        [SerializeField]
        [Min(1)]
        [Tooltip("테스트용 기본 최대 스테미너입니다.")]
        private int baseMaxStamina = 100;

        [SerializeField]
        [Tooltip("최대 스테미너가 변경될 때 현재 스테미너를 최대 스테미너로 채울지 여부입니다.")]
        private bool fillStaminaWhenMaxStaminaChanged = true;

        [Header("적용 결과 확인")]
        [SerializeField]
        [Tooltip("현재 주방 레벨입니다. 런타임 확인용 값입니다.")]
        private int currentKitchenLevel = 1;

        [SerializeField]
        [Tooltip("주방 레벨로 적용된 최대 스테미너 보너스입니다. 런타임 확인용 값입니다.")]
        private int currentStaminaBonus;

        [SerializeField]
        [Tooltip("기본 최대 스테미너와 주방 보너스를 더한 최종 최대 스테미너입니다. 런타임 확인용 값입니다.")]
        private int currentMaxStamina;

        [SerializeField]
        [Tooltip("테스트용 현재 스테미너입니다. 런타임 확인용 값입니다.")]
        private int currentStamina;

        [Header("로그")]
        [SerializeField]
        [Tooltip("스테미너 보너스 적용 결과를 Console에 출력할지 여부입니다.")]
        private bool logStaminaChanged = true;

        public int BaseMaxStamina => baseMaxStamina;
        public int CurrentKitchenLevel => currentKitchenLevel;
        public int CurrentStaminaBonus => currentStaminaBonus;
        public int CurrentMaxStamina => currentMaxStamina;
        public int CurrentStamina => currentStamina;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
            ApplyStaminaBonus();
        }

        private void OnEnable()
        {
            SubscribeStaminaBonusChanged();
            ApplyStaminaBonus();
        }

        private void OnDisable()
        {
            UnsubscribeStaminaBonusChanged();
        }

        private void OnValidate()
        {
            if (baseMaxStamina < 1)
                baseMaxStamina = 1;

            FindRequiredComponents();

            if (!Application.isPlaying)
            {
                currentStaminaBonus = 0;
                currentMaxStamina = baseMaxStamina;
                currentStamina = baseMaxStamina;
            }
        }

        private void FindRequiredComponents()
        {
            if (staminaBonusController == null)
                staminaBonusController = GetComponent<KitchenStaminaBonusController>();
        }

        private void SubscribeStaminaBonusChanged()
        {
            if (staminaBonusController == null)
                return;

            staminaBonusController.OnStaminaBonusChanged -= HandleStaminaBonusChanged;
            staminaBonusController.OnStaminaBonusChanged += HandleStaminaBonusChanged;
        }

        private void UnsubscribeStaminaBonusChanged()
        {
            if (staminaBonusController == null)
                return;

            staminaBonusController.OnStaminaBonusChanged -= HandleStaminaBonusChanged;
        }

        private void HandleStaminaBonusChanged(int kitchenLevel, int maxStaminaBonus)
        {
            ApplyStaminaBonus();
        }

        public void ApplyStaminaBonus()
        {
            if (staminaBonusController == null)
            {
                Debug.LogWarning("[KitchenStaminaBonusTestReceiver] KitchenStaminaBonusController가 연결되어 있지 않습니다.", this);
                return;
            }

            currentKitchenLevel = staminaBonusController.CurrentKitchenLevel;
            currentStaminaBonus = staminaBonusController.GetMaxStaminaBonus();
            currentMaxStamina = baseMaxStamina + currentStaminaBonus;

            if (fillStaminaWhenMaxStaminaChanged)
                currentStamina = currentMaxStamina;
            else
                currentStamina = Mathf.Clamp(currentStamina, 0, currentMaxStamina);

            if (logStaminaChanged)
            {
                Debug.Log(
                    $"[KitchenStaminaBonusTestReceiver] 주방 Lv.{currentKitchenLevel} / 기본 스테미너 {baseMaxStamina} / 보너스 +{currentStaminaBonus} / 최종 최대 스테미너 {currentMaxStamina} / 현재 스테미너 {currentStamina}",
                    this
                );
            }
        }

#if UNITY_EDITOR
        [ContextMenu("스테미너 보너스 다시 적용")]
        private void DebugApplyStaminaBonus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[KitchenStaminaBonusTestReceiver] 플레이 중에만 테스트할 수 있습니다.", this);
                return;
            }

            ApplyStaminaBonus();
        }
#endif
    }
}
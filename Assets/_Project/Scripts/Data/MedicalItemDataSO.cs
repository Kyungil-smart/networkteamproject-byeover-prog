using UnityEngine;

namespace DeadZone.Core
{
    [CreateAssetMenu(
        fileName = "MedicalItem_New",
        menuName = "DeadZone/Items/Medical Item Data",
        order = 6)]
    public class MedicalItemDataSO : ItemDataSO
    {
        [Header("사용")]
        [Tooltip("아이템 사용이 완료되기까지 걸리는 시간입니다.\n현재 의료 효과는 입력 직후 서버에서 적용되며, 이 값은 추후 사용 모션/캐스팅 UI와 연결할 때 사용합니다.")]
        [Min(0f)] public float useSeconds;

        [Header("회복")]
        [Tooltip("사용 즉시 회복되는 체력입니다.\n붕대처럼 바로 체력이 올라가야 하는 아이템은 이 값을 사용합니다.")]
        [Min(0f)] public float instantHeal;

        [Tooltip("지속 회복이 유지되는 시간입니다.\n0이면 지속 회복을 사용하지 않습니다.")]
        [Min(0f)] public float healDurationSeconds;

        [Tooltip("지속 회복 중 1초마다 회복되는 체력입니다.\n예: 5이면 5초 동안 총 25 체력을 회복합니다.")]
        [Min(0f)] public float healPerSecond;

        [Header("임시 효과")]
        [Tooltip("임시 효과가 유지되는 시간입니다.\n무게 증가 주사기처럼 일정 시간 뒤 사라지는 효과에 사용합니다.")]
        [Min(0f)] public float buffDurationSeconds;

        [Tooltip("최대 소지 무게 증가 배율입니다.\n0.5이면 최대 소지 무게가 50% 증가합니다.")]
        [Min(0f)] public float weightCapacityMultiplierBonus;

        [Tooltip("스태미나 소모량 증가/감소 배율입니다.\n0.3이면 스태미나 소모량 관련 임시 효과에 30% 값으로 전달됩니다.")]
        [Min(0f)] public float staminaCostMultiplierBonus;

        public bool HasAnyEffect =>
            instantHeal > 0f ||
            healDurationSeconds > 0f && healPerSecond > 0f ||
            buffDurationSeconds > 0f &&
            (weightCapacityMultiplierBonus > 0f || staminaCostMultiplierBonus > 0f);
    }
}

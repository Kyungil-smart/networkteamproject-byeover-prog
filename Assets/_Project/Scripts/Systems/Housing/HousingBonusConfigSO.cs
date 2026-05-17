using UnityEngine;

namespace DeadZone.Systems.Housing
{
    [CreateAssetMenu(menuName = "DeadZone/Housing/Housing Bonus Config", fileName = "Housing_BonusConfig")]
    public sealed class HousingBonusConfigSO : ScriptableObject
    {
        [Header("보너스 시작 레벨")]
        [SerializeField, Min(1)]
        [Tooltip("하우징 보너스가 적용되기 시작하는 시설 레벨입니다. 예: 2이면 Lv.2부터 보너스가 붙습니다.")]
        private int bonusStartLevel = 2;

        [Header("레벨당 보너스")]
        [SerializeField, Min(0f)]
        [Tooltip("의료시설 레벨이 보너스 시작 레벨 이상일 때, 레벨마다 증가하는 최대 체력입니다.")]
        private float medicalHealthBonusPerLevel = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("주방 레벨이 보너스 시작 레벨 이상일 때, 레벨마다 증가하는 최대 스태미너입니다.")]
        private float kitchenStaminaBonusPerLevel = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("침대 레벨이 보너스 시작 레벨 이상일 때, 레벨마다 증가하는 최대 스태미너입니다.")]
        private float bedStaminaBonusPerLevel = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("헬스장 레벨이 보너스 시작 레벨 이상일 때, 레벨마다 증가하는 최대 체력입니다.")]
        private float gymHealthBonusPerLevel = 5f;

        public int BonusStartLevel => Mathf.Max(1, bonusStartLevel);
        public float MedicalHealthBonusPerLevel => Mathf.Max(0f, medicalHealthBonusPerLevel);
        public float KitchenStaminaBonusPerLevel => Mathf.Max(0f, kitchenStaminaBonusPerLevel);
        public float BedStaminaBonusPerLevel => Mathf.Max(0f, bedStaminaBonusPerLevel);
        public float GymHealthBonusPerLevel => Mathf.Max(0f, gymHealthBonusPerLevel);
        public float GymCarryWeightBonusPerLevelKg => 0f;
    }
}

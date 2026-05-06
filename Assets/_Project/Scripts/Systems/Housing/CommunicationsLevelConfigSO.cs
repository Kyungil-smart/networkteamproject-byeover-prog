using System;
using UnityEngine;

namespace DeadZone.Core
{
    /// <summary>
    /// 통신장비 레벨별 해금 퀘스트와 보너스 수치를 담는 정적 데이터입니다.
    /// 런타임 상태는 저장하지 않고, 값 조회만 담당합니다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Housing/Communications Level Config", fileName = "Communications_LevelConfig")]
    public class CommunicationsLevelConfigSO : ScriptableObject
    {
        [SerializeField]
        [Tooltip("통신장비 Lv1~Lv4 설정입니다.")]
        private CommunicationsLevelConfig[] levels;

        public CommunicationsLevelConfig[] Levels => levels;

        public CommunicationsLevelConfig GetLevel(int level)
        {
            if (levels == null || levels.Length == 0)
                return null;

            for (int i = 0; i < levels.Length; i++)
            {
                CommunicationsLevelConfig config = levels[i];

                if (config == null)
                    continue;

                if (config.Level == level)
                    return config;
            }

            return null;
        }

        public bool TryGetLevelByQuest(string questId, out CommunicationsLevelConfig levelConfig)
        {
            levelConfig = null;

            if (string.IsNullOrWhiteSpace(questId))
                return false;

            if (levels == null || levels.Length == 0)
                return false;

            for (int i = 0; i < levels.Length; i++)
            {
                CommunicationsLevelConfig config = levels[i];

                if (config == null)
                    continue;

                if (!config.ContainsQuest(questId))
                    continue;

                levelConfig = config;
                return true;
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (levels == null)
                return;

            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] == null)
                    continue;

                levels[i].Validate();
            }
        }
#endif
    }

    [Serializable]
    public class CommunicationsLevelConfig
    {
        [SerializeField]
        [Min(1)]
        [Tooltip("통신장비 레벨입니다. Lv1~Lv4를 사용합니다.")]
        private int level = 1;

        [SerializeField]
        [Tooltip("이 레벨에서 해금되는 시작 퀘스트 ID입니다. 예: Q3")]
        private string unlockedQuestStartId = "Q1";

        [SerializeField]
        [Tooltip("이 레벨에서 해금되는 마지막 퀘스트 ID입니다. 예: Q4")]
        private string unlockedQuestEndId = "Q2";

        [SerializeField]
        [Tooltip("이 레벨 업그레이드 전에 완료되어야 하는 퀘스트 ID입니다. Lv1은 비워둡니다.")]
        private string requiredCompletedQuestId;

        [SerializeField]
        [Min(0)]
        [Tooltip("경험치 보너스 퍼센트입니다. 예: 10 = 경험치 +10%")]
        private int experienceBonusPercent;

        [SerializeField]
        [Min(0)]
        [Tooltip("감지 저항 보너스 퍼센트입니다. 예: 5 = 감지 저항 +5%")]
        private int detectionResistancePercent;

        [SerializeField]
        [Min(0)]
        [Tooltip("트레이더 할인 퍼센트입니다. 예: 5 = 트레이더 할인 5%")]
        private int traderDiscountPercent;

        public int Level => Mathf.Max(1, level);
        public string UnlockedQuestStartId => unlockedQuestStartId;
        public string UnlockedQuestEndId => unlockedQuestEndId;
        public string RequiredCompletedQuestId => requiredCompletedQuestId;
        public int ExperienceBonusPercent => Mathf.Max(0, experienceBonusPercent);
        public int DetectionResistancePercent => Mathf.Max(0, detectionResistancePercent);
        public int TraderDiscountPercent => Mathf.Max(0, traderDiscountPercent);

        public bool HasRequiredCompletedQuest => !string.IsNullOrWhiteSpace(requiredCompletedQuestId);

        public string GetQuestRangeText()
        {
            if (string.IsNullOrWhiteSpace(unlockedQuestStartId) && string.IsNullOrWhiteSpace(unlockedQuestEndId))
                return "없음";

            if (string.Equals(unlockedQuestStartId, unlockedQuestEndId, StringComparison.OrdinalIgnoreCase))
                return unlockedQuestStartId;

            return $"{unlockedQuestStartId}~{unlockedQuestEndId}";
        }

        public bool ContainsQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            int questNumber = ExtractQuestNumber(questId);
            int startNumber = ExtractQuestNumber(unlockedQuestStartId);
            int endNumber = ExtractQuestNumber(unlockedQuestEndId);

            if (questNumber < 0 || startNumber < 0 || endNumber < 0)
                return string.Equals(questId, unlockedQuestStartId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(questId, unlockedQuestEndId, StringComparison.OrdinalIgnoreCase);

            if (startNumber > endNumber)
            {
                int temp = startNumber;
                startNumber = endNumber;
                endNumber = temp;
            }

            return questNumber >= startNumber && questNumber <= endNumber;
        }

        private static int ExtractQuestNumber(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return -1;

            int value = 0;
            bool hasNumber = false;

            for (int i = 0; i < questId.Length; i++)
            {
                char c = questId[i];

                if (!char.IsDigit(c))
                    continue;

                value = value * 10 + (c - '0');
                hasNumber = true;
            }

            return hasNumber ? value : -1;
        }

#if UNITY_EDITOR
        public void Validate()
        {
            level = Mathf.Max(1, level);
            experienceBonusPercent = Mathf.Max(0, experienceBonusPercent);
            detectionResistancePercent = Mathf.Max(0, detectionResistancePercent);
            traderDiscountPercent = Mathf.Max(0, traderDiscountPercent);
        }
#endif
    }
}

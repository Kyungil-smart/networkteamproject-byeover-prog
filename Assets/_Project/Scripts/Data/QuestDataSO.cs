using System;
using UnityEngine;


namespace DeadZone.Core
{
    [Serializable]
    public struct QuestObjectiveData
    {
        public ObjectiveType type;
        public string targetID;
        public int requiredCount;
        public string location;
    }

    public enum RewardType : byte { Credits, Item, FacilityMaterial }

    [Serializable]
    public struct QuestReward
    {
        public RewardType type;
        public string itemID;
        public int amount;
    }

    [CreateAssetMenu(menuName = "DeadZone/Quests/Quest Data", fileName = "Q_New")]
    public class QuestDataSO : ScriptableObject
    {
        [Header("Identity")]
        public string questID;
        public string questName;
        [TextArea] public string description;

        [Header("Objectives")]
        public QuestObjectiveData[] objectives;

        [Header("Rewards")]
        public QuestReward[] rewards;

        [Header("Flow")]
        public string unlockZoneID;
        public string prerequisiteQuestID;

        [Header("Side Quest")]
        [Tooltip("true면 사이드 퀘스트 (Q2-1, Q3-1). 메인 진행의 선행 조건이 아님.")]
        public bool isSideQuest;
    }
}
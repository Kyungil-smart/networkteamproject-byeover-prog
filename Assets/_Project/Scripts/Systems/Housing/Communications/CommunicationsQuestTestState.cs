using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Systems
{
    // 실제 QuestManager 연동 전까지 통신장비 업그레이드 조건을 테스트하기 위한 임시 퀘스트 완료 상태
    // UI, Player, 실제 QuestManager가 완성되면 제거하거나 QuestManager 기반 구현으로 교체
    [DisallowMultipleComponent]
    public class CommunicationsQuestTestState : MonoBehaviour, IQuestCompletionReader
    {
        [Header("테스트 완료 퀘스트")]
        [SerializeField]
        [Tooltip("테스트상 완료 처리할 퀘스트 ID 목록입니다. 예: Q2, Q4, Q6")]
        private List<string> completedQuestIds = new();

        [Header("로그")]
        [SerializeField]
        [Tooltip("퀘스트 완료 상태 변경 로그를 Console에 출력할지 여부입니다.")]
        private bool logStateChanged = true;

        public bool IsQuestCompleted(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            for (int i = 0; i < completedQuestIds.Count; i++)
            {
                if (string.Equals(completedQuestIds[i], questId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public void SetQuestCompleted(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return;

            if (IsQuestCompleted(questId))
                return;

            completedQuestIds.Add(questId);

            if (logStateChanged)
                Debug.Log($"[CommunicationsQuestTestState] 테스트 퀘스트 완료 처리: {questId}", this);
        }

        public void ClearCompletedQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return;

            for (int i = completedQuestIds.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(completedQuestIds[i], questId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                completedQuestIds.RemoveAt(i);
            }

            if (logStateChanged)
                Debug.Log($"[CommunicationsQuestTestState] 테스트 퀘스트 완료 해제: {questId}", this);
        }

        [ContextMenu("테스트 Q2 완료 처리")]
        private void DebugCompleteQ2()
        {
            SetQuestCompleted("Q2");
        }

        [ContextMenu("테스트 Q4 완료 처리")]
        private void DebugCompleteQ4()
        {
            SetQuestCompleted("Q4");
        }

        [ContextMenu("테스트 Q6 완료 처리")]
        private void DebugCompleteQ6()
        {
            SetQuestCompleted("Q6");
        }

        [ContextMenu("테스트 완료 퀘스트 출력")]
        private void DebugPrintCompletedQuests()
        {
            if (completedQuestIds.Count == 0)
            {
                Debug.Log("[CommunicationsQuestTestState] 테스트 완료 퀘스트가 없습니다.", this);
                return;
            }

            for (int i = 0; i < completedQuestIds.Count; i++)
                Debug.Log($"[CommunicationsQuestTestState] 완료 퀘스트: {completedQuestIds[i]}", this);
        }
    }
}

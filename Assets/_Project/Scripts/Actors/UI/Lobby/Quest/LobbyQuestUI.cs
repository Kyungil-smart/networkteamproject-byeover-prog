using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Quests;

namespace DeadZone.Actors.UI.Lobby
{
    public class LobbyQuestUI : MonoBehaviour
    {
        [Header("퀘스트 데이터")]
        [Tooltip("로비 퀘스트탭에 표시할 QuestDataSO 배열입니다.")]
        [SerializeField] private QuestDataSO[] quests;

        [Header("왼쪽 목록")]
        [Tooltip("QuestListRowUI 프리팹이 생성될 부모입니다. 현재 구조에서는 QuestList 오브젝트를 연결하세요.")]
        [SerializeField] private Transform questListRoot;

        [Tooltip("왼쪽 목록에 생성할 퀘스트 Row 프리팹입니다.")]
        [SerializeField] private QuestListRowUI questRowPrefab;

        [Header("오른쪽 상세")]
        [Tooltip("Right_QuestDetailPanel에 붙은 QuestDetailUI입니다.")]
        [SerializeField] private QuestDetailUI questDetailUI;

        [Header("옵션")]
        [Tooltip("탭이 켜질 때 첫 번째 퀘스트를 자동 선택합니다.")]
        [SerializeField] private bool selectFirstQuestOnEnable = true;

        private readonly List<QuestListRowUI> spawnedRows = new();
        private QuestManager questManager;
        private QuestDataSO selectedQuest;

        private void OnEnable()
        {
            ResolveQuestManager();
            RebuildList();

            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void ResolveQuestManager()
        {
            if (questManager != null) return;

            if (!ServiceLocator.TryGet(out questManager))
            {
                questManager = FindFirstObjectByType<QuestManager>();
            }
        }

        private void RebuildList()
        {
            ClearRows();

            if (quests == null || quests.Length == 0)
            {
                questDetailUI?.Clear();
                return;
            }

            QuestDataSO firstSelectableQuest = null;

            foreach (QuestDataSO quest in quests)
            {
                if (quest == null) continue;

                QuestListRowUI row = Instantiate(questRowPrefab, questListRoot);
                row.Bind(quest, GetQuestState(quest), OnSelectQuest);

                spawnedRows.Add(row);

                if (firstSelectableQuest == null)
                    firstSelectableQuest = quest;
            }

            if (selectFirstQuestOnEnable && firstSelectableQuest != null)
            {
                SelectQuest(firstSelectableQuest);
            }
            else if (selectedQuest != null)
            {
                SelectQuest(selectedQuest);
            }
        }

        private void ClearRows()
        {
            for (int i = spawnedRows.Count - 1; i >= 0; i--)
            {
                if (spawnedRows[i] != null)
                    Destroy(spawnedRows[i].gameObject);
            }

            spawnedRows.Clear();
        }

        private void OnSelectQuest(QuestDataSO quest)
        {
            SelectQuest(quest);
        }

        private void SelectQuest(QuestDataSO quest)
        {
            selectedQuest = quest;

            RefreshRowsSelection();
            questDetailUI?.Show(
                quest,
                GetQuestState(quest),
                GetObjectiveProgressText(quest),
                RequestAcceptQuest
            );
        }

        private void RefreshRowsSelection()
        {
            foreach (QuestListRowUI row in spawnedRows)
            {
                if (row == null) continue;

                QuestViewState state = GetQuestState(row.Quest);
                bool selected = row.Quest == selectedQuest;

                row.Refresh(state, selected);
            }
        }

        private QuestViewState GetQuestState(QuestDataSO quest)
        {
            if (quest == null)
                return QuestViewState.Locked;

            ResolveQuestManager();

            if (questManager == null || NetworkManager.Singleton == null)
            {
                return HasPrerequisiteText(quest)
                    ? QuestViewState.Locked
                    : QuestViewState.Available;
            }

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            if (questManager.IsQuestCompleted(localClientId, quest.questID))
                return QuestViewState.Completed;

            if (questManager.IsQuestActive(localClientId, quest.questID))
                return QuestViewState.Active;

            if (!string.IsNullOrWhiteSpace(quest.prerequisiteQuestID)
                && !questManager.IsQuestCompleted(localClientId, quest.prerequisiteQuestID))
                return QuestViewState.Locked;

            return QuestViewState.Available;
        }

        private bool HasPrerequisiteText(QuestDataSO quest)
        {
            return quest != null && !string.IsNullOrWhiteSpace(quest.prerequisiteQuestID);
        }

        private string GetObjectiveProgressText(QuestDataSO quest)
        {
            if (quest == null || quest.objectives == null || quest.objectives.Length == 0)
                return string.Empty;

            ResolveQuestManager();

            if (questManager == null || NetworkManager.Singleton == null)
                return string.Empty;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;

            List<string> lines = new();

            foreach (QuestObjectiveData objective in quest.objectives)
            {
                int current = questManager.GetObjectiveProgress(localClientId, quest.questID, objective.targetID);
                int required = Mathf.Max(1, objective.requiredCount);

                lines.Add($"{current}/{required}");
            }

            return string.Join("\n", lines);
        }

        private void RequestAcceptQuest(QuestDataSO quest)
        {
            if (quest == null) return;

            ResolveQuestManager();

            if (questManager == null)
            {
                Debug.LogWarning("[LobbyQuestUI] QuestManager를 찾을 수 없습니다.");
                return;
            }

            QuestViewState state = GetQuestState(quest);
            if (state != QuestViewState.Available)
                return;

            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[LobbyQuestUI] NetworkManager가 없습니다. 퀘스트 수락은 네트워크 세션에서 처리됩니다.");
                return;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                questManager.AcceptQuest(NetworkManager.Singleton.LocalClientId, quest.questID);
            }
            else
            {
                questManager.AcceptQuestServerRpc(quest.questID);
            }

            SelectQuest(quest);
        }

        private void RefreshCurrentView()
        {
            RefreshRowsSelection();

            if (selectedQuest != null)
            {
                questDetailUI?.Show(
                    selectedQuest,
                    GetQuestState(selectedQuest),
                    GetObjectiveProgressText(selectedQuest),
                    RequestAcceptQuest
                );
            }
        }

        private void OnQuestAccepted(QuestAcceptedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            RefreshCurrentView();
        }

        private void OnQuestProgress(QuestProgressEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            RefreshCurrentView();
        }

        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            RefreshCurrentView();
        }

        private bool IsLocalClientEvent(ulong clientId)
        {
            return NetworkManager.Singleton == null
                   || clientId == NetworkManager.Singleton.LocalClientId;
        }
    }

    public enum QuestViewState
    {
        Locked,
        Available,
        Active,
        Completed
    }
}
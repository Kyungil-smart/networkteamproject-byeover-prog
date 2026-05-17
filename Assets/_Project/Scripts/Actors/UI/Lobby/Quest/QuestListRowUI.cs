using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Actors.UI.Lobby
{
    public class QuestListRowUI : MonoBehaviour
    {
        [Header("UI 연결")]
        [SerializeField] private Button button;

        [Tooltip("버튼 안에 있는 TMP_Text입니다. 퀘스트 이름을 표시합니다.")]
        [SerializeField] private TMP_Text questNameText;

        [Header("선택 표시")]
        [Tooltip("선택됐을 때 켤 오브젝트입니다. 없으면 비워도 됩니다.")]
        [SerializeField] private GameObject selectedRoot;

        public QuestDataSO Quest { get; private set; }

        private Action<QuestDataSO> onClicked;

        private void Awake()
        {
            if (button == null)
                button = GetComponent<Button>();

            if (questNameText == null)
                questNameText = GetComponentInChildren<TMP_Text>(true);
        }

        public void Bind(QuestDataSO quest, QuestViewState state, Action<QuestDataSO> clickCallback)
        {
            Quest = quest;
            onClicked = clickCallback;

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }

            Refresh(state, false);
        }

        public void Refresh(QuestViewState state, bool selected)
        {
            if (questNameText != null)
            {
                string questName = Quest != null ? Quest.questName : "퀘스트 없음";
                questNameText.text = GetDisplayName(questName, state);
            }

            if (selectedRoot != null)
                selectedRoot.SetActive(selected);

            if (button != null)
                button.interactable = Quest != null;
        }

        private static string GetDisplayName(string questName, QuestViewState state)
        {
            return state switch
            {
                QuestViewState.Locked => $"[잠김] {questName}",
                QuestViewState.Available => questName,
                QuestViewState.Active => $"[진행중] {questName}",
                QuestViewState.Completed => $"[완료] {questName}",
                QuestViewState.Claimed => $"[수령 완료] {questName}",
                _ => questName
            };
        }

        private void HandleClick()
        {
            if (Quest == null) return;
            onClicked?.Invoke(Quest);
        }

        private void OnDestroy()
        {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }
    }
}

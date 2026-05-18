using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DeadZone.Network;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors.UI
{
    public class RaidResultUI : MonoBehaviour
    {
        private const string DefaultLobbySceneName = "Lobby";

        [Header("패널")]
        [SerializeField] private GameObject raidResultUI;
        [SerializeField] private GameObject gameOverUI;

        [Header("생존 결과 텍스트")]
        [SerializeField] private TMP_Text textSurvival;
        [SerializeField] private TMP_Text textMapName;
        [SerializeField] private TMP_Text textKillCount;
        [SerializeField] private TMP_Text textSurvivalTime;

        [Header("생존 - 획득 아이템")]
        [SerializeField] private Transform getItemContent;
        [SerializeField] private GameObject getItemRowPrefab;

        [Header("사망 결과 텍스트")]
        [SerializeField] private TMP_Text textLose;
        [SerializeField] private TMP_Text textDeadMapName;
        [SerializeField] private TMP_Text textDeadKillCount;
        [SerializeField] private TMP_Text textDeadSurvivalTime;

        [Header("버튼")]
        [SerializeField] private Button btnGoLobbySurvived;
        [SerializeField] private Button btnGoLobbyDead;

        [Header("씬 이동")]
        [SerializeField] private string lobbySceneName = DefaultLobbySceneName;

        private void Awake()
        {
            if (btnGoLobbySurvived != null)
                btnGoLobbySurvived.onClick.AddListener(GoLobby);

            if (btnGoLobbyDead != null)
                btnGoLobbyDead.onClick.AddListener(GoLobby);
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (btnGoLobbySurvived != null)
                btnGoLobbySurvived.onClick.RemoveListener(GoLobby);

            if (btnGoLobbyDead != null)
                btnGoLobbyDead.onClick.RemoveListener(GoLobby);
        }

        public void Refresh()
        {
            bool isSurvived = RaidResultData.ResultType == RaidResultType.Survived;

            if (raidResultUI != null)
                raidResultUI.SetActive(isSurvived);

            if (gameOverUI != null)
                gameOverUI.SetActive(!isSurvived);

            if (isSurvived)
            {
                ShowSurvivedResult();
            }
            else
            {
                ShowDeadResult();
            }
        }

        private void ShowSurvivedResult()
        {
            if (textSurvival != null)
                textSurvival.text = "생존";

            if (textMapName != null)
                textMapName.text = RaidResultData.MapName;

            if (textKillCount != null)
                textKillCount.text = RaidResultData.KillCount.ToString();

            if (textSurvivalTime != null)
                textSurvivalTime.text = RaidResultData.FormatTime(RaidResultData.SurvivalTime);

            RefreshLootItems();
        }

        private void ShowDeadResult()
        {
            if (textLose != null)
                textLose.text = "사망";

            if (textDeadMapName != null)
                textDeadMapName.text = RaidResultData.MapName;

            if (textDeadKillCount != null)
                textDeadKillCount.text = RaidResultData.KillCount.ToString();

            if (textDeadSurvivalTime != null)
                textDeadSurvivalTime.text = RaidResultData.FormatTime(RaidResultData.SurvivalTime);
        }

        private void RefreshLootItems()
        {
            if (getItemContent == null || getItemRowPrefab == null)
                return;

            for (int i = getItemContent.childCount - 1; i >= 0; i--)
            {
                Destroy(getItemContent.GetChild(i).gameObject);
            }

            foreach (RaidLootResult loot in RaidResultData.LootItems)
            {
                GameObject row = Instantiate(getItemRowPrefab, getItemContent);

                TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);

                if (texts.Length >= 1)
                    texts[0].text = loot.itemName;

                if (texts.Length >= 2)
                    texts[1].text = $"X {loot.count:N0}";
            }
        }

        private void GoLobby()
        {
            NetworkGameManager.RequestReturnToLobbyAfterRaid(NormalizeLobbySceneName(lobbySceneName));
        }

        private static string NormalizeLobbySceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return DefaultLobbySceneName;

            return string.Equals(sceneName, "HJO_Lobby", System.StringComparison.Ordinal)
                ? DefaultLobbySceneName
                : sceneName;
        }
    }
}

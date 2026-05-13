using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Actors.UI;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors.Extraction
{
    public class ExtractionResultTrigger : MonoBehaviour
    {
        [Header("탈출 설정")]
        [SerializeField] private string resultSceneName = "HJO_RaidResult";
        [SerializeField] private float requiredStayTime = 7f;
        [SerializeField] private string playerTag = "Player";

        [Header("디버그")]
        [SerializeField] private float currentStayTime;
        [SerializeField] private bool isPlayerInside;
        [SerializeField] private bool isCompleted;

        private void Update()
        {
            if (isCompleted)
                return;

            if (!isPlayerInside)
                return;

            currentStayTime += Time.deltaTime;

            if (currentStayTime >= requiredStayTime)
            {
                CompleteExtraction();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag))
                return;

            isPlayerInside = true;
            currentStayTime = 0f;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag))
                return;

            isPlayerInside = false;
            currentStayTime = 0f;
        }

        private void CompleteExtraction()
        {
            isCompleted = true;

            RaidSessionTracker tracker = RaidSessionTracker.Instance;

            if (tracker == null)
            {
                Debug.LogError("[ExtractionResultTrigger] RaidSessionTracker가 씬에 없습니다.");
                return;
            }

            tracker.StopTracking();

            RaidResultData.SetSurvived(
                tracker.MapName,
                tracker.KillCount,
                tracker.AcquiredDollar,
                tracker.SurvivalTime,
                tracker.GetLootResults()
            );

            LoadingScreenService.LoadSceneOrFallback(resultSceneName);
        }
    }
}

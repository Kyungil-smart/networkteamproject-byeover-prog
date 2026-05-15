using UnityEngine;

using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors.Extraction
{
    public class ExtractionResultTrigger : MonoBehaviour
    {
        [Header("탈출 설정")]
        [SerializeField] private string resultSceneName = "HJO_RaidResult";
        [SerializeField] private float requiredStayTime = 7f;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private string requiredUnlockZoneId;

        [Header("디버그")]
        [SerializeField] private float currentStayTime;
        [SerializeField] private bool isPlayerInside;
        [SerializeField] private bool isCompleted;

        private Collider triggerCollider;
        private bool unlockedByRuntimeEvent;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            RefreshUnlockState();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void Start()
        {
            RefreshUnlockState();
        }

        private void Update()
        {
            if (isCompleted || !isPlayerInside)
                return;

            currentStayTime += Time.deltaTime;

            if (currentStayTime >= requiredStayTime)
                CompleteExtraction();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsUnlocked() || !other.CompareTag(playerTag))
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
            if (!IsUnlocked())
                return;

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

        private void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (string.IsNullOrWhiteSpace(requiredUnlockZoneId))
                return;

            if (e.unlockZoneId.ToString() != requiredUnlockZoneId)
                return;

            unlockedByRuntimeEvent = true;
            RefreshUnlockState();
        }

        private void RefreshUnlockState()
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<Collider>();

            bool unlocked = IsUnlocked();
            if (triggerCollider != null)
                triggerCollider.enabled = unlocked;

            if (!unlocked)
            {
                isPlayerInside = false;
                currentStayTime = 0f;
            }
        }

        private bool IsUnlocked()
        {
            if (string.IsNullOrWhiteSpace(requiredUnlockZoneId))
                return true;

            if (unlockedByRuntimeEvent)
                return true;

            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem == null)
                cloudSaveSystem = FindFirstObjectByType<CloudSaveSystem>(FindObjectsInactive.Include);

            return cloudSaveSystem?.CurrentData?.progress?.unlockedZones != null &&
                   cloudSaveSystem.CurrentData.progress.unlockedZones.Contains(requiredUnlockZoneId);
        }
    }
}

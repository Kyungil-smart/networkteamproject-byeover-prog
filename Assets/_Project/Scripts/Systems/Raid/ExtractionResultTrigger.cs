using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

using DeadZone.Actors.UI;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems.Raid;

namespace DeadZone.Actors.Extraction
{
    /// <summary>
    /// 레이드 결과 또는 엔딩 씬으로 이동하는 탈출 트리거를 제어한다.
    /// </summary>
    public class ExtractionResultTrigger : MonoBehaviour
    {
        [Header("탈출 설정")]
        [SerializeField] private string resultSceneName = "HJO_RaidResult";
        [SerializeField] private float requiredStayTime = 7f;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private string requiredUnlockZoneId;

        [Header("보스 처치 잠금")]
        [Tooltip("비워두면 보스 처치 조건을 사용하지 않습니다. EnemyStatsSO.enemyId와 동일해야 합니다.")]
        [SerializeField] private string requiredEnemyKillId;
        [Tooltip("requiredEnemyKillId가 지정된 경우 필요한 처치 수입니다.")]
        [SerializeField, Min(0)] private int requiredEnemyKillCount;

        [Header("씬 로드")]
        [SerializeField] private bool useNetworkSceneLoadWhenAvailable = true;
        [SerializeField] private bool useExistingExtractionUI = true;
        [SerializeField] private string extractionUIDisplayName = "Ending";

        [Header("디버그")]
        [SerializeField] private float currentStayTime;
        [SerializeField] private bool isPlayerInside;
        [SerializeField] private bool isCompleted;
        [SerializeField] private int currentEnemyKillCount;

        private Collider triggerCollider;
        private bool unlockedByRuntimeEvent;
        private ExtractionUI extractionUI;
        private bool extractionUIVisible;

        public void ConfigureRuntime(
            string resultScene,
            float stayTime,
            string unlockZoneId,
            string enemyKillId,
            int enemyKillCount,
            string uiDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(resultScene))
                resultSceneName = resultScene;

            requiredStayTime = Mathf.Max(0f, stayTime);
            requiredUnlockZoneId = unlockZoneId ?? string.Empty;
            requiredEnemyKillId = enemyKillId ?? string.Empty;
            requiredEnemyKillCount = Mathf.Max(0, enemyKillCount);
            extractionUIDisplayName = string.IsNullOrWhiteSpace(uiDisplayName) ? resultSceneName : uiDisplayName;
            RefreshUnlockState();
        }

        private void Awake()
        {
            triggerCollider = ResolveTriggerCollider();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            RefreshUnlockState();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
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
            UpdateExtractionUI();

            if (currentStayTime >= requiredStayTime)
                CompleteExtraction();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsUnlocked() || !other.CompareTag(playerTag))
                return;

            if (!CanLocalInstanceCompleteExtraction())
                return;

            isPlayerInside = true;
            currentStayTime = 0f;
            ShowExtractionUI();

            if (requiredStayTime <= 0f)
                CompleteExtraction();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag))
                return;

            isPlayerInside = false;
            currentStayTime = 0f;
            HideExtractionUI();
        }

        private void CompleteExtraction()
        {
            if (!IsUnlocked())
                return;

            if (!CanLocalInstanceCompleteExtraction())
                return;

            isCompleted = true;
            HideExtractionUI();

            RaidSessionTracker tracker = RaidSessionTracker.Instance;
            if (tracker != null)
            {
                tracker.StopTracking();

                RaidResultData.SetSurvived(
                    tracker.MapName,
                    tracker.KillCount,
                    tracker.AcquiredDollar,
                    tracker.SurvivalTime,
                    tracker.GetLootResults()
                );
            }
            else
            {
                Debug.LogWarning("[ExtractionResultTrigger] RaidSessionTracker가 씬에 없어 결과 데이터 갱신 없이 씬을 전환합니다.", this);
            }

            LoadResultScene();
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

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            if (string.IsNullOrWhiteSpace(requiredEnemyKillId))
                return;

            if (e.enemyId.ToString() != requiredEnemyKillId)
                return;

            if (requiredEnemyKillCount <= 0)
                return;

            currentEnemyKillCount = Mathf.Min(requiredEnemyKillCount, currentEnemyKillCount + 1);
            RefreshUnlockState();
        }

        private void RefreshUnlockState()
        {
            if (triggerCollider == null)
                triggerCollider = ResolveTriggerCollider();

            bool unlocked = IsUnlocked();
            if (triggerCollider != null)
                triggerCollider.enabled = unlocked;

            if (!unlocked)
            {
                isPlayerInside = false;
                currentStayTime = 0f;
                HideExtractionUI();
            }
        }

        private bool IsUnlocked()
        {
            if (!HasRequiredEnemyKills())
                return false;

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

        private bool HasRequiredEnemyKills()
        {
            if (string.IsNullOrWhiteSpace(requiredEnemyKillId))
                return true;

            return requiredEnemyKillCount <= 0 || currentEnemyKillCount >= requiredEnemyKillCount;
        }

        private bool CanLocalInstanceCompleteExtraction()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
        }

        private void LoadResultScene()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            bool shouldUseNetworkSceneLoad =
                useNetworkSceneLoadWhenAvailable &&
                networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsServer &&
                networkManager.SceneManager != null;

            if (shouldUseNetworkSceneLoad)
            {
                NetworkGameManager.LoadSceneWithLoading(resultSceneName, LoadSceneMode.Single);
                return;
            }

            LoadingScreenService.LoadSceneOrFallback(resultSceneName);
        }

        private void ShowExtractionUI()
        {
            if (!useExistingExtractionUI)
                return;

            ExtractionUI ui = ResolveExtractionUI();
            if (ui == null)
            {
                Debug.LogWarning("[ExtractionResultTrigger] ExtractionUI를 찾지 못해 엔딩 탈출 진행 UI를 표시하지 못했습니다.", this);
                return;
            }

            if (!ui.gameObject.activeSelf)
                ui.gameObject.SetActive(true);

            extractionUIVisible = true;
            ui.Show(extractionUIDisplayName, Mathf.Max(0f, requiredStayTime));
        }

        private void UpdateExtractionUI()
        {
            if (!extractionUIVisible)
                return;

            ExtractionUI ui = ResolveExtractionUI();
            if (ui != null)
                ui.UpdateProgress(currentStayTime, Mathf.Max(0f, requiredStayTime));
        }

        private void HideExtractionUI()
        {
            if (!extractionUIVisible)
                return;

            extractionUIVisible = false;

            ExtractionUI ui = ResolveExtractionUI();
            if (ui != null)
                ui.Hide();
        }

        private ExtractionUI ResolveExtractionUI()
        {
            if (extractionUI == null)
                extractionUI = FindFirstObjectByType<ExtractionUI>(FindObjectsInactive.Include);

            return extractionUI;
        }

        private Collider ResolveTriggerCollider()
        {
            Collider[] colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger)
                    return colliders[i];
            }

            return colliders.Length > 0 ? colliders[0] : null;
        }
    }
}

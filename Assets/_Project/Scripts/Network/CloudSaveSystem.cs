using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using Unity.Netcode;
using UnityEngine;
using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Quests;
using DeadZone.Systems.Save;

namespace DeadZone.Network
{
    /// <summary>
    /// Firebase Cloud Firestore에 플레이어 저장 데이터를 로드하고 업로드합니다.
    /// </summary>
    public class CloudSaveSystem : MonoBehaviour
    {
        private const int DefaultStashColumnCount = 10;
        private const int DefaultStartingCredits = 50000;
        private const int EconomyStarterPackSchemaVersion = 3;
        private const string UsersCollection = "users";
        private const bool DisableFirestorePersistence = true;

        [Header("파산신청")]
        [Tooltip("파산신청 시 적용할 스타터팩 설정입니다.")]
        [SerializeField] private StarterPackConfigSO bankruptcyStarterPack;

        [Header("Save Audit")]
        [SerializeField] private bool logSaveAudit = true;

        private FirebaseFirestore db;
        private FirebaseAuthManager authManager;
        private PlayerCloudData currentData;
        private string loadedFirebaseUid = string.Empty;
        private bool isRegistered;

        /// <summary>
        /// 현재 로드된 플레이어 Cloud Save 데이터입니다.
        /// </summary>
        public PlayerCloudData CurrentData => currentData;

        /// <summary>
        /// 현재 플레이어 Cloud Save 데이터가 로드되어 있는지 반환합니다.
        /// </summary>
        public bool HasLoadedData => currentData != null;

        /// <summary>
        /// 현재 로드된 Cloud Save 데이터의 Firebase UID입니다.
        /// </summary>
        public string LoadedFirebaseUid => loadedFirebaseUid;

        public void ClearLoadedData()
        {
            currentData = null;
            loadedFirebaseUid = string.Empty;
        }

        private void Awake()
        {
            CloudSaveSystem existing = ServiceLocator.Get<CloudSaveSystem>();
            if (existing != null && existing != this)
            {
                Debug.LogWarning("[CloudSaveSystem] Duplicate instance ignored. Cloud Save must be owned by one active service instance.", this);
                enabled = false;
                return;
            }

            ServiceLocator.Register(this);
            isRegistered = true;
        }

        private void Start()
        {
            if (!isRegistered)
                return;

            EventBus.Subscribe<AuthSignedInEvent>(OnAuthSignedIn);
            EventBus.Subscribe<AuthSignedOutEvent>(OnAuthSignedOut);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Subscribe<SceneChangedEvent>(OnSceneChanged);
            EventBus.Subscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Subscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDestroy()
        {
            if (!isRegistered)
                return;

            EventBus.Unsubscribe<AuthSignedInEvent>(OnAuthSignedIn);
            EventBus.Unsubscribe<AuthSignedOutEvent>(OnAuthSignedOut);
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Unsubscribe<SceneChangedEvent>(OnSceneChanged);
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnQuestAccepted);
            EventBus.Unsubscribe<QuestProgressEvent>(OnQuestProgress);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);

            ServiceLocator.Unregister(this);
            isRegistered = false;
        }

        private void OnApplicationQuit()
        {
            if (!isRegistered)
                return;

            // 동기 대기. 3초 타임아웃 초과 시 포기하고 앱 종료.
            if (!HasLoadedData || authManager == null || !authManager.IsSignedIn) return;
            if (loadedFirebaseUid != authManager.CurrentUid) return;

            var uploadTask = UploadAsync();
            uploadTask.Wait(TimeSpan.FromSeconds(3));
        }

        private bool TryEnsureFirestoreReady()
        {
            if (db != null)
            {
                return true;
            }

            var bootstrap = ServiceLocator.Get<FirebaseBootstrap>();
            if (bootstrap == null || !bootstrap.IsReady)
            {
                Debug.LogWarning("[CloudSaveSystem] Firestore attach skipped: FirebaseBootstrap is not ready");
                return false;
            }

            try
            {
                Debug.Log("[CloudSaveSystem] Preparing Firestore instance");

                FirebaseFirestore firestore = FirebaseFirestore.DefaultInstance;
                Debug.Log("[CloudSaveSystem] FirebaseFirestore.DefaultInstance acquired");

                if (DisableFirestorePersistence)
                {
                    TryDisableFirestorePersistence(firestore);
                }

                db = firestore;
                Debug.Log("[CloudSaveSystem] Attached to Firestore after Firebase sign-in");
                return true;
            }
            catch (Exception ex)
            {
                db = null;
                Debug.LogError($"[CloudSaveSystem] Firestore attach failed: {ex}");
                return false;
            }
        }

        private static void TryDisableFirestorePersistence(FirebaseFirestore firestore)
        {
            if (firestore == null)
                return;

            try
            {
                Debug.Log("[CloudSaveSystem] Disabling Firestore persistence before first read/write");
                firestore.Settings.PersistenceEnabled = false;
                Debug.Log("[CloudSaveSystem] Firestore persistence disabled");
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[CloudSaveSystem] Firestore persistence setting was already locked. Continuing with existing Firestore instance. Reason={exception.Message}");
            }
        }

        private bool TryGetSignedInAuth(out string uid, out string email)
        {
            uid = string.Empty;
            email = string.Empty;

            authManager ??= ServiceLocator.Get<FirebaseAuthManager>();

            if (authManager == null || !authManager.IsSignedIn)
            {
                return false;
            }

            uid = authManager.CurrentUid;
            email = authManager.CurrentEmail;

            return !string.IsNullOrWhiteSpace(uid);
        }

        // =================================================================
        // 이벤트 핸들러
        // =================================================================

        private async void OnAuthSignedIn(AuthSignedInEvent e)
        {
            currentData = null;
            loadedFirebaseUid = string.Empty;

            string signedInUid = e.firebaseUid.ToString();
            if (string.IsNullOrWhiteSpace(signedInUid))
            {
                Debug.LogWarning("[CloudSaveSystem] AuthSignedInEvent uid is empty. Cloud Save load skipped.");
                return;
            }

            await LoadAsync();
        }

        private void OnAuthSignedOut(AuthSignedOutEvent e)
        {
            ClearLoadedData();
        }

        private async void OnPlayerDied(PlayerDiedEvent e)
        {
            // 본인이 죽었을 때만 업로드
            if (!HasLoadedData || string.IsNullOrWhiteSpace(loadedFirebaseUid)) return;
            if (NetworkManager.Singleton == null) return;
            if (e.victimClientId != NetworkManager.Singleton.LocalClientId) return;

            Debug.Log("[CloudSaveSystem] Local player died -> upload");
            await UploadAsync();
        }

        private async void OnSceneChanged(SceneChangedEvent e)
        {
            // Hideout 복귀 시 업로드
            string sceneName = e.sceneName.ToString();
            if (!string.Equals(sceneName, "Hideout", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sceneName, "HideOut", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!HasLoadedData) return;
            if (string.IsNullOrWhiteSpace(loadedFirebaseUid)) return;

            bool isInParty = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            Debug.Log($"[HideoutLoad] Enter Hideout. isInParty={isInParty}, userId={loadedFirebaseUid}");
            Debug.Log("[Save] Save requested. reason=Enter Hideout auto upload");
            Debug.LogWarning("[Save] Save skipped because load is not completed yet.");
            await Task.CompletedTask;
        }

        private async void OnQuestAccepted(QuestAcceptedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            await UploadAsync();
        }

        private async void OnQuestProgress(QuestProgressEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            await UploadAsync();
        }

        private async void OnQuestCompleted(QuestCompletedEvent e)
        {
            if (!IsLocalClientEvent(e.clientId)) return;
            await UploadAsync();
        }

        private static bool IsLocalClientEvent(ulong clientId)
        {
            return NetworkManager.Singleton == null ||
                   clientId == NetworkManager.Singleton.LocalClientId;
        }

        // =================================================================
        // 다운로드
        // =================================================================

        /// <summary>
        /// users/{uid} 문서 읽어와 CurrentData 채움. 없으면 신규 유저로 간주하고 기본값 생성.
        /// </summary>
        public async Task<PlayerCloudData> LoadAsync()
        {
            if (!TryGetSignedInAuth(out string uid, out string email))
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot load: not ready or not signed in");
                return null;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot load: Firestore is not ready");
                return null;
            }

            bool isNew = false;

            try
            {
                Debug.Log($"[CloudSaveSystem] Preparing Firestore document reference users/{uid}");
                var doc = db.Collection(UsersCollection).Document(uid);
                Debug.Log($"[CloudSaveSystem] Requesting Firestore document snapshot users/{uid}");
                var snapshot = await doc.GetSnapshotAsync();
                Debug.Log($"[CloudSaveSystem] Firestore document load completed. Exists={snapshot.Exists}");

                if (snapshot.Exists)
                {
                    // [FirestoreData] 어노테이션이 붙어있으면 ConvertTo<T>()로 자동 역직렬화
                    currentData = snapshot.ConvertTo<PlayerCloudData>();
                    if (currentData == null) currentData = NewPlayerData(uid, authManager.CurrentEmail);
                    EnsureCloudDataContainers();
                    EnsureLobbySaveContainers();

                    bool shouldWriteBack = NormalizeFacilityProgressFromLegacyFields();
                    shouldWriteBack |= TryApplyStarterPackMigration();

                    if (shouldWriteBack)
                    {
                        loadedFirebaseUid = uid;
                        currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);
                        Debug.Log("[CloudSaveSystem] 기존 Cloud Save 문서에 스타터팩 경제 데이터 마이그레이션을 적용했습니다.", this);
                    }
                }
                else
                {
                    currentData = NewPlayerData(uid, email);
                    isNew = true;
                    loadedFirebaseUid = uid;

                    bool uploaded = await UploadAsync(); // 신규 유저는 즉시 기본 데이터 기록
                    if (!uploaded)
                    {
                        currentData = null;
                        loadedFirebaseUid = string.Empty;
                        return null;
                    }
                }

                loadedFirebaseUid = uid;
                LogSaveAudit("LoadAsync completed");

                EventBus.Publish(new CloudSaveLoadedEvent
                {
                    firebaseUid = uid,
                    isNewUser = isNew,
                });

                RestoreQuestStateIfAvailable();

                return currentData;
            }
            catch (Exception ex)
            {
                loadedFirebaseUid = string.Empty;
                Debug.LogError($"[CloudSaveSystem] Load failed: {ex}");
                return null;
            }
        }

        // =================================================================
        // 업로드
        // =================================================================

        /// <summary>
        /// 현재 씬에서 플레이어 관련 시스템 데이터를 수집해 Firestore에 기록.
        /// </summary>
        public async Task<bool> UploadAsync()
        {
            if (!TryGetSignedInAuth(out string uid, out _))
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: not ready or not signed in");
                return false;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: Firestore is not ready");
                return false;
            }

            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: currentData null (not loaded)");
                return false;
            }

            if (loadedFirebaseUid != uid)
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: loaded data does not match current Firebase user");
                return false;
            }

            try
            {
                string beforeCollectSummary = BuildSaveAuditSummary();
                CollectDataFromScene();
                LogSaveAudit("UploadAsync collected scene", $"before={beforeCollectSummary}");
                currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // [FirestoreData] 객체는 SetAsync에 바로 넘길 수 있음
                await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);

                EventBus.Publish(new CloudSaveUploadedEvent { firebaseUid = uid, success = true });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveSystem] Upload failed: {ex}");
                EventBus.Publish(new CloudSaveUploadedEvent { firebaseUid = uid, success = false });
                return false;
            }
        }

        public async Task<bool> SaveLobbyDataAsync(LobbySaveDTO lobbySaveDto)
        {
            if (lobbySaveDto == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 저장 실패. DTO가 null입니다.");
                return false;
            }

            if (!TryGetSignedInAuth(out string uid, out _))
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 저장 실패. 로그인 상태가 아닙니다.");
                return false;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 저장 실패. Firestore가 준비되지 않았습니다.");
                return false;
            }

            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 저장 실패. CurrentData가 없습니다.");
                return false;
            }

            if (loadedFirebaseUid != uid)
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 저장 실패. 현재 로그인 UID와 로드된 데이터 UID가 다릅니다.");
                return false;
            }

            try
            {
                EnsureCloudDataContainers();
                EnsureLobbySaveContainers();
                LogSaveAudit("SaveLobbyDataAsync request", BuildLobbyDtoAuditSummary(lobbySaveDto));

                CollectPersonalQuestProgress();
                ReplaceLobbyDtoFacilitiesFromCurrentData(lobbySaveDto);

                LobbySaveCloudData nextLobbySave = ToLobbySaveCloudData(lobbySaveDto);
                PreserveExistingLobbySectionsForEmptyInput(nextLobbySave);
                currentData.lobbySave = nextLobbySave;
                ApplyLobbySaveToLegacyCloudFields(lobbySaveDto);
                if (lobbySaveDto.hasCredits)
                    currentData.progress.credits = lobbySaveDto.credits;
                currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                currentData.schemaVersion = Mathf.Max(currentData.schemaVersion, 3);

                await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);
                LogSaveAudit("SaveLobbyDataAsync uploaded");

                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = true,
                });

                Debug.Log("[CloudSaveSystem] Lobby 저장 완료");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveSystem] Lobby 저장 실패: {ex}");
                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = false,
                });
                return false;
            }
        }

        /// <summary>
        /// 현재 로그인 사용자의 하우징 진행도를 Cloud Save의 legacy facilities와 lobbySave facilities에 함께 저장합니다.
        /// </summary>
        public async Task<bool> SaveHousingProgressAsync(PlayerHousingProgressDTO housingProgressDto, bool allowDefaultOverwrite = false)
        {
            if (housingProgressDto == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Housing save failed. DTO is null.");
                return false;
            }

            if (!TryGetSignedInAuth(out string uid, out _))
            {
                Debug.LogWarning("[CloudSaveSystem] Housing save failed. Not signed in.");
                return false;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning("[CloudSaveSystem] Housing save failed. Firestore is not ready.");
                return false;
            }

            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Housing save failed. CurrentData is null.");
                return false;
            }

            if (loadedFirebaseUid != uid)
            {
                Debug.LogWarning("[CloudSaveSystem] Housing save failed. Loaded data UID does not match current user.");
                return false;
            }

            try
            {
                housingProgressDto.Normalize();
                EnsureCloudDataContainers();
                EnsureLobbySaveContainers();
                LogSaveAudit("SaveHousingProgressAsync request", BuildHousingDtoAuditSummary(housingProgressDto));

                if (!TryApplyHousingProgressToCloudFields(housingProgressDto, allowDefaultOverwrite))
                    return false;

                currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                currentData.schemaVersion = Mathf.Max(currentData.schemaVersion, 3);

                await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);
                LogSaveAudit("SaveHousingProgressAsync uploaded");

                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = true,
                });

                Debug.Log("[CloudSaveSystem] Housing save completed.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveSystem] Housing save failed: {ex}");
                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = false,
                });
                return false;
            }
        }

        public LobbySaveDTO CreateLobbySaveDTOFromCurrentData()
        {
            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Lobby 로드 실패. CurrentData가 없습니다.");
                return null;
            }

            EnsureCloudDataContainers();
            EnsureLobbySaveContainers();

            LobbySaveDTO dto = HasLobbySaveData(currentData.lobbySave)
                ? ToLobbySaveDTO(currentData.lobbySave)
                : CreateLobbySaveDTOFromLegacyCloudFields();

            ReconcileLegacyInventorySectionsIntoLobbySaveDTO(dto);
            ReconcileLegacyFacilitiesIntoLobbySaveDTO(dto);
            return dto;
        }

        /// <summary>
        /// 현재 로드된 Cloud Save 데이터에서 서버 PlayerHousingProgress에 적용할 하우징 진행도 DTO를 생성합니다.
        /// </summary>
        public PlayerHousingProgressDTO CreateHousingProgressDTOFromCurrentData()
        {
            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Housing load failed. CurrentData is null.");
                return null;
            }

            EnsureCloudDataContainers();
            EnsureLobbySaveContainers();

            PlayerHousingProgressDTO dto = new PlayerHousingProgressDTO
            {
                workbenchLevel = currentData.facilities.workbench,
                medicalLevel = currentData.facilities.medical,
                gymLevel = currentData.facilities.gym,
                stashLevel = currentData.facilities.stash,
                kitchenLevel = currentData.facilities.kitchen,
                bedLevel = currentData.facilities.bed,
                commStationLevel = currentData.facilities.commStation
            };

            dto.Normalize();
            return dto;
        }

        /// <summary>
        /// 지정한 구역 해금 상태를 저장 데이터에 반영하고 업로드합니다.
        /// </summary>
        public async Task<bool> UnlockZoneAndUploadAsync(string zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                Debug.LogWarning("[CloudSaveSystem] Zone 저장 실패. ZoneId가 비어 있습니다.");
                return false;
            }

            if (!TryGetSignedInAuth(out string uid, out _))
            {
                Debug.LogWarning($"[CloudSaveSystem] Zone 저장 실패. 로그인 상태가 아닙니다. ZoneId={zoneId}");
                return false;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning($"[CloudSaveSystem] Zone 저장 실패. Firestore가 준비되지 않았습니다. ZoneId={zoneId}");
                return false;
            }

            if (currentData == null)
            {
                Debug.LogWarning($"[CloudSaveSystem] Zone 저장 실패. CurrentData가 없습니다. ZoneId={zoneId}");
                return false;
            }

            if (loadedFirebaseUid != uid)
            {
                Debug.LogWarning($"[CloudSaveSystem] Zone 저장 실패. 현재 로그인 UID와 로드된 데이터 UID가 다릅니다. ZoneId={zoneId}");
                return false;
            }

            try
            {
                EnsureCloudDataContainers();

                CollectDataFromScene();

                EnsureCloudDataContainers();

                bool added = false;

                if (!currentData.progress.unlockedZones.Contains(zoneId))
                {
                    currentData.progress.unlockedZones.Add(zoneId);
                    added = true;
                }

                currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);

                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = true,
                });

                Debug.Log($"[CloudSaveSystem] Zone 저장 완료. ZoneId={zoneId}, Added={added}");

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveSystem] Zone 저장 실패. ZoneId={zoneId}, Error={ex}");
                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = false,
                });

                return false;
            }
        }

        /// <summary>
        /// 퀘스트와 하이드아웃 진행도는 유지하고 경제 데이터만 스타터팩 상태로 초기화합니다.
        /// </summary>
        public async Task<bool> ApplyBankruptcyStarterPackAsync(StarterPackConfigSO starterPackOverride = null)
        {
            StarterPackConfigSO starterPack = starterPackOverride != null
                ? starterPackOverride
                : bankruptcyStarterPack;

            if (starterPack == null)
            {
                Debug.LogWarning("[CloudSaveSystem] 파산신청 실패. StarterPackConfigSO가 연결되지 않았습니다.", this);
                return false;
            }

            if (!TryGetSignedInAuth(out string uid, out _))
            {
                Debug.LogWarning("[CloudSaveSystem] 파산신청 실패. 로그인 상태가 아닙니다.", this);
                return false;
            }

            if (!TryEnsureFirestoreReady())
            {
                Debug.LogWarning("[CloudSaveSystem] 파산신청 실패. Firestore가 준비되지 않았습니다.", this);
                return false;
            }

            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] 파산신청 실패. Cloud Save 데이터가 아직 로드되지 않았습니다.", this);
                return false;
            }

            if (loadedFirebaseUid != uid)
            {
                Debug.LogWarning("[CloudSaveSystem] 파산신청 실패. 현재 로그인 UID와 로드된 데이터 UID가 다릅니다.", this);
                return false;
            }

            try
            {
                EnsureCloudDataContainers();

                // 진행도 계열은 현재 씬에서 확인 가능한 최신 값만 먼저 반영한다.
                CollectFacilities();
                CollectPersonalQuestProgress();

                EnsureCloudDataContainers();
                ApplyBankruptcyStarterPack(starterPack);
                LobbySaveDTO bankruptcyLobbySave = CreateLobbySaveDTOFromLegacyCloudFields();
                bankruptcyLobbySave.hasCredits = true;
                bankruptcyLobbySave.credits = starterPack.StartingCredits;
                bankruptcyLobbySave.inventoryItems.Clear();
                bankruptcyLobbySave.quickSlotItems.Clear();
                bankruptcyLobbySave.equipmentItems.Clear();
                ForceFacilityLevel(bankruptcyLobbySave, "Stash", 1);
                bankruptcyLobbySave.quickSlotItems.Clear();
                bankruptcyLobbySave.hasInventorySection = true;
                bankruptcyLobbySave.hasStashSection = true;
                bankruptcyLobbySave.hasEquipmentSection = true;
                bankruptcyLobbySave.hasQuickSlotSection = true;

                currentData.lobbySave = ToLobbySaveCloudData(bankruptcyLobbySave);
                ApplyBankruptcyLobbyStateToRuntime(bankruptcyLobbySave);

                currentData.profile.lastPlayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                await db.Collection(UsersCollection).Document(uid).SetAsync(currentData, SetOptions.Overwrite);

                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = true,
                });

                EventBus.Publish(new CloudSaveLoadedEvent
                {
                    firebaseUid = uid,
                    isNewUser = false,
                });

                Debug.Log("[CloudSaveSystem] 파산신청 완료. 크레딧, 인벤토리, 보관함, 장비 슬롯을 스타터팩 LobbySaveData로 초기화했습니다.", this);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveSystem] 파산신청 실패. Error={ex}", this);
                EventBus.Publish(new CloudSaveUploadedEvent
                {
                    firebaseUid = uid,
                    success = false,
                });
                return false;
            }
        }

        private void EnsureCloudDataContainers()
        {
            currentData.profile ??= new ProfileData();
            currentData.progress ??= new ProgressData();
            currentData.progress.personalActiveQuestIds ??= new List<string>();
            currentData.progress.personalCompletedQuestIds ??= new List<string>();
            currentData.progress.rewardClaimedQuestIds ??= new List<string>();
            currentData.progress.unlockedZones ??= new List<string>();
            currentData.progress.questObjectives ??= new List<QuestObjectiveProgress>();
            currentData.progress.pendingCompletionIds ??= new List<string>();
            currentData.stash ??= new StashData();
            currentData.stash.slots ??= new List<StashSlot>();
            currentData.safePocket ??= new SafePocketData();
            currentData.safePocket.slots ??= new List<SafePocketSlot>();
            currentData.equipment ??= new EquipmentData();
            currentData.facilities ??= new FacilitiesData();
            currentData.insurance ??= new List<InsuranceEntry>();
        }

        private void EnsureLobbySaveContainers()
        {
            if (currentData == null)
                return;

            currentData.lobbySave ??= new LobbySaveCloudData();
            currentData.lobbySave.inventoryItems ??= new List<LobbyItemCloudData>();
            currentData.lobbySave.stashItems ??= new List<LobbyItemCloudData>();
            currentData.lobbySave.equipmentItems ??= new List<LobbyEquipmentCloudData>();
            currentData.lobbySave.quickSlotItems ??= new List<LobbyItemCloudData>();
            currentData.lobbySave.facilities ??= new List<LobbyFacilityCloudData>();
        }

        private void LogSaveAudit(string action, string extra = null)
        {
            if (!logSaveAudit)
                return;

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string uid = string.IsNullOrWhiteSpace(loadedFirebaseUid) ? "(none)" : loadedFirebaseUid;
            string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $" {extra}";

            Debug.Log(
                $"[CloudSaveSystem] Audit {action} scene={sceneName} uid={uid} {BuildSaveAuditSummary()}{suffix}",
                this);
        }

        private string BuildSaveAuditSummary()
        {
            if (currentData == null)
                return "data=null";

            int credits = currentData.progress != null ? currentData.progress.credits : -1;
            int legacyStashCount = currentData.stash?.slots?.Count ?? -1;
            int lobbyInventoryCount = currentData.lobbySave?.inventoryItems?.Count ?? -1;
            int lobbyStashCount = currentData.lobbySave?.stashItems?.Count ?? -1;
            int lobbyEquipmentCount = currentData.lobbySave?.equipmentItems?.Count ?? -1;
            int lobbyQuickSlotCount = currentData.lobbySave?.quickSlotItems?.Count ?? -1;
            int lobbyFacilityCount = currentData.lobbySave?.facilities?.Count ?? -1;

            return
                $"credits={credits} legacyStash={legacyStashCount} lobbyInventory={lobbyInventoryCount} " +
                $"lobbyStash={lobbyStashCount} lobbyEquipment={lobbyEquipmentCount} lobbyQuickSlots={lobbyQuickSlotCount} lobbyFacilities={lobbyFacilityCount} " +
                BuildFacilityAuditSummary();
        }

        private string BuildFacilityAuditSummary()
        {
            FacilitiesData facilities = currentData?.facilities;
            if (facilities == null)
                return "facilities=null";

            return
                $"facilities=Workbench:{facilities.workbench},CommStation:{facilities.commStation},Medical:{facilities.medical}," +
                $"Gym:{facilities.gym},Stash:{facilities.stash},Kitchen:{facilities.kitchen},Bed:{facilities.bed}";
        }

        private static string BuildLobbyDtoAuditSummary(LobbySaveDTO dto)
        {
            if (dto == null)
                return "dto=null";

            string credits = dto.hasCredits ? dto.credits.ToString() : "none";
            return
                $"dtoCredits={credits} dtoInventory={dto.inventoryItems?.Count ?? -1}(known={dto.hasInventorySection}) " +
                $"dtoStash={dto.stashItems?.Count ?? -1}(known={dto.hasStashSection}) " +
                $"dtoEquipment={dto.equipmentItems?.Count ?? -1}(known={dto.hasEquipmentSection}) " +
                $"dtoQuickSlots={dto.quickSlotItems?.Count ?? -1}(known={dto.hasQuickSlotSection}) " +
                $"dtoFacilities={dto.facilities?.Count ?? -1}";
        }

        private static string BuildHousingDtoAuditSummary(PlayerHousingProgressDTO dto)
        {
            if (dto == null)
                return "housingDto=null";

            return
                $"housingDto=Workbench:{dto.workbenchLevel},CommStation:{dto.commStationLevel},Medical:{dto.medicalLevel}," +
                $"Gym:{dto.gymLevel},Stash:{dto.stashLevel},Kitchen:{dto.kitchenLevel},Bed:{dto.bedLevel}";
        }
        
        // =================================================================
        // 씬에서 데이터 수집 (Part VII §7.7 - Pull 방식)
        // =================================================================

        private void CollectDataFromScene()
        {
            if (currentData == null) return;

            EnsureCloudDataContainers();

            // 1) 본인 PlayerPrefab 찾기 (있을 때만)
            var localPlayer = FindLocalPlayer();
            if (localPlayer != null)
            {
                CollectWallet(localPlayer);
                CollectEquipment(localPlayer);
            }

            // 2) 하우징 레벨 (Hideout 씬에서만 Facility 인스턴스 존재)
            CollectFacilities();

            // 3) 퀘스트 진행도 (QuestManager는 PersistentSystems에 있어 항상 접근 가능)
            CollectPersonalQuestProgress();
        }

        private NetworkObject FindLocalPlayer()
        {
            if (NetworkManager.Singleton == null) return null;
            var local = NetworkManager.Singleton.LocalClient;
            return local?.PlayerObject;
        }

        private void CollectWallet(NetworkObject player)
        {
            var wallet = player.GetComponent<WalletSystem>();
            if (wallet != null)
            {
                currentData.progress.credits = wallet.CurrentCredits;
            }
        }

        private void CollectEquipment(NetworkObject player)
        {
            var eq = player.GetComponent<EquipmentSlots>();
            if (eq == null) return;

            currentData.equipment.helmetId = eq.HeadSlotId.Value.ToString();
            currentData.equipment.armorId = eq.TorsoSlotId.Value.ToString();
            currentData.equipment.primary1 = eq.Primary1Id.Value.ToString();
            currentData.equipment.primary2 = eq.Primary2Id.Value.ToString();
            currentData.equipment.secondary = eq.SecondaryId.Value.ToString();
            currentData.equipment.melee = eq.MeleeId.Value.ToString();
            currentData.equipment.helmetDurability = eq.HelmetDurability.Value;
            currentData.equipment.armorDurability = eq.ArmorDurability.Value;
        }

        private void CollectFacilities()
        {
            if (TryCollectPlayerHousingProgress())
                return;

            if (TryCollectLobbyFacilityState())
            {
                MirrorFacilitiesToLobbySave();
                return;
            }

            if (TryCollectCachedLobbyFacilities())
            {
                MirrorFacilitiesToLobbySave();
                return;
            }

            // FacilityBase는 Hideout 씬에만 존재. 다른 씬이면 찾기 못하고 기존 값 유지됨.
            var facilities = FindObjectsByType<FacilityBase>(FindObjectsSortMode.None);
            if (facilities == null || facilities.Length == 0)
            {
                if (currentData?.facilities != null)
                    MirrorFacilitiesToLobbySave();

                return;
            }

            foreach (var f in facilities)
            {
                int level = f.GetCurrentLevel();

                switch (f.Type)
                {
                    case FacilityType.Workbench: currentData.facilities.workbench = Mathf.Max(currentData.facilities.workbench, level); break;
                    case FacilityType.CommStation: currentData.facilities.commStation = Mathf.Max(currentData.facilities.commStation, level); break;
                    case FacilityType.Medical: currentData.facilities.medical = Mathf.Max(currentData.facilities.medical, level); break;
                    case FacilityType.Gym: currentData.facilities.gym = Mathf.Max(currentData.facilities.gym, level); break;
                    case FacilityType.Stash: currentData.facilities.stash = Mathf.Max(currentData.facilities.stash, level); break;
                    case FacilityType.Kitchen: currentData.facilities.kitchen = Mathf.Max(currentData.facilities.kitchen, level); break;
                    case FacilityType.Bed: currentData.facilities.bed = Mathf.Max(currentData.facilities.bed, level); break;
                }
            }

            MirrorFacilitiesToLobbySave();
        }

        private bool TryCollectPlayerHousingProgress()
        {
            NetworkObject localPlayer = FindLocalPlayer();

            if (localPlayer == null)
                return false;

            PlayerHousingProgress progress = localPlayer.GetComponent<PlayerHousingProgress>();

            if (progress == null)
                progress = localPlayer.GetComponentInChildren<PlayerHousingProgress>(true);

            if (progress == null)
                return false;

            PlayerHousingProgressDTO dto = progress.ToSaveData();
            dto.Normalize();

            if (IsDefaultHousingProgress(dto) && HasNonDefaultHousingProgress())
            {
                Debug.LogWarning("[Save] Save skipped because load is not completed yet. Default PlayerHousingProgress would overwrite existing housing save.");
                return false;
            }

            return TryApplyHousingProgressToCloudFields(dto);
        }

        private bool TryCollectLobbyFacilityState()
        {
            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null || facilityState.Facilities == null || facilityState.Facilities.Count == 0)
                return false;

            EnsureLobbySaveContainers();

            for (int i = 0; i < facilityState.Facilities.Count; i++)
            {
                FacilitySaveDTO facility = facilityState.Facilities[i];

                if (facility == null || string.IsNullOrWhiteSpace(facility.facilityId))
                    continue;

                ApplyFacilityToLegacyField(facility);
                UpsertLobbyFacility(facility.facilityId, facility.level);
            }

            return true;
        }

        private bool TryCollectCachedLobbyFacilities()
        {
            if (currentData?.lobbySave?.facilities == null || currentData.lobbySave.facilities.Count == 0)
                return false;

            for (int i = 0; i < currentData.lobbySave.facilities.Count; i++)
            {
                LobbyFacilityCloudData facility = currentData.lobbySave.facilities[i];

                if (facility == null || string.IsNullOrWhiteSpace(facility.facilityId))
                    continue;

                ApplyLobbyFacilityCloudToLegacyField(facility);
            }

            return true;
        }

        private void CollectPersonalQuestProgress()
        {
            var quest = ServiceLocator.Get<QuestManager>();
            if (quest == null) return;

            ulong localClientId = quest.GetLocalClientIdForState();
            var myState = quest.GetPlayerState(localClientId);
            if (myState == null) return;

            myState.WriteToCloudProgress(currentData.progress);
        }

        private void RestoreQuestStateIfAvailable()
        {
            if (currentData?.progress == null)
                return;

            QuestManager quest = ServiceLocator.Get<QuestManager>();
            if (quest == null)
                return;

            quest.RestorePlayerState(quest.GetLocalClientIdForState(), currentData.progress);
        }

        private void ApplyBankruptcyStarterPack(StarterPackConfigSO starterPack)
        {
            currentData.progress.credits = starterPack.StartingCredits;
            currentData.stash.slots = BuildStarterPackStashSlots(starterPack);
            currentData.facilities ??= new FacilitiesData();
            currentData.facilities.stash = 1;
            ApplyBankruptcyStashLevelToRuntimeState();
            MirrorFacilitiesToLobbySave();
            currentData.safePocket.slots.Clear();
            currentData.equipment = new EquipmentData();
            currentData.insurance.Clear();
            currentData.schemaVersion = Mathf.Max(currentData.schemaVersion, EconomyStarterPackSchemaVersion);
        }

        private void ApplyBankruptcyStashLevelToRuntimeState()
        {
            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);
            facilityState?.SetFacilityLevel("Stash", 1);

            NetworkObject localPlayer = FindLocalPlayer();
            PlayerHousingProgress housingProgress = localPlayer != null
                ? localPlayer.GetComponent<PlayerHousingProgress>()
                : null;

            if (housingProgress == null && localPlayer != null)
                housingProgress = localPlayer.GetComponentInChildren<PlayerHousingProgress>(true);

            if (housingProgress != null && housingProgress.IsServer)
                housingProgress.TrySetLevelFromServer(FacilityType.Stash, 1);

            DeadZone.Actors.UI.StashGridUI stashGridUI = FindFirstObjectByType<DeadZone.Actors.UI.StashGridUI>(FindObjectsInactive.Include);
            if (stashGridUI != null)
                stashGridUI.SetLevel(1);
        }

        private static void ApplyBankruptcyLobbyStateToRuntime(LobbySaveDTO bankruptcyLobbySave)
        {
            if (bankruptcyLobbySave == null)
                return;

            LobbyInventoryState inventoryState = FindFirstObjectByType<LobbyInventoryState>(FindObjectsInactive.Include);
            if (inventoryState != null)
            {
                inventoryState.SetCredits(bankruptcyLobbySave.credits);
                inventoryState.SetInventoryItems(bankruptcyLobbySave.inventoryItems);
                inventoryState.SetStashItems(bankruptcyLobbySave.stashItems);
                inventoryState.SetQuickSlotItems(bankruptcyLobbySave.quickSlotItems);
                inventoryState.SetEquipmentItems(bankruptcyLobbySave.equipmentItems);
            }

            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);
            if (facilityState != null)
                facilityState.SetFacilities(bankruptcyLobbySave.facilities);

            LobbyInventoryStateUiBridge uiBridge = FindFirstObjectByType<LobbyInventoryStateUiBridge>(FindObjectsInactive.Include);
            if (uiBridge != null)
                uiBridge.ApplyStateToUi();

            WalletSystem walletSystem = FindFirstObjectByType<WalletSystem>(FindObjectsInactive.Include);
            if (walletSystem != null)
                walletSystem.SetCreditsLocalTest(bankruptcyLobbySave.credits);

            DeadZone.Actors.UI.StashGridUI stashGridUI = FindFirstObjectByType<DeadZone.Actors.UI.StashGridUI>(FindObjectsInactive.Include);
            if (stashGridUI != null)
            {
                stashGridUI.SetLevel(1);
                stashGridUI.ApplySavedStashItems(bankruptcyLobbySave.stashItems, ServiceLocator.Get<IItemDatabase>());
            }
        }

        private bool TryApplyStarterPackMigration()
        {
            if (bankruptcyStarterPack == null)
                return false;

            if (currentData.schemaVersion >= EconomyStarterPackSchemaVersion)
                return false;

            if (!IsEconomyEmptyForStarterPackMigration())
            {
                currentData.schemaVersion = EconomyStarterPackSchemaVersion;
                return false;
            }

            ApplyBankruptcyStarterPack(bankruptcyStarterPack);
            return true;
        }

        private bool IsEconomyEmptyForStarterPackMigration()
        {
            bool stashEmpty = currentData.stash == null ||
                              currentData.stash.slots == null ||
                              currentData.stash.slots.Count == 0;

            bool safePocketEmpty = currentData.safePocket == null ||
                                   currentData.safePocket.slots == null ||
                                   currentData.safePocket.slots.Count == 0;

            bool insuranceEmpty = currentData.insurance == null ||
                                  currentData.insurance.Count == 0;

            bool equipmentEmpty = currentData.equipment == null ||
                                  (string.IsNullOrWhiteSpace(currentData.equipment.helmetId) &&
                                   string.IsNullOrWhiteSpace(currentData.equipment.armorId) &&
                                   string.IsNullOrWhiteSpace(currentData.equipment.primary1) &&
                                   string.IsNullOrWhiteSpace(currentData.equipment.primary2) &&
                                   string.IsNullOrWhiteSpace(currentData.equipment.secondary) &&
                                   string.IsNullOrWhiteSpace(currentData.equipment.melee));

            return stashEmpty && safePocketEmpty && insuranceEmpty && equipmentEmpty;
        }

        private static List<StashSlot> BuildStarterPackStashSlots(StarterPackConfigSO starterPack)
        {
            List<StashSlot> result = new List<StashSlot>();
            List<RectInt> occupiedRects = new List<RectInt>();

            IReadOnlyList<StarterPackEntry> entries = starterPack.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AppendStarterPackEntry(result, occupiedRects, entries[i]);
            }

            return result;
        }

        private static void AppendStarterPackEntry(
            List<StashSlot> result,
            List<RectInt> occupiedRects,
            StarterPackEntry entry)
        {
            if (entry == null || entry.Item == null)
                return;

            int remaining = entry.Amount;
            int maxStack = Mathf.Max(1, entry.Item.maxStackSize);
            Vector2Int itemSize = Vector2Int.one;

            while (remaining > 0)
            {
                int stackCount = Mathf.Min(maxStack, remaining);

                if (!TryFindStarterPackPlacement(occupiedRects, itemSize, out int gridX, out int gridY))
                {
                    Debug.LogWarning($"[CloudSaveSystem] Starter pack item placement failed. itemId={entry.Item.itemID}");
                    return;
                }

                result.Add(CreateStarterPackSlot(entry, stackCount, gridX, gridY));
                occupiedRects.Add(new RectInt(gridX, gridY, itemSize.x, itemSize.y));
                remaining -= stackCount;
            }
        }

        private static StashSlot CreateStarterPackSlot(StarterPackEntry entry, int stackCount, int gridX, int gridY)
        {
            ItemDataSO item = entry.Item;

            return new StashSlot
            {
                itemId = item.itemID,
                stackCount = stackCount,
                gridX = gridX,
                gridY = gridY,
                rotated = false,
                currentDurability = GetDurabilityValue(item, entry.DurabilityRatio),
                currentAmmo = GetCurrentAmmoValue(item, entry.CurrentAmmo),
            };
        }

        private static bool TryFindStarterPackPlacement(
            List<RectInt> occupiedRects,
            Vector2Int itemSize,
            out int gridX,
            out int gridY)
        {
            gridX = 0;
            gridY = 0;

            const int maxStarterPackRows = 100;

            for (int y = 0; y < maxStarterPackRows; y++)
            {
                for (int x = 0; x <= DefaultStashColumnCount - itemSize.x; x++)
                {
                    if (!OverlapsAnyStarterPackSlot(occupiedRects, x, y, itemSize))
                    {
                        gridX = x;
                        gridY = y;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool OverlapsAnyStarterPackSlot(
            List<RectInt> occupiedRects,
            int x,
            int y,
            Vector2Int size)
        {
            RectInt candidate = new RectInt(x, y, size.x, size.y);

            for (int i = 0; i < occupiedRects.Count; i++)
            {
                if (candidate.Overlaps(occupiedRects[i]))
                    return true;
            }

            return false;
        }

        private static Vector2Int GetSafeGridSize(ItemDataSO item)
        {
            if (item == null)
                return Vector2Int.one;

            return new Vector2Int(
                Mathf.Clamp(item.gridSize.x, 1, DefaultStashColumnCount),
                Mathf.Max(1, item.gridSize.y));
        }

        private static int GetDurabilityValue(ItemDataSO item, float durabilityRatio)
        {
            float maxDurability = item switch
            {
                WeaponDataSO weapon => weapon.maxDurability,
                ArmorDataSO armor => armor.maxDurability,
                HelmetDataSO helmet => helmet.maxDurability,
                _ => 0f,
            };

            if (maxDurability <= 0f)
                return 0;

            return Mathf.RoundToInt(maxDurability * Mathf.Clamp01(durabilityRatio));
        }

        private static int GetCurrentAmmoValue(ItemDataSO item, int currentAmmo)
        {
            return item is WeaponDataSO ? Mathf.Max(0, currentAmmo) : 0;
        }

        // =================================================================
        // 신규 유저 기본값
        // =================================================================

        private PlayerCloudData NewPlayerData(string uid, string email)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new PlayerCloudData();
            data.profile.email = email ?? "";
            data.profile.createdAtUnix = now;
            data.profile.lastPlayedAtUnix = now;
            data.profile.totalPlayTimeSec = 0;
            data.progress.credits = bankruptcyStarterPack != null
                ? bankruptcyStarterPack.StartingCredits
                : DefaultStartingCredits;

            if (bankruptcyStarterPack != null)
                data.stash.slots = BuildStarterPackStashSlots(bankruptcyStarterPack);

            data.schemaVersion = EconomyStarterPackSchemaVersion;

            // facilities 는 생성자에서 모두 Lv1 기본값
            return data;
        }

        private static LobbySaveCloudData ToLobbySaveCloudData(LobbySaveDTO dto)
        {
            LobbySaveCloudData cloudData = new LobbySaveCloudData();
            cloudData.hasCredits = dto.hasCredits;
            cloudData.credits = dto.credits;
            cloudData.hasInventorySection = dto.hasInventorySection || dto.inventoryItems?.Count > 0;
            cloudData.hasStashSection = dto.hasStashSection || dto.stashItems?.Count > 0;
            cloudData.hasEquipmentSection = dto.hasEquipmentSection || dto.equipmentItems?.Count > 0;
            cloudData.hasQuickSlotSection = dto.hasQuickSlotSection || dto.quickSlotItems?.Count > 0;

            if (dto.inventoryItems != null)
            {
                for (int i = 0; i < dto.inventoryItems.Count; i++)
                    cloudData.inventoryItems.Add(ToCloudItem(dto.inventoryItems[i]));
            }

            if (dto.stashItems != null)
            {
                for (int i = 0; i < dto.stashItems.Count; i++)
                    cloudData.stashItems.Add(ToCloudItem(dto.stashItems[i]));
            }

            if (dto.equipmentItems != null)
            {
                for (int i = 0; i < dto.equipmentItems.Count; i++)
                    cloudData.equipmentItems.Add(ToCloudEquipment(dto.equipmentItems[i]));
            }

            if (dto.quickSlotItems != null)
            {
                for (int i = 0; i < dto.quickSlotItems.Count; i++)
                    cloudData.quickSlotItems.Add(ToCloudItem(dto.quickSlotItems[i]));
            }

            if (dto.facilities != null)
            {
                for (int i = 0; i < dto.facilities.Count; i++)
                    cloudData.facilities.Add(ToCloudFacility(dto.facilities[i]));
            }

            return cloudData;
        }

        private void PreserveExistingLobbySectionsForEmptyInput(LobbySaveCloudData nextLobbySave)
        {
            if (nextLobbySave == null || currentData?.lobbySave == null)
                return;

            LobbySaveCloudData currentLobbySave = currentData.lobbySave;

            if (!nextLobbySave.hasCredits && currentLobbySave.hasCredits)
            {
                nextLobbySave.hasCredits = true;
                nextLobbySave.credits = currentLobbySave.credits;
            }

            if (!nextLobbySave.hasInventorySection
                && (nextLobbySave.inventoryItems == null || nextLobbySave.inventoryItems.Count == 0)
                && currentLobbySave.inventoryItems != null
                && currentLobbySave.inventoryItems.Count > 0)
            {
                nextLobbySave.hasInventorySection = true;
                nextLobbySave.inventoryItems = CloneLobbyItems(currentLobbySave.inventoryItems);
            }

            if (!nextLobbySave.hasStashSection
                && (nextLobbySave.stashItems == null || nextLobbySave.stashItems.Count == 0)
                && currentLobbySave.stashItems != null
                && currentLobbySave.stashItems.Count > 0)
            {
                nextLobbySave.hasStashSection = true;
                nextLobbySave.stashItems = CloneLobbyItems(currentLobbySave.stashItems);
            }

            if (!nextLobbySave.hasEquipmentSection
                && (nextLobbySave.equipmentItems == null || nextLobbySave.equipmentItems.Count == 0)
                && currentLobbySave.equipmentItems != null
                && currentLobbySave.equipmentItems.Count > 0)
            {
                nextLobbySave.hasEquipmentSection = true;
                nextLobbySave.equipmentItems = CloneLobbyEquipment(currentLobbySave.equipmentItems);
            }

            if (!nextLobbySave.hasQuickSlotSection
                && (nextLobbySave.quickSlotItems == null || nextLobbySave.quickSlotItems.Count == 0)
                && currentLobbySave.quickSlotItems != null
                && currentLobbySave.quickSlotItems.Count > 0)
            {
                nextLobbySave.hasQuickSlotSection = true;
                nextLobbySave.quickSlotItems = CloneLobbyItems(currentLobbySave.quickSlotItems);
            }
        }

        private static List<LobbyItemCloudData> CloneLobbyItems(List<LobbyItemCloudData> source)
        {
            List<LobbyItemCloudData> clone = new();

            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
            {
                LobbyItemCloudData item = source[i];
                if (item == null)
                    continue;

                clone.Add(new LobbyItemCloudData
                {
                    itemId = item.itemId ?? "",
                    instanceId = item.instanceId ?? "",
                    containerId = item.containerId ?? "",
                    x = item.x,
                    y = item.y,
                    rotated = item.rotated,
                    stackCount = item.stackCount,
                    currentDurability = item.currentDurability,
                    currentAmmo = item.currentAmmo
                });
            }

            return clone;
        }

        private static List<LobbyEquipmentCloudData> CloneLobbyEquipment(List<LobbyEquipmentCloudData> source)
        {
            List<LobbyEquipmentCloudData> clone = new();

            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
            {
                LobbyEquipmentCloudData equipment = source[i];
                if (equipment == null)
                    continue;

                clone.Add(new LobbyEquipmentCloudData
                {
                    slotId = equipment.slotId ?? "",
                    itemId = equipment.itemId ?? "",
                    instanceId = equipment.instanceId ?? "",
                    loadedAmmoId = equipment.loadedAmmoId ?? "",
                    currentAmmo = equipment.currentAmmo,
                    durability = equipment.durability
                });
            }

            return clone;
        }

        private static LobbySaveDTO ToLobbySaveDTO(LobbySaveCloudData cloudData)
        {
            LobbySaveDTO dto = new LobbySaveDTO();
            dto.hasCredits = cloudData.hasCredits;
            dto.credits = cloudData.credits;
            dto.hasInventorySection = cloudData.hasInventorySection || HasItems(cloudData.inventoryItems);
            dto.hasStashSection = cloudData.hasStashSection || HasItems(cloudData.stashItems);
            dto.hasEquipmentSection = cloudData.hasEquipmentSection || HasItems(cloudData.equipmentItems);
            dto.hasQuickSlotSection = cloudData.hasQuickSlotSection || HasItems(cloudData.quickSlotItems);

            if (cloudData.inventoryItems != null)
            {
                for (int i = 0; i < cloudData.inventoryItems.Count; i++)
                    dto.inventoryItems.Add(ToItemSaveDTO(cloudData.inventoryItems[i]));
            }

            if (cloudData.stashItems != null)
            {
                for (int i = 0; i < cloudData.stashItems.Count; i++)
                    dto.stashItems.Add(ToItemSaveDTO(cloudData.stashItems[i]));
            }

            if (cloudData.equipmentItems != null)
            {
                for (int i = 0; i < cloudData.equipmentItems.Count; i++)
                    dto.equipmentItems.Add(ToEquipmentSaveDTO(cloudData.equipmentItems[i]));
            }

            if (cloudData.quickSlotItems != null)
            {
                for (int i = 0; i < cloudData.quickSlotItems.Count; i++)
                    dto.quickSlotItems.Add(ToItemSaveDTO(cloudData.quickSlotItems[i]));
            }

            if (cloudData.facilities != null)
            {
                for (int i = 0; i < cloudData.facilities.Count; i++)
                    dto.facilities.Add(ToFacilitySaveDTO(cloudData.facilities[i]));
            }

            return dto;
        }

        private static LobbyItemCloudData ToCloudItem(ItemSaveDTO item)
        {
            if (item == null)
                return new LobbyItemCloudData();

            return new LobbyItemCloudData
            {
                itemId = item.itemId ?? "",
                instanceId = item.instanceId ?? "",
                containerId = item.containerId ?? "",
                x = item.x,
                y = item.y,
                rotated = item.rotated,
                stackCount = item.stackCount,
                currentDurability = item.currentDurability,
                currentAmmo = item.currentAmmo
            };
        }

        private static LobbyEquipmentCloudData ToCloudEquipment(EquipmentSaveDTO equipment)
        {
            if (equipment == null)
                return new LobbyEquipmentCloudData();

            return new LobbyEquipmentCloudData
            {
                slotId = equipment.slotId ?? "",
                itemId = equipment.itemId ?? "",
                instanceId = equipment.instanceId ?? "",
                loadedAmmoId = equipment.loadedAmmoId ?? "",
                currentAmmo = equipment.currentAmmo,
                durability = equipment.durability
            };
        }

        private static LobbyFacilityCloudData ToCloudFacility(FacilitySaveDTO facility)
        {
            if (facility == null)
                return new LobbyFacilityCloudData();

            return new LobbyFacilityCloudData
            {
                facilityId = facility.facilityId ?? "",
                level = facility.level
            };
        }

        private static ItemSaveDTO ToItemSaveDTO(LobbyItemCloudData item)
        {
            if (item == null)
                return new ItemSaveDTO();

            return new ItemSaveDTO
            {
                itemId = item.itemId ?? "",
                instanceId = item.instanceId ?? "",
                containerId = item.containerId ?? "",
                x = item.x,
                y = item.y,
                rotated = item.rotated,
                stackCount = item.stackCount,
                currentDurability = item.currentDurability,
                currentAmmo = item.currentAmmo
            };
        }

        private static EquipmentSaveDTO ToEquipmentSaveDTO(LobbyEquipmentCloudData equipment)
        {
            if (equipment == null)
                return new EquipmentSaveDTO();

            return new EquipmentSaveDTO
            {
                slotId = equipment.slotId ?? "",
                itemId = equipment.itemId ?? "",
                instanceId = equipment.instanceId ?? "",
                loadedAmmoId = equipment.loadedAmmoId ?? "",
                currentAmmo = equipment.currentAmmo,
                durability = equipment.durability
            };
        }

        private static FacilitySaveDTO ToFacilitySaveDTO(LobbyFacilityCloudData facility)
        {
            if (facility == null)
                return new FacilitySaveDTO();

            return new FacilitySaveDTO
            {
                facilityId = facility.facilityId ?? "",
                level = facility.level
            };
        }

        private static bool HasLobbySaveData(LobbySaveCloudData lobbySave)
        {
            if (lobbySave == null)
                return false;

            return lobbySave.hasCredits ||
                   lobbySave.hasInventorySection ||
                   lobbySave.hasStashSection ||
                   lobbySave.hasEquipmentSection ||
                   lobbySave.hasQuickSlotSection ||
                   HasItems(lobbySave.inventoryItems) ||
                   HasItems(lobbySave.stashItems) ||
                   HasItems(lobbySave.equipmentItems) ||
                   HasItems(lobbySave.quickSlotItems) ||
                   HasItems(lobbySave.facilities);
        }

        private static bool HasItems<T>(List<T> list)
        {
            return list != null && list.Count > 0;
        }

        private void ApplyLobbySaveToLegacyCloudFields(LobbySaveDTO lobbySaveDto)
        {
            if (lobbySaveDto.hasStashSection || lobbySaveDto.stashItems?.Count > 0)
            {
                currentData.stash.slots.Clear();

                for (int i = 0; lobbySaveDto.stashItems != null && i < lobbySaveDto.stashItems.Count; i++)
                {
                    ItemSaveDTO item = lobbySaveDto.stashItems[i];
                    if (item == null)
                        continue;

                    currentData.stash.slots.Add(new StashSlot
                    {
                        itemId = item.itemId ?? "",
                        stackCount = item.stackCount,
                        gridX = GetStashGridX(item),
                        gridY = GetStashGridY(item),
                        rotated = item.rotated,
                        currentDurability = Mathf.RoundToInt(Mathf.Max(0f, item.currentDurability)),
                        currentAmmo = Mathf.Max(0, item.currentAmmo),
                    });
                }
            }

            if (lobbySaveDto.hasEquipmentSection || lobbySaveDto.equipmentItems?.Count > 0)
            {
                currentData.equipment = new EquipmentData();

                for (int i = 0; lobbySaveDto.equipmentItems != null && i < lobbySaveDto.equipmentItems.Count; i++)
                    ApplyEquipmentToLegacyField(lobbySaveDto.equipmentItems[i]);
            }

            MirrorFacilitiesToLobbySave();
        }

        private void ApplyEquipmentToLegacyField(EquipmentSaveDTO equipmentItem)
        {
            if (equipmentItem == null)
                return;

            switch (equipmentItem.slotId)
            {
                case "EquipmentHead":
                case "head":
                    currentData.equipment.helmetId = equipmentItem.itemId ?? "";
                    currentData.equipment.helmetDurability = equipmentItem.durability;
                    break;
                case "EquipmentArmor":
                case "torso":
                    currentData.equipment.armorId = equipmentItem.itemId ?? "";
                    currentData.equipment.armorDurability = equipmentItem.durability;
                    break;
                case "EquipmentPrimaryWeapon":
                case "primary1":
                    if (string.IsNullOrWhiteSpace(currentData.equipment.primary1))
                        currentData.equipment.primary1 = equipmentItem.itemId ?? "";
                    else
                        currentData.equipment.primary2 = equipmentItem.itemId ?? "";
                    break;
                case "primary2":
                    currentData.equipment.primary2 = equipmentItem.itemId ?? "";
                    break;
                case "EquipmentSecondaryWeapon":
                case "secondary":
                    currentData.equipment.secondary = equipmentItem.itemId ?? "";
                    break;
                case "EquipmentMeleeWeapon":
                case "melee":
                    currentData.equipment.melee = equipmentItem.itemId ?? "";
                    break;
            }
        }

        private void ApplyFacilityToLegacyField(FacilitySaveDTO facility)
        {
            if (facility == null)
                return;

            switch (NormalizeFacilityId(facility.facilityId))
            {
                case "workbench": currentData.facilities.workbench = Mathf.Max(currentData.facilities.workbench, facility.level); break;
                case "commstation": currentData.facilities.commStation = Mathf.Max(currentData.facilities.commStation, facility.level); break;
                case "medical": currentData.facilities.medical = Mathf.Max(currentData.facilities.medical, facility.level); break;
                case "gym": currentData.facilities.gym = Mathf.Max(currentData.facilities.gym, facility.level); break;
                case "stash": currentData.facilities.stash = Mathf.Max(currentData.facilities.stash, facility.level); break;
                case "kitchen": currentData.facilities.kitchen = Mathf.Max(currentData.facilities.kitchen, facility.level); break;
                case "bed": currentData.facilities.bed = Mathf.Max(currentData.facilities.bed, facility.level); break;
            }
        }

        private void ApplyLobbyFacilityCloudToLegacyField(LobbyFacilityCloudData facility)
        {
            if (facility == null)
                return;

            switch (NormalizeFacilityId(facility.facilityId))
            {
                case "workbench": currentData.facilities.workbench = Mathf.Max(currentData.facilities.workbench, facility.level); break;
                case "commstation": currentData.facilities.commStation = Mathf.Max(currentData.facilities.commStation, facility.level); break;
                case "medical": currentData.facilities.medical = Mathf.Max(currentData.facilities.medical, facility.level); break;
                case "gym": currentData.facilities.gym = Mathf.Max(currentData.facilities.gym, facility.level); break;
                case "stash": currentData.facilities.stash = Mathf.Max(currentData.facilities.stash, facility.level); break;
                case "kitchen": currentData.facilities.kitchen = Mathf.Max(currentData.facilities.kitchen, facility.level); break;
                case "bed": currentData.facilities.bed = Mathf.Max(currentData.facilities.bed, facility.level); break;
            }
        }

        private bool TryApplyHousingProgressToCloudFields(PlayerHousingProgressDTO dto, bool allowDefaultOverwrite = false)
        {
            if (dto == null)
                return false;

            dto.Normalize();

            if (!allowDefaultOverwrite && IsDefaultHousingProgress(dto) && HasNonDefaultHousingProgress())
            {
                Debug.LogWarning("[Party] WARNING: Save data reset attempted during party creation. caller=CloudSaveSystem.TryApplyHousingProgressToCloudFields");
                Debug.LogWarning("[Save] Save skipped because load is not completed yet.");
                return false;
            }

            currentData.facilities.workbench = dto.workbenchLevel;
            currentData.facilities.medical = dto.medicalLevel;
            currentData.facilities.gym = dto.gymLevel;
            currentData.facilities.stash = dto.stashLevel;
            currentData.facilities.kitchen = dto.kitchenLevel;
            currentData.facilities.bed = dto.bedLevel;
            currentData.facilities.commStation = dto.commStationLevel;

            MirrorFacilitiesToLobbySave();
            return true;
        }

        private bool HasNonDefaultHousingProgress()
        {
            if (currentData == null)
                return false;

            if (currentData.facilities != null &&
                (currentData.facilities.workbench > 1 ||
                 currentData.facilities.medical > 1 ||
                 currentData.facilities.gym > 1 ||
                 currentData.facilities.stash > 1 ||
                 currentData.facilities.kitchen > 1 ||
                 currentData.facilities.bed > 1 ||
                 currentData.facilities.commStation > 1))
            {
                return true;
            }

            if (currentData.lobbySave?.facilities == null)
                return false;

            for (int i = 0; i < currentData.lobbySave.facilities.Count; i++)
            {
                LobbyFacilityCloudData facility = currentData.lobbySave.facilities[i];

                if (facility != null && facility.level > 1)
                    return true;
            }

            return false;
        }

        private static bool IsDefaultHousingProgress(PlayerHousingProgressDTO dto)
        {
            if (dto == null)
                return true;

            return dto.workbenchLevel <= 1 &&
                   dto.medicalLevel <= 1 &&
                   dto.gymLevel <= 1 &&
                   dto.stashLevel <= 1 &&
                   dto.kitchenLevel <= 1 &&
                   dto.bedLevel <= 1 &&
                   dto.commStationLevel <= 1;
        }

        private void ApplyFacilityToHousingProgressDTO(PlayerHousingProgressDTO dto, LobbyFacilityCloudData facility)
        {
            if (dto == null || facility == null)
                return;

            int safeLevel = Mathf.Max(1, facility.level);

            switch (NormalizeFacilityId(facility.facilityId))
            {
                case "workbench": dto.workbenchLevel = Mathf.Max(dto.workbenchLevel, safeLevel); break;
                case "commstation": dto.commStationLevel = Mathf.Max(dto.commStationLevel, safeLevel); break;
                case "medical": dto.medicalLevel = Mathf.Max(dto.medicalLevel, safeLevel); break;
                case "gym": dto.gymLevel = Mathf.Max(dto.gymLevel, safeLevel); break;
                case "stash": dto.stashLevel = Mathf.Max(dto.stashLevel, safeLevel); break;
                case "kitchen": dto.kitchenLevel = Mathf.Max(dto.kitchenLevel, safeLevel); break;
                case "bed": dto.bedLevel = Mathf.Max(dto.bedLevel, safeLevel); break;
            }
        }

        private void UpsertLobbyFacility(string facilityId, int level, bool allowDowngrade = false)
        {
            if (string.IsNullOrWhiteSpace(facilityId))
                return;

            EnsureLobbySaveContainers();

            string normalizedId = NormalizeFacilityId(facilityId);
            int safeLevel = Mathf.Max(1, level);

            for (int i = 0; i < currentData.lobbySave.facilities.Count; i++)
            {
                LobbyFacilityCloudData facility = currentData.lobbySave.facilities[i];

                if (facility == null)
                    continue;

                if (NormalizeFacilityId(facility.facilityId) != normalizedId)
                    continue;

                facility.facilityId = facilityId;
                facility.level = allowDowngrade ? safeLevel : Mathf.Max(Mathf.Max(1, facility.level), safeLevel);
                return;
            }

            currentData.lobbySave.facilities.Add(new LobbyFacilityCloudData
            {
                facilityId = facilityId,
                level = safeLevel
            });
        }

        private static string NormalizeFacilityId(string facilityId)
        {
            return string.IsNullOrWhiteSpace(facilityId)
                ? string.Empty
                : facilityId.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

        private bool NormalizeFacilityProgressFromLegacyFields()
        {
            if (currentData?.facilities == null)
                return false;

            bool changed = false;

            if (currentData.lobbySave?.facilities != null)
            {
                for (int i = 0; i < currentData.lobbySave.facilities.Count; i++)
                {
                    LobbyFacilityCloudData facility = currentData.lobbySave.facilities[i];
                    if (facility == null || string.IsNullOrWhiteSpace(facility.facilityId))
                        continue;

                    changed |= MergeFacilityIntoAuthoritativeProgress(facility.facilityId, facility.level);
                }
            }

            MirrorFacilitiesToLobbySave();
            return changed;
        }

        private bool MergeFacilityIntoAuthoritativeProgress(string facilityId, int level)
        {
            int safeLevel = Mathf.Max(1, level);

            switch (NormalizeFacilityId(facilityId))
            {
                case "workbench":
                    return TryAssignMaxFacilityLevel(currentData.facilities.workbench, safeLevel, value => currentData.facilities.workbench = value);
                case "commstation":
                    return TryAssignMaxFacilityLevel(currentData.facilities.commStation, safeLevel, value => currentData.facilities.commStation = value);
                case "medical":
                    return TryAssignMaxFacilityLevel(currentData.facilities.medical, safeLevel, value => currentData.facilities.medical = value);
                case "gym":
                    return TryAssignMaxFacilityLevel(currentData.facilities.gym, safeLevel, value => currentData.facilities.gym = value);
                case "stash":
                    return TryAssignMaxFacilityLevel(currentData.facilities.stash, safeLevel, value => currentData.facilities.stash = value);
                case "kitchen":
                    return TryAssignMaxFacilityLevel(currentData.facilities.kitchen, safeLevel, value => currentData.facilities.kitchen = value);
                case "bed":
                    return TryAssignMaxFacilityLevel(currentData.facilities.bed, safeLevel, value => currentData.facilities.bed = value);
                default:
                    return false;
            }
        }

        private static bool TryAssignMaxFacilityLevel(int currentLevel, int incomingLevel, Action<int> assign)
        {
            int safeCurrent = Mathf.Max(1, currentLevel);
            int merged = Mathf.Max(safeCurrent, incomingLevel);

            if (currentLevel == merged)
                return false;

            assign?.Invoke(merged);
            return true;
        }

        private void MirrorFacilitiesToLobbySave()
        {
            if (currentData?.facilities == null)
                return;

            EnsureLobbySaveContainers();
            currentData.lobbySave.facilities.Clear();

            UpsertLobbyFacility("Workbench", currentData.facilities.workbench, allowDowngrade: true);
            UpsertLobbyFacility("CommStation", currentData.facilities.commStation, allowDowngrade: true);
            UpsertLobbyFacility("Medical", currentData.facilities.medical, allowDowngrade: true);
            UpsertLobbyFacility("Gym", currentData.facilities.gym, allowDowngrade: true);
            UpsertLobbyFacility("Stash", currentData.facilities.stash, allowDowngrade: true);
            UpsertLobbyFacility("Kitchen", currentData.facilities.kitchen, allowDowngrade: true);
            UpsertLobbyFacility("Bed", currentData.facilities.bed, allowDowngrade: true);
        }

        private void ReplaceLobbyDtoFacilitiesFromCurrentData(LobbySaveDTO dto)
        {
            if (dto == null)
                return;

            dto.facilities ??= new List<FacilitySaveDTO>();
            dto.facilities.Clear();

            if (currentData?.facilities == null)
                return;

            AddLegacyFacility(dto, "Workbench", currentData.facilities.workbench);
            AddLegacyFacility(dto, "CommStation", currentData.facilities.commStation);
            AddLegacyFacility(dto, "Medical", currentData.facilities.medical);
            AddLegacyFacility(dto, "Gym", currentData.facilities.gym);
            AddLegacyFacility(dto, "Stash", currentData.facilities.stash);
            AddLegacyFacility(dto, "Kitchen", currentData.facilities.kitchen);
            AddLegacyFacility(dto, "Bed", currentData.facilities.bed);
        }

        private void PreserveCurrentFacilityProgress(LobbySaveDTO dto)
        {
            if (dto == null || currentData?.facilities == null)
                return;

            UpsertFacilitySaveDTO(dto, "Workbench", currentData.facilities.workbench);
            UpsertFacilitySaveDTO(dto, "CommStation", currentData.facilities.commStation);
            UpsertFacilitySaveDTO(dto, "Medical", currentData.facilities.medical);
            UpsertFacilitySaveDTO(dto, "Gym", currentData.facilities.gym);
            UpsertFacilitySaveDTO(dto, "Stash", currentData.facilities.stash);
            UpsertFacilitySaveDTO(dto, "Kitchen", currentData.facilities.kitchen);
            UpsertFacilitySaveDTO(dto, "Bed", currentData.facilities.bed);

            if (currentData.lobbySave?.facilities == null)
                return;

            for (int i = 0; i < currentData.lobbySave.facilities.Count; i++)
            {
                LobbyFacilityCloudData facility = currentData.lobbySave.facilities[i];
                if (facility == null || string.IsNullOrWhiteSpace(facility.facilityId))
                    continue;

                UpsertFacilitySaveDTO(dto, facility.facilityId, facility.level);
            }
        }

        private LobbySaveDTO CreateLobbySaveDTOFromLegacyCloudFields()
        {
            LobbySaveDTO dto = new LobbySaveDTO();
            dto.hasCredits = false;
            dto.credits = 0;
            dto.hasStashSection = currentData.stash != null;
            dto.hasEquipmentSection = currentData.equipment != null;

            if (currentData.stash?.slots != null)
            {
                for (int i = 0; i < currentData.stash.slots.Count; i++)
                {
                    StashSlot slot = currentData.stash.slots[i];
                    if (slot == null || string.IsNullOrWhiteSpace(slot.itemId))
                        continue;

                    dto.stashItems.Add(new ItemSaveDTO
                    {
                        itemId = slot.itemId,
                        containerId = "stash",
                        x = GetStashSlotIndex(slot),
                        y = 0,
                        rotated = slot.rotated,
                        stackCount = slot.stackCount,
                        currentDurability = Mathf.Max(0, slot.currentDurability),
                        currentAmmo = Mathf.Max(0, slot.currentAmmo)
                    });
                }
            }

            AddLegacyEquipment(dto, "EquipmentHead", currentData.equipment?.helmetId, currentData.equipment?.helmetDurability ?? 0f);
            AddLegacyEquipment(dto, "EquipmentArmor", currentData.equipment?.armorId, currentData.equipment?.armorDurability ?? 0f);
            AddLegacyEquipment(dto, "primary1", currentData.equipment?.primary1, 0f);
            AddLegacyEquipment(dto, "primary2", currentData.equipment?.primary2, 0f);
            AddLegacyEquipment(dto, "EquipmentSecondaryWeapon", currentData.equipment?.secondary, 0f);
            AddLegacyEquipment(dto, "EquipmentMeleeWeapon", currentData.equipment?.melee, 0f);

            AddLegacyFacility(dto, "Workbench", currentData.facilities?.workbench ?? 1);
            AddLegacyFacility(dto, "CommStation", currentData.facilities?.commStation ?? 1);
            AddLegacyFacility(dto, "Medical", currentData.facilities?.medical ?? 1);
            AddLegacyFacility(dto, "Gym", currentData.facilities?.gym ?? 1);
            AddLegacyFacility(dto, "Stash", currentData.facilities?.stash ?? 1);
            AddLegacyFacility(dto, "Kitchen", currentData.facilities?.kitchen ?? 1);
            AddLegacyFacility(dto, "Bed", currentData.facilities?.bed ?? 1);

            return dto;
        }

        private static void AddLegacyEquipment(LobbySaveDTO dto, string slotId, string itemId, float durability)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            dto.equipmentItems.Add(new EquipmentSaveDTO
            {
                slotId = slotId,
                itemId = itemId,
                durability = durability
            });
        }

        private static void AddLegacyFacility(LobbySaveDTO dto, string facilityId, int level)
        {
            dto.facilities.Add(new FacilitySaveDTO
            {
                facilityId = facilityId,
                level = level
            });
        }

        private void ReconcileLegacyInventorySectionsIntoLobbySaveDTO(LobbySaveDTO dto)
        {
            if (dto == null || currentData == null)
                return;

            if (!dto.hasStashSection && !HasItems(dto.stashItems))
            {
                dto.hasStashSection = currentData.stash != null;

                if (currentData.stash?.slots != null)
                {
                    for (int i = 0; i < currentData.stash.slots.Count; i++)
                    {
                        StashSlot slot = currentData.stash.slots[i];
                        if (slot == null || string.IsNullOrWhiteSpace(slot.itemId))
                            continue;

                        dto.stashItems.Add(new ItemSaveDTO
                        {
                            itemId = slot.itemId,
                            containerId = "stash",
                            x = GetStashSlotIndex(slot),
                            y = 0,
                            rotated = slot.rotated,
                            stackCount = slot.stackCount,
                            currentDurability = Mathf.Max(0, slot.currentDurability),
                            currentAmmo = Mathf.Max(0, slot.currentAmmo)
                        });
                    }
                }
            }

            if (!dto.hasEquipmentSection && !HasItems(dto.equipmentItems))
            {
                dto.hasEquipmentSection = currentData.equipment != null;

                AddLegacyEquipment(dto, "EquipmentHead", currentData.equipment?.helmetId, currentData.equipment?.helmetDurability ?? 0f);
                AddLegacyEquipment(dto, "EquipmentArmor", currentData.equipment?.armorId, currentData.equipment?.armorDurability ?? 0f);
                AddLegacyEquipment(dto, "primary1", currentData.equipment?.primary1, 0f);
                AddLegacyEquipment(dto, "primary2", currentData.equipment?.primary2, 0f);
                AddLegacyEquipment(dto, "EquipmentSecondaryWeapon", currentData.equipment?.secondary, 0f);
                AddLegacyEquipment(dto, "EquipmentMeleeWeapon", currentData.equipment?.melee, 0f);
            }
        }

        private void ReconcileLegacyFacilitiesIntoLobbySaveDTO(LobbySaveDTO dto)
        {
            if (dto == null || currentData?.facilities == null)
                return;

            UpsertFacilitySaveDTO(dto, "Workbench", currentData.facilities.workbench);
            UpsertFacilitySaveDTO(dto, "CommStation", currentData.facilities.commStation);
            UpsertFacilitySaveDTO(dto, "Medical", currentData.facilities.medical);
            UpsertFacilitySaveDTO(dto, "Gym", currentData.facilities.gym);
            UpsertFacilitySaveDTO(dto, "Stash", currentData.facilities.stash);
            UpsertFacilitySaveDTO(dto, "Kitchen", currentData.facilities.kitchen);
            UpsertFacilitySaveDTO(dto, "Bed", currentData.facilities.bed);
        }

        private static void UpsertFacilitySaveDTO(LobbySaveDTO dto, string facilityId, int level)
        {
            if (dto == null || string.IsNullOrWhiteSpace(facilityId))
                return;

            dto.facilities ??= new List<FacilitySaveDTO>();

            string normalizedId = NormalizeFacilityId(facilityId);
            int safeLevel = Mathf.Max(1, level);

            for (int i = 0; i < dto.facilities.Count; i++)
            {
                FacilitySaveDTO facility = dto.facilities[i];

                if (facility == null)
                    continue;

                if (NormalizeFacilityId(facility.facilityId) != normalizedId)
                    continue;

                facility.facilityId = facilityId;
                facility.level = Mathf.Max(Mathf.Max(1, facility.level), safeLevel);
                return;
            }

            dto.facilities.Add(new FacilitySaveDTO
            {
                facilityId = facilityId,
                level = safeLevel
            });
        }

        private static void ForceFacilityLevel(LobbySaveDTO dto, string facilityId, int level)
        {
            if (dto == null || string.IsNullOrWhiteSpace(facilityId))
                return;

            dto.facilities ??= new List<FacilitySaveDTO>();

            string normalizedId = NormalizeFacilityId(facilityId);
            int safeLevel = Mathf.Max(1, level);

            for (int i = 0; i < dto.facilities.Count; i++)
            {
                FacilitySaveDTO facility = dto.facilities[i];

                if (facility == null)
                    continue;

                if (NormalizeFacilityId(facility.facilityId) != normalizedId)
                    continue;

                facility.facilityId = facilityId;
                facility.level = safeLevel;
                return;
            }

            dto.facilities.Add(new FacilitySaveDTO
            {
                facilityId = facilityId,
                level = safeLevel
            });
        }

        private static int GetStashSlotIndex(StashSlot slot)
        {
            if (slot == null)
                return 0;

            return Mathf.Max(0, slot.gridY) * DefaultStashColumnCount + Mathf.Max(0, slot.gridX);
        }

        private static int GetStashGridX(ItemSaveDTO item)
        {
            if (item == null)
                return 0;

            return item.x >= DefaultStashColumnCount && item.y == 0
                ? item.x % DefaultStashColumnCount
                : item.x;
        }

        private static int GetStashGridY(ItemSaveDTO item)
        {
            if (item == null)
                return 0;

            return item.x >= DefaultStashColumnCount && item.y == 0
                ? item.x / DefaultStashColumnCount
                : item.y;
        }
    }
}

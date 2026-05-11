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

        private FirebaseFirestore db;
        private FirebaseAuthManager authManager;
        private PlayerCloudData currentData;
        private string loadedFirebaseUid = string.Empty;

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

        private void Awake()
        {
            ServiceLocator.Register(this);
        }

        private void Start()
        {
            EventBus.Subscribe<AuthSignedInEvent>(OnAuthSignedIn);
            EventBus.Subscribe<AuthSignedOutEvent>(OnAuthSignedOut);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Subscribe<SceneChangedEvent>(OnSceneChanged);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<AuthSignedInEvent>(OnAuthSignedIn);
            EventBus.Unsubscribe<AuthSignedOutEvent>(OnAuthSignedOut);
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Unsubscribe<SceneChangedEvent>(OnSceneChanged);

            ServiceLocator.Unregister<CloudSaveSystem>();
        }

        private void OnApplicationQuit()
        {
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
                    Debug.Log("[CloudSaveSystem] Disabling Firestore persistence before first read/write");
                    firestore.Settings.PersistenceEnabled = false;
                    Debug.Log("[CloudSaveSystem] Firestore persistence disabled");
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
            currentData = null;
            loadedFirebaseUid = string.Empty;
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
            if (e.sceneName != "Hideout") return;
            if (!HasLoadedData) return;
            if (string.IsNullOrWhiteSpace(loadedFirebaseUid)) return;

            Debug.Log("[CloudSaveSystem] Returned to Hideout -> upload");
            await UploadAsync();
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

                    if (TryApplyStarterPackMigration())
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

                EventBus.Publish(new CloudSaveLoadedEvent
                {
                    firebaseUid = uid,
                    isNewUser = isNew,
                });

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
                CollectDataFromScene();
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

                Debug.Log("[CloudSaveSystem] 파산신청 완료. 경제 데이터만 스타터팩 상태로 초기화했습니다.", this);
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
                currentData.progress.credits = wallet.Credits.Value;
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
            // FacilityBase는 Hideout 씬에만 존재. 다른 씬이면 찾기 못하고 기존 값 유지됨.
            var facilities = FindObjectsByType<FacilityBase>(FindObjectsSortMode.None);
            if (facilities == null || facilities.Length == 0) return;

            foreach (var f in facilities)
            {
                switch (f.Type)
                {
                    case FacilityType.Workbench: currentData.facilities.workbench = f.CurrentLevel.Value; break;
                    case FacilityType.CommStation: currentData.facilities.commStation = f.CurrentLevel.Value; break;
                    case FacilityType.Gym: currentData.facilities.gym = f.CurrentLevel.Value; break;
                    case FacilityType.Stash: currentData.facilities.stash = f.CurrentLevel.Value; break;
                    case FacilityType.Kitchen: currentData.facilities.kitchen = f.CurrentLevel.Value; break;
                    case FacilityType.Bed: currentData.facilities.bed = f.CurrentLevel.Value; break;
                }
            }
        }

        private void CollectPersonalQuestProgress()
        {
            if (NetworkManager.Singleton == null) return;

            var quest = ServiceLocator.Get<QuestManager>();
            if (quest == null) return;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            var myState = quest.GetPlayerState(localClientId);
            if (myState == null) return;

            myState.WriteToCloudProgress(currentData.progress);
        }

        private void ApplyBankruptcyStarterPack(StarterPackConfigSO starterPack)
        {
            currentData.progress.credits = starterPack.StartingCredits;
            currentData.stash.slots = BuildStarterPackStashSlots(starterPack);
            currentData.safePocket.slots.Clear();
            currentData.equipment = new EquipmentData();
            currentData.insurance.Clear();
            currentData.schemaVersion = Mathf.Max(currentData.schemaVersion, EconomyStarterPackSchemaVersion);
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
            int nextSlotIndex = 0;

            IReadOnlyList<StarterPackEntry> entries = starterPack.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AppendStarterPackEntry(result, entries[i], ref nextSlotIndex);
            }

            return result;
        }

        private static void AppendStarterPackEntry(List<StashSlot> result, StarterPackEntry entry, ref int nextSlotIndex)
        {
            if (entry == null || entry.Item == null)
                return;

            int remaining = entry.Amount;
            int maxStack = Mathf.Max(1, entry.Item.maxStackSize);

            while (remaining > 0)
            {
                int stackCount = Mathf.Min(maxStack, remaining);
                result.Add(CreateStarterPackSlot(entry, stackCount, nextSlotIndex));
                nextSlotIndex++;
                remaining -= stackCount;
            }
        }

        private static StashSlot CreateStarterPackSlot(StarterPackEntry entry, int stackCount, int slotIndex)
        {
            ItemDataSO item = entry.Item;

            return new StashSlot
            {
                itemId = item.itemID,
                stackCount = stackCount,
                gridX = slotIndex % DefaultStashColumnCount,
                gridY = slotIndex / DefaultStashColumnCount,
                rotated = false,
                currentDurability = GetDurabilityValue(item, entry.DurabilityRatio),
                currentAmmo = GetCurrentAmmoValue(item, entry.CurrentAmmo),
            };
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
    }
}

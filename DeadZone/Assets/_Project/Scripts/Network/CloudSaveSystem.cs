using System;
using System.Threading.Tasks;
using Firebase.Firestore;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Network
{
    /// <summary>
    /// Firestore로 PlayerCloudData를 업/다운로드.
    /// Lives on PersistentSystems 하위의 자기 GameObject (DontDestroyOnLoad).
    ///
    /// 세이브 트리거 (팀장 결정):
    ///   1) PlayerDiedEvent 발행 + 본인 플레이어일 때 즉시 업로드
    ///   2) SceneChangedEvent("Hideout") 수신 시 업로드
    ///   3) OnApplicationQuit 시 최대 3초 동기 대기 업로드
    ///
    /// 데이터 수집 전략 (Part VII §7.7):
    ///   CloudSaveSystem이 각 시스템을 ServiceLocator / GetComponent로 pull.
    ///   (L1 → L3 의존성은 이 매니저에 한해 예외 허용.)
    ///
    /// Firestore 직렬화:
    ///   PlayerCloudData에 [FirestoreData] 어노테이션이 붙어있어
    ///   SetAsync(currentData) / snapshot.ConvertTo<T>() 만으로 자동 변환.
    /// </summary>
    public class CloudSaveSystem : MonoBehaviour
    {
        private const string UsersCollection = "users";

        private FirebaseFirestore db;
        private FirebaseAuthManager authManager;
        private PlayerCloudData currentData;

        public PlayerCloudData CurrentData => currentData;
        public bool HasLoadedData => currentData != null;

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

            TryAttachFirestore();
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

            var uploadTask = UploadAsync();
            uploadTask.Wait(TimeSpan.FromSeconds(3));
        }

        private void TryAttachFirestore()
        {
            var bootstrap = ServiceLocator.Get<FirebaseBootstrap>();
            if (bootstrap == null || !bootstrap.IsReady)
            {
                Invoke(nameof(TryAttachFirestore), 0.1f);
                return;
            }

            db = FirebaseFirestore.DefaultInstance;
            authManager = ServiceLocator.Get<FirebaseAuthManager>();
            Debug.Log("[CloudSaveSystem] Attached to Firestore");
        }

        // =================================================================
        // 이벤트 핸들러
        // =================================================================

        private async void OnAuthSignedIn(AuthSignedInEvent e)
        {
            await LoadAsync();
        }

        private void OnAuthSignedOut(AuthSignedOutEvent e)
        {
            currentData = null;
        }

        private async void OnPlayerDied(PlayerDiedEvent e)
        {
            // 본인이 죽었을 때만 업로드
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
            if (db == null || authManager == null || !authManager.IsSignedIn)
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot load: not ready or not signed in");
                return null;
            }

            string uid = authManager.CurrentUid;
            bool isNew = false;

            try
            {
                var doc = db.Collection(UsersCollection).Document(uid);
                var snapshot = await doc.GetSnapshotAsync();

                if (snapshot.Exists)
                {
                    // [FirestoreData] 어노테이션이 붙어있으면 ConvertTo<T>()로 자동 역직렬화
                    currentData = snapshot.ConvertTo<PlayerCloudData>();
                    if (currentData == null) currentData = NewPlayerData(uid, authManager.CurrentEmail);
                }
                else
                {
                    currentData = NewPlayerData(uid, authManager.CurrentEmail);
                    isNew = true;
                    await UploadAsync();  // 신규 유저는 즉시 기본 데이터 기록
                }

                EventBus.Publish(new CloudSaveLoadedEvent
                {
                    firebaseUid = uid,
                    isNewUser = isNew,
                });

                return currentData;
            }
            catch (Exception ex)
            {
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
            if (db == null || authManager == null || !authManager.IsSignedIn)
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: not ready or not signed in");
                return false;
            }
            if (currentData == null)
            {
                Debug.LogWarning("[CloudSaveSystem] Cannot upload: currentData null (not loaded)");
                return false;
            }

            string uid = authManager.CurrentUid;

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

        // =================================================================
        // 씬에서 데이터 수집 (Part VII §7.7 - Pull 방식)
        // =================================================================

        private void CollectDataFromScene()
        {
            if (currentData == null) return;

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
            currentData.equipment.armorId  = eq.TorsoSlotId.Value.ToString();
            currentData.equipment.primary1 = eq.Primary1Id.Value.ToString();
            currentData.equipment.primary2 = eq.Primary2Id.Value.ToString();
            currentData.equipment.secondary = eq.SecondaryId.Value.ToString();
            currentData.equipment.melee    = eq.MeleeId.Value.ToString();
            currentData.equipment.helmetDurability = eq.HelmetDurability.Value;
            currentData.equipment.armorDurability  = eq.ArmorDurability.Value;
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
                    case FacilityType.Workbench:   currentData.facilities.workbench   = f.CurrentLevel.Value; break;
                    case FacilityType.CommStation: currentData.facilities.commStation = f.CurrentLevel.Value; break;
                    case FacilityType.Gym:         currentData.facilities.gym         = f.CurrentLevel.Value; break;
                    case FacilityType.Stash:       currentData.facilities.stash       = f.CurrentLevel.Value; break;
                    case FacilityType.Kitchen:     currentData.facilities.kitchen     = f.CurrentLevel.Value; break;
                    case FacilityType.Bed:         currentData.facilities.bed         = f.CurrentLevel.Value; break;
                }
            }
        }

        private void CollectPersonalQuestProgress()
        {
            // v1.3 초기: NetworkList(공유 상태)를 그대로 개인 필드에 복사.
            // 추후 QuestManager 개선 시 개인 플래그를 분리하면 이 로직도 교체.
            var quest = ServiceLocator.Get<QuestManager>();
            if (quest == null) return;

            currentData.progress.personalActiveQuestIds.Clear();
            for (int i = 0; i < quest.ActiveQuestIds.Count; i++)
                currentData.progress.personalActiveQuestIds.Add(quest.ActiveQuestIds[i].ToString());

            currentData.progress.personalCompletedQuestIds.Clear();
            for (int i = 0; i < quest.CompletedQuestIds.Count; i++)
                currentData.progress.personalCompletedQuestIds.Add(quest.CompletedQuestIds[i].ToString());
        }

        // =================================================================
        // 신규 유저 기본값
        // =================================================================

        private static PlayerCloudData NewPlayerData(string uid, string email)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new PlayerCloudData();
            data.profile.email = email ?? "";
            data.profile.createdAtUnix = now;
            data.profile.lastPlayedAtUnix = now;
            data.profile.totalPlayTimeSec = 0;
            data.progress.credits = 0;
            // facilities 는 생성자에서 모두 Lv1 기본값
            return data;
        }
    }
}

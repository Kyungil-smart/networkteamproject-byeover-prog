using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Systems.Quests
{
    public class QuestManager : NetworkBehaviour, IQuestQuery
    {
        private const ulong StandaloneClientId = 0;

        [Header("Quest Data (8개)")]
        [SerializeField] private QuestDataSO[] allQuests;

        [Header("Debug")]
        [Tooltip("true면 Home 키로 순차 퀘스트 강제 완료 가능")]
        [SerializeField] private bool enableDebugKeys = true;

        
        private readonly Dictionary<ulong, PlayerQuestState> _playerStates = new();

       
        private readonly Dictionary<string, QuestDataSO> _questLookup = new();

        // ───────── 생명주기 ─────────

        private void Awake()
        {
            foreach (var q in allQuests)
            {
                if (q != null) _questLookup[q.questID] = q;
            }
        }

        private void OnEnable()
        {
            if (!IsSpawned)
            {
                RegisterQuestServices();
                RestoreLocalStateFromCloudIfAvailable();
            }
        }

        private void OnDisable()
        {
            if (!IsSpawned)
                UnregisterQuestServicesIfCurrent();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            RegisterQuestServices();
            RestoreLocalStateFromCloudIfAvailable();

            if (IsServer)
            {
                EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
                EventBus.Subscribe<ZoneEnteredEvent>(OnZoneEntered);
                EventBus.Subscribe<SceneChangedEvent>(OnSceneChanged);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
                EventBus.Unsubscribe<ZoneEnteredEvent>(OnZoneEntered);
                EventBus.Unsubscribe<SceneChangedEvent>(OnSceneChanged);
            }
            UnregisterQuestServicesIfCurrent();
            base.OnNetworkDespawn();
        }

        private void RegisterQuestServices()
        {
            if (ServiceLocator.Get<QuestManager>() != this)
                ServiceLocator.Register(this);

            if (ServiceLocator.Get<IQuestQuery>() != (IQuestQuery)this)
                ServiceLocator.Register<IQuestQuery>(this);
        }

        private void UnregisterQuestServicesIfCurrent()
        {
            if (ServiceLocator.Get<IQuestQuery>() == (IQuestQuery)this)
                ServiceLocator.Unregister<IQuestQuery>();

            if (ServiceLocator.Get<QuestManager>() == this)
                ServiceLocator.Unregister<QuestManager>();
        }

        private void Update()
        {
           
            if (!enableDebugKeys || !IsServer) return;

            if (Input.GetKeyDown(KeyCode.Home))
            {
                DebugForceCompleteNext();
            }
        }
        

        public PlayerQuestState GetPlayerState(ulong clientId)
        {
            if (!_playerStates.TryGetValue(clientId, out var state))
            {
                state = new PlayerQuestState();
                _playerStates[clientId] = state;
            }
            return state;
        }

        public ulong GetLocalClientIdForState()
        {
            return HasNetworkSession()
                ? NetworkManager.Singleton.LocalClientId
                : StandaloneClientId;
        }

        private static bool HasNetworkSession()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        private bool CanWriteQuestState()
        {
            return IsServer || !HasNetworkSession();
        }

        private void RestoreLocalStateFromCloudIfAvailable()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData || cloudSaveSystem.CurrentData?.progress == null)
                return;

            RestorePlayerState(GetLocalClientIdForState(), cloudSaveSystem.CurrentData.progress);
        }

       
        public void RestorePlayerState(ulong clientId, ProgressData progress)
        {
            var state = GetPlayerState(clientId);
            state.ReadFromCloudProgress(progress);

            Debug.Log($"[QuestManager] Restored client {clientId}: " +
                      $"active={state.ActiveQuestIds.Count}, " +
                      $"completed={state.CompletedQuestIds.Count}, " +
                      $"pending={state.PendingCompletionIds.Count}");

            if (state.ActiveQuestIds.Count == 0 && state.CompletedQuestIds.Count == 0)
                AcceptQuest(clientId, "Q1");

            PublishQuestTrackerSnapshot(clientId);
        }

        /// <summary>
        /// 복원된 퀘스트 상태를 바탕으로 HUD가 즉시 표시할 수 있는 현재 진행도 이벤트를 발행합니다.
        /// 수락/진행/완료 이벤트와 달리 실제 상태 변화가 아니라, UI 초기 표시를 위한 스냅샷입니다.
        /// </summary>
        private void PublishQuestTrackerSnapshot(ulong clientId)
        {
            PlayerQuestState state = GetPlayerState(clientId);

            foreach (string questId in state.ActiveQuestIds)
            {
                if (!TryGetQuestData(questId, out QuestDataSO questData))
                    continue;

                if (questData.objectives == null || questData.objectives.Length == 0)
                    continue;

                QuestObjectiveData objective = questData.objectives[0];
                EventBus.Publish(new QuestTrackerSnapshotEvent
                {
                    questId = new FixedString64Bytes(questId),
                    objectiveType = objective.type,
                    currentCount = state.GetProgress(questId, objective.targetID),
                    requiredCount = objective.requiredCount,
                    clientId = clientId,
                    targetId = new FixedString64Bytes(objective.targetID),
                    isPendingCompletion = state.PendingCompletionIds.Contains(questId)
                });

                return;
            }
        }


        public bool AcceptQuest(ulong clientId, string questId)
        {
            if (!CanWriteQuestState()) return false;
            if (!_questLookup.TryGetValue(questId, out var questData)) return false;

            var state = GetPlayerState(clientId);
            if (state.ActiveQuestIds.Contains(questId) || state.CompletedQuestIds.Contains(questId))
                return false;

            if (!string.IsNullOrEmpty(questData.prerequisiteQuestID)
                && !state.CompletedQuestIds.Contains(questData.prerequisiteQuestID))
                return false;

            state.ActiveQuestIds.Add(questId);

            foreach (var obj in questData.objectives)
            {
                string key = PlayerQuestState.MakeKey(questId, obj.targetID);
                if (!state.ObjectiveProgress.ContainsKey(key))
                    state.ObjectiveProgress[key] = (0, obj.requiredCount);
            }

            EventBus.Publish(new QuestAcceptedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = clientId
            });

            if (HasNetworkSession())
            {
                NotifyQuestAcceptedClientRpc(questId, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }

            Debug.Log($"[QuestManager] Client {clientId} accepted {questId}");
            TryAutoAcceptSideQuests(clientId);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AcceptQuestServerRpc(string questId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            AcceptQuest(senderClientId, questId);
        }

        private void TryAutoAcceptSideQuests(ulong clientId)
        {
            var state = GetPlayerState(clientId);
            foreach (var quest in allQuests)
            {
                if (quest == null || !quest.isSideQuest) continue;
                if (state.ActiveQuestIds.Contains(quest.questID)) continue;
                if (state.CompletedQuestIds.Contains(quest.questID)) continue;
                if (!string.IsNullOrEmpty(quest.prerequisiteQuestID)
                    && !state.CompletedQuestIds.Contains(quest.prerequisiteQuestID))
                    continue;

                AcceptQuest(clientId, quest.questID);
            }
        }

        public void ReportProgress(ulong clientId, ObjectiveType type, string targetId, int amount)
        {
            if (!CanWriteQuestState()) return;
            var state = GetPlayerState(clientId);

            var activeSnapshot = new List<string>(state.ActiveQuestIds);

            foreach (string questId in activeSnapshot)
            {
                if (!_questLookup.TryGetValue(questId, out var questData)) continue;

                foreach (var obj in questData.objectives)
                {
                    if (obj.type != type || obj.targetID != targetId) continue;

                    int newCount = state.AddProgress(questId, targetId, amount, obj.requiredCount);

                    EventBus.Publish(new QuestProgressEvent
                    {
                        questId = new FixedString64Bytes(questId),
                        objectiveType = type,
                        currentCount = newCount,
                        requiredCount = obj.requiredCount,
                        clientId = clientId,
                        targetId = new FixedString64Bytes(targetId)
                    });

                    if (HasNetworkSession())
                    {
                        NotifyQuestProgressClientRpc(questId, targetId, newCount, obj.requiredCount,
                            new ClientRpcParams
                            {
                                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                            });
                    }
                    
                    if (state.AreAllObjectivesComplete(questId)
                        && !state.PendingCompletionIds.Contains(questId))
                    {
                        state.PendingCompletionIds.Add(questId);
                        Debug.Log($"[QuestManager] Client {clientId}: {questId} objectives done → pending (Hideout에서 완료 처리)");

                        if (HasNetworkSession())
                        {
                            NotifyQuestPendingClientRpc(questId, new ClientRpcParams
                            {
                                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                            });
                        }
                    }
                }
            }
        }

        public int GetObjectiveProgress(ulong clientId, string questId, string targetId)
            => GetPlayerState(clientId).GetProgress(questId, targetId);

        /// <summary>
        /// questId를 기준으로 QuestManager가 보유한 QuestDataSO를 조회합니다.
        /// EventBus로 전달된 questId를 UI나 다른 수신부가 표시용 데이터로 해석할 때 사용합니다.
        /// </summary>
        public bool TryGetQuestData(string questId, out QuestDataSO questData)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                questData = null;
                return false;
            }

            return _questLookup.TryGetValue(questId, out questData);
        }

        public bool IsQuestCompleted(ulong clientId, string questId)
            => GetPlayerState(clientId).CompletedQuestIds.Contains(questId);

        public bool IsQuestActive(ulong clientId, string questId)
            => GetPlayerState(clientId).ActiveQuestIds.Contains(questId);
        
        public bool IsQuestCompleted(string questId)
        {
            return IsQuestCompleted(GetLocalClientIdForState(), questId);
        }

        public bool IsQuestActive(string questId)
        {
            return IsQuestActive(GetLocalClientIdForState(), questId);
        }

        private void OnSceneChanged(SceneChangedEvent e)
        {
            if (!CanWriteQuestState()) return;
            if (e.sceneName.ToString() != "Hideout") return;

            Debug.Log("[QuestManager] Hideout 진입 → 대기 중인 퀘스트 완료 처리 시작");

            foreach (var kvp in _playerStates)
            {
                ProcessPendingCompletions(kvp.Key);
            }
        }
        
        private void ProcessPendingCompletions(ulong clientId)
        {
            var state = GetPlayerState(clientId);
            if (state.PendingCompletionIds.Count == 0) return;

            // 복사본으로 순회 (CompleteQuest가 다음 퀘스트를 수주하면서 PendingIds가 바뀔 수 있음)
            var pendingSnapshot = new List<string>(state.PendingCompletionIds);

            foreach (string questId in pendingSnapshot)
            {
                CompleteQuest(clientId, questId);
            }

            Debug.Log($"[QuestManager] Client {clientId}: {pendingSnapshot.Count}개 퀘스트 완료 처리됨");
        }

        private void CompleteQuest(ulong clientId, string questId)
        {
            if (!CanWriteQuestState()) return;
            var state = GetPlayerState(clientId);

            state.ActiveQuestIds.Remove(questId);
            state.PendingCompletionIds.Remove(questId);
            state.CompletedQuestIds.Add(questId);

            if (!_questLookup.TryGetValue(questId, out var questData)) return;
            
            if (!string.IsNullOrEmpty(questData.unlockZoneID))
                state.UnlockedZones.Add(questData.unlockZoneID);

            GrantRewards(clientId, questData);

            EventBus.Publish(new QuestCompletedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = clientId,
                unlockZoneId = new FixedString64Bytes(questData.unlockZoneID ?? "")
            });

            if (HasNetworkSession())
            {
                NotifyQuestCompletedClientRpc(questId, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }

            Debug.Log($"[QuestManager] Client {clientId} completed {questId}" +
                      (string.IsNullOrEmpty(questData.unlockZoneID) ? "" : $" → unlocked {questData.unlockZoneID}"));

            TryAutoAcceptNextQuest(clientId, questId);
        }

        private void TryAutoAcceptNextQuest(ulong clientId, string completedQuestId)
        {
            foreach (var quest in allQuests)
            {
                if (quest == null || quest.isSideQuest) continue;
                if (quest.prerequisiteQuestID == completedQuestId)
                {
                    AcceptQuest(clientId, quest.questID);
                    break;
                }
            }
        }
        

        private void GrantRewards(ulong clientId, QuestDataSO questData)
        {
            if (questData.rewards == null) return;
            foreach (var reward in questData.rewards)
            {
                switch (reward.type)
                {
                    case RewardType.Credits:
                        Debug.Log($"[QuestManager] Grant {reward.amount} credits → client {clientId}");
                        break;
                    case RewardType.Item:
                        Debug.Log($"[QuestManager] Grant item {reward.itemID} x{reward.amount} → client {clientId}");
                        break;
                    case RewardType.FacilityMaterial:
                        Debug.Log($"[QuestManager] Grant facility mat {reward.itemID} x{reward.amount} → client {clientId}");
                        break;
                }
            }
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            if (!CanWriteQuestState()) return;
            string enemyId = e.enemyId.ToString();
            if (string.IsNullOrEmpty(enemyId)) return;
            ReportProgress(e.attackerClientId, ObjectiveType.Kill, enemyId, 1);
        }

        private void OnZoneEntered(ZoneEnteredEvent e)
        {
            if (!CanWriteQuestState()) return;
            ReportProgress(e.clientId, ObjectiveType.Reach, e.zoneId.ToString(), 1);
        }
        
        private void DebugForceCompleteNext()
        {
            ulong localClientId = GetLocalClientIdForState();
            var state = GetPlayerState(localClientId);

            // 메인 퀘스트 순서대로 찾아서 첫 번째 미완료를 완료
            foreach (var quest in allQuests)
            {
                if (quest == null || quest.isSideQuest) continue;
                if (state.CompletedQuestIds.Contains(quest.questID)) continue;

                // 아직 수주 안 됐으면 수주부터
                if (!state.ActiveQuestIds.Contains(quest.questID))
                    AcceptQuest(localClientId, quest.questID);

                // objective 전부 채우기
                foreach (var obj in quest.objectives)
                {
                    string key = PlayerQuestState.MakeKey(quest.questID, obj.targetID);
                    state.ObjectiveProgress[key] = (obj.requiredCount, obj.requiredCount);
                }

                // 즉시 완료 (디버그니까 Hideout 대기 안 함)
                CompleteQuest(localClientId, quest.questID);

                Debug.Log($"<color=yellow>[DEBUG] Home 키 → {quest.questID} ({quest.questName}) 강제 완료</color>");
                return;
            }

            Debug.Log("<color=yellow>[DEBUG] Home 키 → 모든 메인 퀘스트 완료됨</color>");
        }

        // ───────── ClientRpc ─────────

        [ClientRpc]
        private void NotifyQuestAcceptedClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            Debug.Log($"[QuestManager] Quest accepted: {questId}");
        }

        [ClientRpc]
        private void NotifyQuestProgressClientRpc(string questId, string targetId,
            int currentCount, int requiredCount, ClientRpcParams rpcParams = default)
        {
            Debug.Log($"[QuestManager] Progress: {questId}/{targetId} ({currentCount}/{requiredCount})");
        }

        [ClientRpc]
        private void NotifyQuestPendingClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            Debug.Log($"[QuestManager] Quest ready to complete (return to Hideout): {questId}");
        }

        [ClientRpc]
        private void NotifyQuestCompletedClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            Debug.Log($"[QuestManager] Quest completed: {questId}");
        }

        // ───────── 세션 관리 ─────────

        public void OnPlayerDisconnected(ulong clientId)
        {
            Debug.Log($"[QuestManager] Client {clientId} disconnected. State preserved.");
        }

        public void ClearAllPlayerStates()
        {
            _playerStates.Clear();
            Debug.Log("[QuestManager] All player states cleared.");
        }
    }
}

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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ServiceLocator.Register(this);
            ServiceLocator.Register<IQuestQuery>(this);
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
            ServiceLocator.Unregister<IQuestQuery>();
            ServiceLocator.Unregister<QuestManager>();
            base.OnNetworkDespawn();
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

        private void RestoreLocalStateFromCloudIfAvailable()
        {
            CloudSaveSystem cloudSaveSystem = ServiceLocator.Get<CloudSaveSystem>();
            if (cloudSaveSystem == null || !cloudSaveSystem.HasLoadedData || cloudSaveSystem.CurrentData?.progress == null)
                return;

            if (NetworkManager.Singleton == null)
                return;

            RestorePlayerState(NetworkManager.Singleton.LocalClientId, cloudSaveSystem.CurrentData.progress);
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
        }


        public bool AcceptQuest(ulong clientId, string questId)
        {
            if (!IsServer) return false;
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

            NotifyQuestAcceptedClientRpc(questId, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

            Debug.Log($"[QuestManager] Client {clientId} accepted {questId}");
            TryAutoAcceptSideQuests(clientId);
            return true;
        }
        [ServerRpc(RequireOwnership = false)]
        public void AcceptQuestServerRpc(string questId, ServerRpcParams rpcParams = default)
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
            if (!IsServer) return;
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

                    NotifyQuestProgressClientRpc(questId, targetId, newCount, obj.requiredCount,
                        new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                        });
                    
                    if (state.AreAllObjectivesComplete(questId)
                        && !state.PendingCompletionIds.Contains(questId))
                    {
                        state.PendingCompletionIds.Add(questId);
                        Debug.Log($"[QuestManager] Client {clientId}: {questId} objectives done → pending (Hideout에서 완료 처리)");

                        NotifyQuestPendingClientRpc(questId, new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                        });
                    }
                }
            }
        }

        public int GetObjectiveProgress(ulong clientId, string questId, string targetId)
            => GetPlayerState(clientId).GetProgress(questId, targetId);

        public bool IsQuestCompleted(ulong clientId, string questId)
            => GetPlayerState(clientId).CompletedQuestIds.Contains(questId);

        public bool IsQuestActive(ulong clientId, string questId)
            => GetPlayerState(clientId).ActiveQuestIds.Contains(questId);
        
        public bool IsQuestCompleted(string questId)
        {
            if (NetworkManager.Singleton == null) return false;
            return IsQuestCompleted(NetworkManager.Singleton.LocalClientId, questId);
        }

        public bool IsQuestActive(string questId)
        {
            if (NetworkManager.Singleton == null) return false;
            return IsQuestActive(NetworkManager.Singleton.LocalClientId, questId);
        }

        private void OnSceneChanged(SceneChangedEvent e)
        {
            if (!IsServer) return;
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
            if (!IsServer) return;
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

            NotifyQuestCompletedClientRpc(questId, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

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
            if (!IsServer) return;
            string enemyId = e.enemyId.ToString();
            if (string.IsNullOrEmpty(enemyId)) return;
            ReportProgress(e.attackerClientId, ObjectiveType.Kill, enemyId, 1);
        }

        private void OnZoneEntered(ZoneEnteredEvent e)
        {
            if (!IsServer) return;
            ReportProgress(e.clientId, ObjectiveType.Reach, e.zoneId.ToString(), 1);
        }
        
        private void DebugForceCompleteNext()
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
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

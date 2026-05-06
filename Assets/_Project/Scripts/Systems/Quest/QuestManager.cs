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

        /// <summary>clientId → 개인 퀘스트 상태. 서버에서만 Write.</summary>
        private readonly Dictionary<ulong, PlayerQuestState> _playerStates = new();

        /// <summary>questID → QuestDataSO 빠른 조회.</summary>
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

            if (IsServer)
            {
                EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
                EventBus.Subscribe<ZoneEnteredEvent>(OnZoneEntered);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
                EventBus.Unsubscribe<ZoneEnteredEvent>(OnZoneEntered);
            }
            ServiceLocator.Unregister<IQuestQuery>();
            ServiceLocator.Unregister<QuestManager>();
            base.OnNetworkDespawn();
        }

        // ───────── 플레이어 상태 접근 ─────────

        public PlayerQuestState GetPlayerState(ulong clientId)
        {
            if (!_playerStates.TryGetValue(clientId, out var state))
            {
                state = new PlayerQuestState();
                _playerStates[clientId] = state;
            }
            return state;
        }

        /// <summary>CloudSaveSystem이 Firestore 로드 후 호출.</summary>
        public void RestorePlayerState(ulong clientId, ProgressData progress)
        {
            var state = GetPlayerState(clientId);
            state.ReadFromCloudProgress(progress);

            Debug.Log($"[QuestManager] Restored client {clientId}: " +
                      $"active={state.ActiveQuestIds.Count}, completed={state.CompletedQuestIds.Count}");

            if (state.ActiveQuestIds.Count == 0 && state.CompletedQuestIds.Count == 0)
            {
                AcceptQuest(clientId, "Q1");
            }
        }

        // ───────── 퀘스트 수락 ─────────

        public bool AcceptQuest(ulong clientId, string questId)
        {
            if (!IsServer) return false;
            if (!_questLookup.TryGetValue(questId, out var questData)) return false;

            var state = GetPlayerState(clientId);
            if (state.ActiveQuestIds.Contains(questId) || state.CompletedQuestIds.Contains(questId))
                return false;

            // 선행 퀘스트 확인
            if (!string.IsNullOrEmpty(questData.prerequisiteQuestID)
                && !state.CompletedQuestIds.Contains(questData.prerequisiteQuestID))
                return false;

            state.ActiveQuestIds.Add(questId);

            // objective 카운트 초기화
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

        // ───────── IQuestQuery 구현 ─────────

        public void ReportProgress(ulong clientId, ObjectiveType type, string targetId, int amount)
        {
            if (!IsServer) return;
            var state = GetPlayerState(clientId);

            // 복사본으로 순회 (CompleteQuest가 ActiveQuestIds를 수정하므로)
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

                    if (state.AreAllObjectivesComplete(questId))
                        CompleteQuest(clientId, questId);
                }
            }
        }

        public int GetObjectiveProgress(ulong clientId, string questId, string targetId)
            => GetPlayerState(clientId).GetProgress(questId, targetId);

        public bool IsQuestCompleted(ulong clientId, string questId)
            => GetPlayerState(clientId).CompletedQuestIds.Contains(questId);

        public bool IsQuestActive(ulong clientId, string questId)
            => GetPlayerState(clientId).ActiveQuestIds.Contains(questId);

        // ───────── 하위 호환 오버로드 (clientId 없는 옛날 호출용) ─────────
        // CommunicationsQuestUnlockProvider 등 외부 코드가 QuestManager.IsQuestCompleted("Q3")
        // 형태로 호출하면 로컬 클라이언트 기준으로 동작한다.

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

        // ───────── 퀘스트 완료 ─────────

        private void CompleteQuest(ulong clientId, string questId)
        {
            if (!IsServer) return;
            var state = GetPlayerState(clientId);
            if (!state.ActiveQuestIds.Remove(questId)) return;
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

            Debug.Log($"[QuestManager] Client {clientId} completed {questId}");
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

        // ───────── 보상 지급 ─────────

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

        // ───────── EventBus 핸들러 (서버 전용) ─────────

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
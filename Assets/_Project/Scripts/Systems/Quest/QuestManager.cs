using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Network;
using DeadZone.Systems;

namespace DeadZone.Systems.Quests
{
    public class QuestManager : NetworkBehaviour, IQuestQuery
    {
        private const ulong StandaloneClientId = 0;

        [Header("Quest Data")]
        [SerializeField] private QuestDataSO[] allQuests;

        [Header("Debug")]
        [SerializeField] private bool enableDebugKeys = true;

        private readonly Dictionary<ulong, PlayerQuestState> playerStates = new();
        private readonly Dictionary<string, QuestDataSO> questLookup = new();

        private void Awake()
        {
            questLookup.Clear();

            if (allQuests == null)
                return;

            foreach (QuestDataSO quest in allQuests)
            {
                if (quest != null && !string.IsNullOrWhiteSpace(quest.questID))
                    questLookup[quest.questID] = quest;
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

            if (!IsServer)
                return;

            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<ItemLootedEvent>(OnItemLooted);
            EventBus.Subscribe<ZoneEnteredEvent>(OnZoneEntered);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Subscribe<ExtractionCompletedEvent>(OnExtractionCompleted);
            EventBus.Subscribe<SceneChangedEvent>(OnSceneChanged);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
                EventBus.Unsubscribe<ItemLootedEvent>(OnItemLooted);
                EventBus.Unsubscribe<ZoneEnteredEvent>(OnZoneEntered);
                EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
                EventBus.Unsubscribe<ExtractionCompletedEvent>(OnExtractionCompleted);
                EventBus.Unsubscribe<SceneChangedEvent>(OnSceneChanged);
            }

            UnregisterQuestServicesIfCurrent();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!enableDebugKeys || !CanWriteQuestState())
                return;

            if (Input.GetKeyDown(KeyCode.Home))
                DebugForceCompleteNext();
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

        public PlayerQuestState GetPlayerState(ulong clientId)
        {
            if (!playerStates.TryGetValue(clientId, out PlayerQuestState state))
            {
                state = new PlayerQuestState();
                playerStates[clientId] = state;
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
            PlayerQuestState state = GetPlayerState(clientId);
            state.ReadFromCloudProgress(progress);
            InitializeActiveQuestRuntimeProgress(state);

            Debug.Log($"[QuestManager] Restored client {clientId}: active={state.ActiveQuestIds.Count}, completed={state.CompletedQuestIds.Count}, rewardClaimed={state.RewardClaimedQuestIds.Count}");

            if (state.ActiveQuestIds.Count == 0 && state.CompletedQuestIds.Count == 0)
                AcceptQuest(clientId, "Q1");

            PublishQuestTrackerSnapshot(clientId);
        }

        public bool AcceptQuest(ulong clientId, string questId)
        {
            if (!CanWriteQuestState()) return false;
            if (!questLookup.TryGetValue(questId, out QuestDataSO questData)) return false;

            PlayerQuestState state = GetPlayerState(clientId);
            if (state.ActiveQuestIds.Contains(questId) || state.CompletedQuestIds.Contains(questId))
                return false;

            if (!string.IsNullOrEmpty(questData.prerequisiteQuestID) &&
                !state.CompletedQuestIds.Contains(questData.prerequisiteQuestID))
                return false;

            state.ActiveQuestIds.Add(questId);
            InitializeQuestRuntimeProgress(state, questData);

            PublishQuestAccepted(clientId, questId);

            Debug.Log($"[QuestManager] Client {clientId} accepted {questId}");
            TryAutoAcceptSideQuests(clientId);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AcceptQuestServerRpc(string questId, RpcParams rpcParams = default)
        {
            AcceptQuest(rpcParams.Receive.SenderClientId, questId);
        }

        public void ReportProgress(ulong clientId, ObjectiveType type, string targetId, int amount)
        {
            if (!CanWriteQuestState()) return;

            PlayerQuestState state = GetPlayerState(clientId);
            List<string> activeSnapshot = new(state.ActiveQuestIds);

            foreach (string questId in activeSnapshot)
            {
                if (!questLookup.TryGetValue(questId, out QuestDataSO questData) || questData.objectives == null)
                    continue;

                foreach (QuestObjectiveData objective in questData.objectives)
                {
                    if (objective.type != type || objective.targetID != targetId)
                        continue;

                    int newCount = state.AddProgress(questId, targetId, amount, objective.requiredCount);
                    PublishQuestProgress(clientId, questId, targetId, type, newCount, objective.requiredCount);

                    if (state.AreAllObjectivesComplete(questId) && !state.PendingCompletionIds.Contains(questId))
                    {
                        state.PendingCompletionIds.Add(questId);
                        PublishQuestPending(clientId, questId, targetId, type, newCount, objective.requiredCount);
                    }
                }
            }
        }

        public int GetObjectiveProgress(ulong clientId, string questId, string targetId)
            => GetPlayerState(clientId).GetProgress(questId, targetId);

        public bool TryGetQuestData(string questId, out QuestDataSO questData)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                questData = null;
                return false;
            }

            return questLookup.TryGetValue(questId, out questData);
        }

        public bool IsQuestCompleted(ulong clientId, string questId)
            => GetPlayerState(clientId).CompletedQuestIds.Contains(questId);

        public bool IsQuestActive(ulong clientId, string questId)
            => GetPlayerState(clientId).ActiveQuestIds.Contains(questId);

        public bool IsQuestRewardClaimed(ulong clientId, string questId)
            => GetPlayerState(clientId).RewardClaimedQuestIds.Contains(questId);

        public bool CanClaimReward(ulong clientId, string questId)
        {
            PlayerQuestState state = GetPlayerState(clientId);
            return state.CompletedQuestIds.Contains(questId) &&
                   !state.RewardClaimedQuestIds.Contains(questId);
        }

        public bool IsQuestCompleted(string questId)
            => IsQuestCompleted(GetLocalClientIdForState(), questId);

        public bool IsQuestActive(string questId)
            => IsQuestActive(GetLocalClientIdForState(), questId);

        public bool IsQuestRewardClaimed(string questId)
            => IsQuestRewardClaimed(GetLocalClientIdForState(), questId);

        public bool CanClaimReward(string questId)
            => CanClaimReward(GetLocalClientIdForState(), questId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClaimQuestRewardServerRpc(string questId, RpcParams rpcParams = default)
        {
            ClaimReward(rpcParams.Receive.SenderClientId, questId);
        }

        public bool ClaimReward(ulong clientId, string questId)
        {
            if (!CanWriteQuestState()) return false;
            if (!CanClaimReward(clientId, questId)) return false;
            if (!questLookup.TryGetValue(questId, out QuestDataSO questData)) return false;
            if (!CanGrantRewards(clientId, questData)) return false;

            GrantRewards(clientId, questData);

            PlayerQuestState state = GetPlayerState(clientId);
            state.RewardClaimedQuestIds.Add(questId);

            EventBus.Publish(new QuestRewardClaimedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = clientId
            });

            if (HasNetworkSession())
            {
                NotifyQuestRewardClaimedClientRpc(questId, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }

            Debug.Log($"[QuestManager] Client {clientId} claimed reward for {questId}");
            return true;
        }

        public void ResetRaidQuestProgress(ulong clientId)
        {
            if (!CanWriteQuestState()) return;

            PlayerQuestState state = GetPlayerState(clientId);
            state.ResetRaidQuestProgress();
            PublishResetProgress(clientId, state);
        }

        private void CompleteQuest(ulong clientId, string questId)
        {
            if (!CanWriteQuestState()) return;

            PlayerQuestState state = GetPlayerState(clientId);
            if (state.CompletedQuestIds.Contains(questId))
                return;

            state.ActiveQuestIds.Remove(questId);
            state.PendingCompletionIds.Remove(questId);
            state.CompletedQuestIds.Add(questId);

            if (!questLookup.TryGetValue(questId, out QuestDataSO questData))
                return;

            if (!string.IsNullOrEmpty(questData.unlockZoneID))
                state.UnlockedZones.Add(questData.unlockZoneID);

            EventBus.Publish(new QuestCompletedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = clientId,
                unlockZoneId = new FixedString64Bytes(questData.unlockZoneID ?? string.Empty)
            });

            if (HasNetworkSession())
            {
                NotifyQuestCompletedClientRpc(questId, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }

            Debug.Log($"[QuestManager] Client {clientId} completed {questId}");
            TryAutoAcceptNextQuest(clientId, questId);
        }

        private void ConfirmRaidQuestCompletions(ulong clientId)
        {
            PlayerQuestState state = GetPlayerState(clientId);
            List<string> activeSnapshot = new(state.ActiveQuestIds);

            foreach (string questId in activeSnapshot)
            {
                if (state.AreAllObjectivesComplete(questId))
                    CompleteQuest(clientId, questId);
            }

            ResetRaidQuestProgress(clientId);
        }

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            ResetRaidQuestProgress(e.victimClientId);
        }

        private void OnExtractionCompleted(ExtractionCompletedEvent e)
        {
            if (!CanWriteQuestState()) return;
            ConfirmRaidQuestCompletions(e.clientId);
        }

        private void OnSceneChanged(SceneChangedEvent e)
        {
            if (!CanWriteQuestState()) return;

            foreach (ulong clientId in new List<ulong>(playerStates.Keys))
                ResetRaidQuestProgress(clientId);
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            if (!CanWriteQuestState()) return;

            string enemyId = e.enemyId.ToString();
            if (!string.IsNullOrEmpty(enemyId))
                ReportProgress(e.attackerClientId, ObjectiveType.Kill, enemyId, 1);

            if (!e.isBoss)
            {
                if (enemyId != "Enemy_Zone1_Any")
                    ReportProgress(e.attackerClientId, ObjectiveType.Kill, "Enemy_Zone1_Any", 1);
                return;
            }

            if (string.IsNullOrEmpty(enemyId))
                ReportProgress(e.attackerClientId, ObjectiveType.Kill, "Boss_Stage2_All", 1);
        }

        private void OnItemLooted(ItemLootedEvent e)
        {
            if (!CanWriteQuestState()) return;

            string itemId = e.itemId.ToString();
            if (!string.IsNullOrEmpty(itemId))
                ReportProgress(e.clientId, ObjectiveType.Collect, itemId, Mathf.Max(1, e.amount));
        }

        private void OnZoneEntered(ZoneEnteredEvent e)
        {
            if (!CanWriteQuestState()) return;
            ReportProgress(e.clientId, ObjectiveType.Reach, e.zoneId.ToString(), 1);
        }

        private void TryAutoAcceptSideQuests(ulong clientId)
        {
            if (allQuests == null)
                return;

            PlayerQuestState state = GetPlayerState(clientId);
            foreach (QuestDataSO quest in allQuests)
            {
                if (quest == null || !quest.isSideQuest) continue;
                if (state.ActiveQuestIds.Contains(quest.questID)) continue;
                if (state.CompletedQuestIds.Contains(quest.questID)) continue;
                if (!string.IsNullOrEmpty(quest.prerequisiteQuestID) &&
                    !state.CompletedQuestIds.Contains(quest.prerequisiteQuestID))
                    continue;

                AcceptQuest(clientId, quest.questID);
            }
        }

        private void TryAutoAcceptNextQuest(ulong clientId, string completedQuestId)
        {
            if (allQuests == null)
                return;

            foreach (QuestDataSO quest in allQuests)
            {
                if (quest == null || quest.isSideQuest) continue;
                if (quest.prerequisiteQuestID != completedQuestId) continue;

                AcceptQuest(clientId, quest.questID);
                break;
            }
        }

        private void InitializeActiveQuestRuntimeProgress(PlayerQuestState state)
        {
            foreach (string questId in state.ActiveQuestIds)
            {
                if (questLookup.TryGetValue(questId, out QuestDataSO questData))
                    InitializeQuestRuntimeProgress(state, questData);
            }
        }

        private static void InitializeQuestRuntimeProgress(PlayerQuestState state, QuestDataSO questData)
        {
            if (state == null || questData == null || questData.objectives == null)
                return;

            foreach (QuestObjectiveData objective in questData.objectives)
            {
                string key = PlayerQuestState.MakeKey(questData.questID, objective.targetID);
                if (!state.RaidQuestProgress.ContainsKey(key))
                    state.RaidQuestProgress[key] = (0, objective.requiredCount);
            }
        }

        private void PublishQuestTrackerSnapshot(ulong clientId)
        {
            PlayerQuestState state = GetPlayerState(clientId);

            foreach (string questId in state.ActiveQuestIds)
            {
                if (!TryGetQuestData(questId, out QuestDataSO questData) ||
                    questData.objectives == null ||
                    questData.objectives.Length == 0)
                {
                    continue;
                }

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

        private void PublishQuestAccepted(ulong clientId, string questId)
        {
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
        }

        private void PublishQuestProgress(ulong clientId, string questId, string targetId, ObjectiveType type, int currentCount, int requiredCount)
        {
            EventBus.Publish(new QuestProgressEvent
            {
                questId = new FixedString64Bytes(questId),
                objectiveType = type,
                currentCount = currentCount,
                requiredCount = requiredCount,
                clientId = clientId,
                targetId = new FixedString64Bytes(targetId)
            });

            if (HasNetworkSession())
            {
                NotifyQuestProgressClientRpc(questId, targetId, currentCount, requiredCount, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }
        }

        private void PublishQuestPending(ulong clientId, string questId, string targetId, ObjectiveType type, int currentCount, int requiredCount)
        {
            EventBus.Publish(new QuestTrackerSnapshotEvent
            {
                questId = new FixedString64Bytes(questId),
                objectiveType = type,
                currentCount = currentCount,
                requiredCount = requiredCount,
                clientId = clientId,
                targetId = new FixedString64Bytes(targetId),
                isPendingCompletion = true
            });

            if (HasNetworkSession())
            {
                NotifyQuestPendingClientRpc(questId, targetId, type, currentCount, requiredCount, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
            }
        }

        private void PublishResetProgress(ulong clientId, PlayerQuestState state)
        {
            foreach (string questId in state.ActiveQuestIds)
            {
                if (!questLookup.TryGetValue(questId, out QuestDataSO questData) || questData.objectives == null)
                    continue;

                foreach (QuestObjectiveData objective in questData.objectives)
                {
                    EventBus.Publish(new QuestProgressEvent
                    {
                        questId = new FixedString64Bytes(questId),
                        objectiveType = objective.type,
                        currentCount = 0,
                        requiredCount = objective.requiredCount,
                        clientId = clientId,
                        targetId = new FixedString64Bytes(objective.targetID)
                    });

                    if (HasNetworkSession())
                    {
                        NotifyQuestProgressClientRpc(questId, objective.targetID, 0, objective.requiredCount, new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                        });
                    }
                }
            }
        }

        private bool CanGrantRewards(ulong clientId, QuestDataSO questData)
        {
            if (questData.rewards == null)
                return true;

            GridInventory inventory = ResolveClientInventory(clientId);
            WalletSystem wallet = ResolveClientWallet(clientId);
            IItemDatabase itemDatabase = ResolveItemDatabase();

            foreach (QuestReward reward in questData.rewards)
            {
                if (reward.amount <= 0)
                    continue;

                if (reward.type == RewardType.Credits)
                {
                    if (wallet == null)
                        return false;

                    continue;
                }

                if (inventory == null || itemDatabase == null)
                    return false;

                ItemDataSO itemData = itemDatabase.GetById(reward.itemID);
                if (itemData == null || !inventory.CanAddItem(itemData, reward.amount))
                    return false;
            }

            return true;
        }

        private void GrantRewards(ulong clientId, QuestDataSO questData)
        {
            if (questData.rewards == null)
                return;

            WalletSystem wallet = ResolveClientWallet(clientId);
            GridInventory inventory = ResolveClientInventory(clientId);
            IItemDatabase itemDatabase = ResolveItemDatabase();

            foreach (QuestReward reward in questData.rewards)
            {
                if (reward.amount <= 0)
                    continue;

                switch (reward.type)
                {
                    case RewardType.Credits:
                        if (wallet != null && wallet.IsSpawned)
                            wallet.Earn(reward.amount);
                        else
                            wallet?.EarnLocalTest(reward.amount);
                        break;

                    case RewardType.Item:
                    case RewardType.FacilityMaterial:
                        ItemDataSO itemData = itemDatabase?.GetById(reward.itemID);
                        if (itemData != null)
                            inventory?.TryAddItem(itemData, reward.amount);
                        break;
                }
            }
        }

        private static WalletSystem ResolveClientWallet(ulong clientId)
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                WalletSystem wallet = client.PlayerObject.GetComponent<WalletSystem>();
                if (wallet != null)
                    return wallet;

                wallet = client.PlayerObject.GetComponentInChildren<WalletSystem>(true);
                if (wallet != null)
                    return wallet;
            }

            foreach (WalletSystem wallet in FindObjectsByType<WalletSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (wallet != null && (!wallet.IsSpawned || wallet.OwnerClientId == clientId))
                    return wallet;
            }

            return null;
        }

        private static GridInventory ResolveClientInventory(ulong clientId)
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                GridInventory inventory = client.PlayerObject.GetComponent<GridInventory>();
                if (inventory != null)
                    return inventory;

                inventory = client.PlayerObject.GetComponentInChildren<GridInventory>(true);
                if (inventory != null)
                    return inventory;
            }

            foreach (GridInventory inventory in FindObjectsByType<GridInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (inventory != null && (!inventory.IsSpawned || inventory.OwnerClientId == clientId))
                    return inventory;
            }

            return null;
        }

        private static IItemDatabase ResolveItemDatabase()
        {
            IItemDatabase itemDatabase = ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase != null)
                return itemDatabase;

            return FindFirstObjectByType<ItemDatabase>(FindObjectsInactive.Include);
        }

        private ObjectiveType ResolveObjectiveType(string questId, string targetId)
        {
            if (!TryGetQuestData(questId, out QuestDataSO questData) || questData.objectives == null)
                return ObjectiveType.Kill;

            foreach (QuestObjectiveData objective in questData.objectives)
            {
                if (objective.targetID == targetId)
                    return objective.type;
            }

            return ObjectiveType.Kill;
        }

        private void DebugForceCompleteNext()
        {
            ulong localClientId = GetLocalClientIdForState();
            PlayerQuestState state = GetPlayerState(localClientId);

            if (allQuests == null)
                return;

            foreach (QuestDataSO quest in allQuests)
            {
                if (quest == null || quest.isSideQuest) continue;
                if (state.CompletedQuestIds.Contains(quest.questID)) continue;

                if (!state.ActiveQuestIds.Contains(quest.questID))
                    AcceptQuest(localClientId, quest.questID);

                InitializeQuestRuntimeProgress(state, quest);
                foreach (QuestObjectiveData objective in quest.objectives)
                    state.RaidQuestProgress[PlayerQuestState.MakeKey(quest.questID, objective.targetID)] = (objective.requiredCount, objective.requiredCount);

                CompleteQuest(localClientId, quest.questID);
                return;
            }
        }

        [ClientRpc]
        private void NotifyQuestAcceptedClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            PlayerQuestState state = GetPlayerState(GetLocalClientIdForState());
            state.ActiveQuestIds.Add(questId);
            if (questLookup.TryGetValue(questId, out QuestDataSO questData))
                InitializeQuestRuntimeProgress(state, questData);

            EventBus.Publish(new QuestAcceptedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = GetLocalClientIdForState()
            });
        }

        [ClientRpc]
        private void NotifyQuestProgressClientRpc(string questId, string targetId, int currentCount, int requiredCount, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            PlayerQuestState state = GetPlayerState(GetLocalClientIdForState());
            state.RaidQuestProgress[PlayerQuestState.MakeKey(questId, targetId)] = (currentCount, requiredCount);

            EventBus.Publish(new QuestProgressEvent
            {
                questId = new FixedString64Bytes(questId),
                objectiveType = ResolveObjectiveType(questId, targetId),
                currentCount = currentCount,
                requiredCount = requiredCount,
                clientId = GetLocalClientIdForState(),
                targetId = new FixedString64Bytes(targetId)
            });
        }

        [ClientRpc]
        private void NotifyQuestPendingClientRpc(string questId, string targetId, ObjectiveType objectiveType, int currentCount, int requiredCount, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            PlayerQuestState state = GetPlayerState(GetLocalClientIdForState());
            state.PendingCompletionIds.Add(questId);

            EventBus.Publish(new QuestTrackerSnapshotEvent
            {
                questId = new FixedString64Bytes(questId),
                objectiveType = objectiveType,
                currentCount = currentCount,
                requiredCount = requiredCount,
                clientId = GetLocalClientIdForState(),
                targetId = new FixedString64Bytes(targetId),
                isPendingCompletion = true
            });
        }

        [ClientRpc]
        private void NotifyQuestCompletedClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            PlayerQuestState state = GetPlayerState(GetLocalClientIdForState());
            state.ActiveQuestIds.Remove(questId);
            state.PendingCompletionIds.Remove(questId);
            state.CompletedQuestIds.Add(questId);

            string unlockZoneId = string.Empty;
            if (TryGetQuestData(questId, out QuestDataSO questData))
            {
                unlockZoneId = questData.unlockZoneID ?? string.Empty;
                if (!string.IsNullOrEmpty(unlockZoneId))
                    state.UnlockedZones.Add(unlockZoneId);
            }

            EventBus.Publish(new QuestCompletedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = GetLocalClientIdForState(),
                unlockZoneId = new FixedString64Bytes(unlockZoneId)
            });
        }

        [ClientRpc]
        private void NotifyQuestRewardClaimedClientRpc(string questId, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            PlayerQuestState state = GetPlayerState(GetLocalClientIdForState());
            state.RewardClaimedQuestIds.Add(questId);

            EventBus.Publish(new QuestRewardClaimedEvent
            {
                questId = new FixedString64Bytes(questId),
                clientId = GetLocalClientIdForState()
            });
        }

        public void OnPlayerDisconnected(ulong clientId)
        {
            Debug.Log($"[QuestManager] Client {clientId} disconnected. State preserved.");
        }

        public void ClearAllPlayerStates()
        {
            playerStates.Clear();
            Debug.Log("[QuestManager] All player states cleared.");
        }
    }
}

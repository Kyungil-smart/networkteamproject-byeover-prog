using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Actors.Player;
using DeadZone.Core;
using DeadZone.Network;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// Info_Team 영역: 로컬을 제외한 접속 플레이어 HP를 슬롯에 표시합니다.
    /// PlayerHpChangedEvent와 접속자 변화로만 갱신합니다(Update 폴링 없음).
    /// </summary>
    public sealed class TeamInfoHUD : MonoBehaviour
    {
        [TitleGroup("팀 HUD")]
        [BoxGroup("팀 HUD/루트")]
        [Required, SerializeField]
        private GameObject infoTeamRoot;

        [TitleGroup("팀 HUD")]
        [BoxGroup("팀 HUD/슬롯")]
        [InfoBox("정확히 3개의 TeamHpSlotUI를 순서대로 할당하세요 (0~2).")]
        [SerializeField]
        private TeamHpSlotUI[] teamSlots = new TeamHpSlotUI[3];

        [TitleGroup("팀 HUD")]
        [BoxGroup("팀 HUD/수치")]
        [MinValue(1f), SerializeField, Tooltip("PlayerObject를 아직 못 찾을 때만 쓰는 폴백. 실제 최대 체력은 PlayerHealthSystem.ReplicatedMaxHp(서버 동기화)를 사용합니다.")]
        private float maxHP = 100f;

        private readonly Dictionary<ulong, TeamHpSlotUI> clientIdToSlot = new();

        private readonly Dictionary<ulong, PlayerHealthSystem> clientIdToHealth = new();

        private readonly List<Action> maxHpUnsubscribers = new();

        private NetworkManager registeredNetworkManager;

        private bool loggedMissingSlots;

        private bool loggedRootSelfReference;

        private bool loggedRosterSnapshot;

        private ulong lastLoggedLocalClientId = ulong.MaxValue;

        private int lastLoggedSpawnedPlayerCount = -1;

        private int lastLoggedTeammateCount = -1;

        private int lastLoggedUsableSlotCount = -1;

        private void OnEnable()
        {
            loggedMissingSlots = false;
            EventBus.Subscribe<PlayerHpChangedEvent>(OnPlayerHpChanged);
            RegisterNetworkCallbacks();
            StartCoroutine(RebuildAfterFrames());
            StartCoroutine(RebuildWhileNetworkBooting());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnPlayerHpChanged);
            UnregisterNetworkCallbacks();
            ClearMaxHpSubscriptions();
            ClearAllSlots();
        }

        private IEnumerator RebuildAfterFrames()
        {
            yield return null;
            yield return null;
            RebuildTeamRoster();
        }

        /// <summary>
        /// NetworkManager가 HUD보다 늦게 Listen 하거나, PlayerObject가 몇 프레임 뒤에 붙는 경우를 흡수합니다.
        /// </summary>
        private IEnumerator RebuildWhileNetworkBooting()
        {
            const int maxSteps = 300;
            for (int i = 0; i < maxSteps; i++)
            {
                yield return null;
                RebuildTeamRoster();
            }
        }

        private void RegisterNetworkCallbacks()
        {
            registeredNetworkManager = NetworkManager.Singleton;
            if (registeredNetworkManager == null)
                return;

            registeredNetworkManager.OnClientConnectedCallback += OnClientsChanged;
            registeredNetworkManager.OnClientDisconnectCallback += OnClientsChanged;
        }

        private void UnregisterNetworkCallbacks()
        {
            if (registeredNetworkManager != null)
            {
                registeredNetworkManager.OnClientConnectedCallback -= OnClientsChanged;
                registeredNetworkManager.OnClientDisconnectCallback -= OnClientsChanged;
                registeredNetworkManager = null;
            }
        }

        private void OnClientsChanged(ulong _)
        {
            RebuildTeamRoster();
        }

        private void OnPlayerHpChanged(PlayerHpChangedEvent e)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
                return;

            ulong localId = nm.LocalClientId;
            if (e.clientId == localId)
                return;

            if (!clientIdToSlot.TryGetValue(e.clientId, out TeamHpSlotUI slot))
            {
                RebuildTeamRoster();

                if (!clientIdToSlot.TryGetValue(e.clientId, out slot))
                    return;
            }

            float maxForClient = ResolveMaxHpForClient(e.clientId);
            slot.SetHp(e.newValue, maxForClient);
        }

        [Button("팀 슬롯 재구성(에디터)")]
        private void EditorRebuild()
        {
            if (!Application.isPlaying)
                return;

            RebuildTeamRoster();
        }

        private void RebuildTeamRoster()
        {
            EnsureTeamSlotsPopulatedFromHierarchy();
            ClearMaxHpSubscriptions();
            ClearAllSlots();
            clientIdToSlot.Clear();
            clientIdToHealth.Clear();

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                SetTeamRootActive(false);
                return;
            }

            ulong localId = nm.LocalClientId;

            List<TeamPlayerEntry> spawnedPlayers = CollectSpawnedPlayers(nm);
            spawnedPlayers.Sort(CompareTeamPlayerEntry);

            List<TeamPlayerEntry> teammates = CollectTeammates(spawnedPlayers, localId);
            LogRosterSnapshot(localId, spawnedPlayers.Count, teammates.Count, CountNonNullSlots(ResolveTeamSlots()));

            int teammateCount = teammates.Count;
            if (teammateCount == 0)
            {
                SetTeamRootActive(false);
                return;
            }

            SetTeamRootActive(true);

            TeamHpSlotUI[] resolved = ResolveTeamSlots();
            int usableSlots = CountNonNullSlots(resolved);
            int slotsToUse = Mathf.Min(usableSlots, teammateCount);

            if (teammateCount > 0 && usableSlots == 0)
            {
                if (!loggedMissingSlots)
                {
                    loggedMissingSlots = true;
                    Debug.LogWarning(
                        "[TeamInfoHUD] Teammates were found, but no TeamHpSlotUI slots are assigned. " +
                        "Assign TeamHpSlotUI components to Team Slots or place them under infoTeamRoot.",
                        this);
                }

                SetTeamRootActive(false);
                return;
            }

            if (teammateCount > usableSlots && !loggedMissingSlots)
            {
                loggedMissingSlots = true;
                Debug.LogWarning(
                    $"[TeamInfoHUD] Teammate count ({teammateCount}) is greater than usable TeamHpSlotUI slots ({usableSlots}). " +
                    "Only assigned slots can be displayed.",
                    this);
            }

            int teammateIndex = 0;
            for (int i = 0; i < resolved.Length; i++)
            {
                TeamHpSlotUI slot = resolved[i];
                if (slot == null)
                    continue;

                if (teammateIndex < slotsToUse)
                {
                    TeamPlayerEntry teammate = teammates[teammateIndex];
                    BindSlot(slot, teammate, nm);
                    clientIdToSlot[teammate.ClientId] = slot;

                    if (teammate.Health != null)
                        clientIdToHealth[teammate.ClientId] = teammate.Health;

                    teammateIndex++;
                }
                else
                {
                    slot.Clear();
                }
            }
        }

        private static int CountNonNullSlots(TeamHpSlotUI[] slots)
        {
            if (slots == null)
                return 0;

            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    n++;
            }

            return n;
        }

        /// <summary>
        /// teamSlots가 비어 있으면 infoTeamRoot 하위에서 TeamHpSlotUI를 찾아 채웁니다(수동 배열이 우선).
        /// </summary>
        private void EnsureTeamSlotsPopulatedFromHierarchy()
        {
            if (infoTeamRoot == null)
                return;

            if (HasAnyTeamSlotReference())
                return;

            TeamHpSlotUI[] found = infoTeamRoot.GetComponentsInChildren<TeamHpSlotUI>(true);
            if (found == null || found.Length == 0)
                return;

            System.Array.Sort(found, CompareTeamSlotHierarchy);
            int take = Mathf.Min(3, found.Length);
            teamSlots = new TeamHpSlotUI[take];
            for (int i = 0; i < take; i++)
                teamSlots[i] = found[i];
        }

        private bool HasAnyTeamSlotReference()
        {
            if (teamSlots == null || teamSlots.Length == 0)
                return false;

            for (int i = 0; i < teamSlots.Length; i++)
            {
                if (teamSlots[i] != null)
                    return true;
            }

            return false;
        }

        private static int CompareTeamSlotHierarchy(TeamHpSlotUI a, TeamHpSlotUI b)
        {
            if (a == null || b == null)
                return 0;

            return string.CompareOrdinal(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform));
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return string.Empty;

            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private TeamHpSlotUI[] ResolveTeamSlots()
        {
            if (teamSlots != null && teamSlots.Length > 0)
                return teamSlots;

            return System.Array.Empty<TeamHpSlotUI>();
        }

        private static List<TeamPlayerEntry> CollectSpawnedPlayers(NetworkManager nm)
        {
            List<TeamPlayerEntry> list = new List<TeamPlayerEntry>(4);
            if (nm == null || nm.SpawnManager == null || nm.SpawnManager.SpawnedObjectsList == null)
                return list;

            foreach (NetworkObject netObj in nm.SpawnManager.SpawnedObjectsList)
            {
                if (!TryCreatePlayerEntry(netObj, out TeamPlayerEntry entry))
                    continue;

                list.Add(entry);
            }

            return list;
        }

        private static bool TryCreatePlayerEntry(NetworkObject netObj, out TeamPlayerEntry entry)
        {
            entry = default;
            if (netObj == null)
                return false;

            PlayerHealthSystem health = netObj.GetComponent<PlayerHealthSystem>();
            if (health == null)
                health = netObj.GetComponentInChildren<PlayerHealthSystem>(true);

            Component playerStats = null;
            if (health == null)
                playerStats = FindPlayerStats(netObj);

            if (health == null && playerStats == null)
                return false;

            entry = new TeamPlayerEntry(netObj.OwnerClientId, netObj, health, playerStats);
            return true;
        }

        private static Component FindPlayerStats(NetworkObject netObj)
        {
            if (netObj == null)
                return null;

            Component direct = netObj.GetComponent("PlayerStats");
            if (direct != null)
                return direct;

            Component[] components = netObj.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name == "PlayerStats")
                    return component;
            }

            return null;
        }

        private static List<TeamPlayerEntry> CollectTeammates(List<TeamPlayerEntry> spawnedPlayers, ulong localId)
        {
            List<TeamPlayerEntry> teammates = new List<TeamPlayerEntry>(4);
            if (spawnedPlayers == null)
                return teammates;

            HashSet<ulong> seenClientIds = new HashSet<ulong>();
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                TeamPlayerEntry entry = spawnedPlayers[i];
                if (entry.ClientId == localId)
                    continue;

                if (!seenClientIds.Add(entry.ClientId))
                    continue;

                teammates.Add(entry);
            }

            return teammates;
        }

        private static int CompareTeamPlayerEntry(TeamPlayerEntry a, TeamPlayerEntry b)
        {
            return a.ClientId.CompareTo(b.ClientId);
        }

        private void BindSlot(TeamHpSlotUI slot, TeamPlayerEntry teammate, NetworkManager nm)
        {
            Color color = ResolveTeamColor(teammate, nm);
            float hp;
            float maxForClient = Mathf.Max(1f, maxHP);
            PlayerHealthSystem health = teammate.Health;

            if (health != null)
            {
                hp = health.CurrentHP.Value;
                maxForClient = ResolveMaxHpForHealth(health);
            }
            else
            {
                hp = maxForClient;
            }

            slot.Bind(teammate.ClientId, color, hp, maxForClient);
            RegisterMaxHpSubscription(health, slot);
        }

        private float ResolveMaxHpForClient(ulong clientId)
        {
            if (clientIdToHealth.TryGetValue(clientId, out PlayerHealthSystem health))
                return ResolveMaxHpForHealth(health);

            return Mathf.Max(1f, maxHP);
        }

        private static float ResolveMaxHpForHealth(PlayerHealthSystem health)
        {
            if (health == null)
                return 1f;

            if (health.IsSpawned)
                return Mathf.Max(1f, health.ReplicatedMaxHp.Value);

            return Mathf.Max(1f, health.MaxHP);
        }

        private void RegisterMaxHpSubscription(PlayerHealthSystem health, TeamHpSlotUI slot)
        {
            if (health == null || !health.IsSpawned || slot == null)
                return;

            void OnMaxHpChanged(float _, float newMax)
            {
                if (slot == null)
                    return;

                float m = Mathf.Max(1f, newMax);
                slot.SetHp(health.CurrentHP.Value, m);
            }

            health.ReplicatedMaxHp.OnValueChanged += OnMaxHpChanged;
            maxHpUnsubscribers.Add(() =>
            {
                if (health != null)
                    health.ReplicatedMaxHp.OnValueChanged -= OnMaxHpChanged;
            });
        }

        private void ClearMaxHpSubscriptions()
        {
            for (int i = 0; i < maxHpUnsubscribers.Count; i++)
                maxHpUnsubscribers[i]?.Invoke();

            maxHpUnsubscribers.Clear();
        }

        private static Color ResolveTeamColor(TeamPlayerEntry teammate, NetworkManager nm)
        {
            NetworkObject netObj = teammate.NetworkObject != null
                ? teammate.NetworkObject
                : teammate.Health != null ? teammate.Health.NetworkObject : null;

            if (netObj != null)
            {
                PlayerTeamIdentity identity = netObj.GetComponent<PlayerTeamIdentity>()
                    ?? netObj.GetComponentInChildren<PlayerTeamIdentity>(true);
                if (identity != null)
                    return identity.CurrentColor;
            }

            if (LobbyTeamColorCache.TryGetColor(teammate.ClientId, out Color32 cached))
                return cached;

            return Color.white;
        }

        private void ClearAllSlots()
        {
            if (teamSlots == null)
                return;

            for (int i = 0; i < teamSlots.Length; i++)
            {
                if (teamSlots[i] != null)
                    teamSlots[i].Clear();
            }
        }

        private void SetTeamRootActive(bool active)
        {
            if (infoTeamRoot == null)
                return;

            if (infoTeamRoot == gameObject)
            {
                if (!loggedRootSelfReference)
                {
                    loggedRootSelfReference = true;
                    Debug.LogWarning(
                        "[TeamInfoHUD] TeamInfoHUD is attached to infoTeamRoot. Attach this component to PlayerHUD or a persistent HUD manager object.",
                        this);
                }

                return;
            }

            infoTeamRoot.SetActive(active);
        }

        private void LogRosterSnapshot(ulong localClientId, int spawnedPlayerCount, int teammateCount, int usableSlotCount)
        {
            if (lastLoggedLocalClientId == localClientId
                && lastLoggedSpawnedPlayerCount == spawnedPlayerCount
                && lastLoggedTeammateCount == teammateCount
                && lastLoggedUsableSlotCount == usableSlotCount)
            {
                return;
            }

            lastLoggedLocalClientId = localClientId;
            lastLoggedSpawnedPlayerCount = spawnedPlayerCount;
            lastLoggedTeammateCount = teammateCount;
            lastLoggedUsableSlotCount = usableSlotCount;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[TeamInfoHUD] Rebuild roster: localId={localClientId}, teammateCount={teammateCount}, spawnedPlayers={spawnedPlayerCount}, usableSlots={usableSlotCount}",
                this);
#else
            if (loggedRosterSnapshot)
                return;

            loggedRosterSnapshot = true;
            Debug.Log(
                $"[TeamInfoHUD] Rebuild roster: localId={localClientId}, teammateCount={teammateCount}, spawnedPlayers={spawnedPlayerCount}, usableSlots={usableSlotCount}",
                this);
#endif
        }

        private readonly struct TeamPlayerEntry
        {
            public readonly ulong ClientId;
            public readonly NetworkObject NetworkObject;
            public readonly PlayerHealthSystem Health;
            public readonly Component PlayerStats;

            public TeamPlayerEntry(
                ulong clientId,
                NetworkObject networkObject,
                PlayerHealthSystem health,
                Component playerStats)
            {
                ClientId = clientId;
                NetworkObject = networkObject;
                Health = health;
                PlayerStats = playerStats;
            }
        }
    }
}

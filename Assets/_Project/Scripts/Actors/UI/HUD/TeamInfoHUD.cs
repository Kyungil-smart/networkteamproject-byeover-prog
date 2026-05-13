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

        private readonly List<Action> maxHpUnsubscribers = new();

        private NetworkManager registeredNetworkManager;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHpChangedEvent>(OnPlayerHpChanged);
            RegisterNetworkCallbacks();
            StartCoroutine(RebuildAfterFrames());
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
                if (ShouldShowTeammate(e.clientId, localId, nm))
                    RebuildTeamRoster();

                if (!clientIdToSlot.TryGetValue(e.clientId, out slot))
                    return;
            }

            float maxForClient = ResolveMaxHpForClient(e.clientId, nm);
            slot.SetHp(e.newValue, maxForClient);
        }

        private static bool ShouldShowTeammate(ulong clientId, ulong localId, NetworkManager nm)
        {
            if (clientId == localId)
                return false;

            IReadOnlyList<ulong> ids = nm.ConnectedClientsIds;
            if (ids == null)
                return false;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == clientId)
                    return true;
            }

            return false;
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
            ClearMaxHpSubscriptions();
            ClearAllSlots();
            clientIdToSlot.Clear();

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.LocalClient == null)
            {
                SetTeamRootActive(false);
                return;
            }

            ulong localId = nm.LocalClientId;

            List<ulong> others = CollectOtherClientIds(nm, localId);
            others.Sort();

            int teammateCount = others.Count;
            if (teammateCount == 0)
            {
                SetTeamRootActive(false);
                return;
            }

            SetTeamRootActive(true);

            int slotsToUse = Mathf.Min(3, teammateCount);
            TeamHpSlotUI[] resolved = ResolveTeamSlots();

            for (int i = 0; i < resolved.Length; i++)
            {
                TeamHpSlotUI slot = resolved[i];
                if (slot == null)
                    continue;

                if (i < slotsToUse)
                {
                    ulong cid = others[i];
                    BindSlot(slot, cid, nm);
                    clientIdToSlot[cid] = slot;
                }
                else
                {
                    slot.Clear();
                }
            }
        }

        private TeamHpSlotUI[] ResolveTeamSlots()
        {
            if (teamSlots != null && teamSlots.Length > 0)
                return teamSlots;

            return System.Array.Empty<TeamHpSlotUI>();
        }

        private static List<ulong> CollectOtherClientIds(NetworkManager nm, ulong localId)
        {
            List<ulong> list = new List<ulong>(4);
            IReadOnlyList<ulong> ids = nm.ConnectedClientsIds;
            if (ids == null)
                return list;

            for (int i = 0; i < ids.Count; i++)
            {
                ulong id = ids[i];
                if (id != localId)
                    list.Add(id);
            }

            return list;
        }

        private void BindSlot(TeamHpSlotUI slot, ulong clientId, NetworkManager nm)
        {
            Color color = ResolveTeamColor(clientId, nm);
            float hp;
            float maxForClient = Mathf.Max(1f, maxHP);
            PlayerHealthSystem health = null;

            if (TryGetPlayerHealth(clientId, nm, out health))
            {
                hp = health.CurrentHP.Value;
                maxForClient = ResolveMaxHpForHealth(health);
            }
            else
            {
                hp = maxForClient;
            }

            slot.Bind(clientId, color, hp, maxForClient);
            RegisterMaxHpSubscription(health, slot);
        }

        private static bool TryGetPlayerHealth(ulong clientId, NetworkManager nm, out PlayerHealthSystem health)
        {
            health = null;
            if (nm.ConnectedClients == null)
                return false;

            if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client == null)
                return false;

            NetworkObject po = client.PlayerObject;
            if (po == null)
                return false;

            health = po.GetComponent<PlayerHealthSystem>();
            if (health == null)
                health = po.GetComponentInChildren<PlayerHealthSystem>(true);

            return health != null;
        }

        private float ResolveMaxHpForClient(ulong clientId, NetworkManager nm)
        {
            if (TryGetPlayerHealth(clientId, nm, out PlayerHealthSystem health))
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

        private static Color ResolveTeamColor(ulong clientId, NetworkManager nm)
        {
            if (TryGetPlayerHealth(clientId, nm, out PlayerHealthSystem health))
            {
                NetworkObject netObj = health.NetworkObject;
                if (netObj != null)
                {
                    PlayerTeamIdentity identity = netObj.GetComponent<PlayerTeamIdentity>()
                        ?? netObj.GetComponentInChildren<PlayerTeamIdentity>(true);
                    if (identity != null)
                        return identity.CurrentColor;
                }
            }

            if (LobbyTeamColorCache.TryGetColor(clientId, out Color32 cached))
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
            if (infoTeamRoot != null)
                infoTeamRoot.SetActive(active);
        }
    }
}

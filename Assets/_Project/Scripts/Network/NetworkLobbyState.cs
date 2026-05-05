using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Network
{
    /// <summary>
    /// HJO_Lobby에서 사용하는 로비 플레이어 상태의 서버 권위 원본입니다.
    /// 서버만 플레이어 목록과 Ready 상태를 변경합니다.
    /// </summary>
    public class NetworkLobbyState : NetworkBehaviour
    {
        [Header("==== 로비 설정 ====")]
        [Tooltip("로비 최대 인원 Host를 포함된 인원")]
        [SerializeField, Min(1)] private int maxPlayers = 4;

        [Tooltip("표시 이름을 아직 받지 못했을 때 사용할 기본 이름")]
        [SerializeField] private string fallbackDisplayName = "Player";

        [Tooltip("표시 이름 최대 글자 수 FixedString64Bytes에 저장되며 한글 기준 약 20자")]
        [SerializeField, Range(1, 20)] private int maxDisplayNameCharacters = 20;

        [Header("==== 디버그 ====")]
        [Tooltip("로비 상태 변경 로그를 출력할지 여부")]
        [SerializeField] private bool logDebug = false;
        
        private NetworkList<LobbyPlayerState> players;

        /// <summary>
        /// UI 계층에서 읽기와 OnListChanged 구독 용도로 사용
        /// 목록 변경은 서버 메서드에서만 처리
        /// </summary>
        public NetworkList<LobbyPlayerState> Players => players;

        public event Action NetworkSpawned;
        public event Action NetworkDespawned;

        private void Awake()
        {
            players = new NetworkList<LobbyPlayerState>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server
            );
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                    NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
                }

                RegisterExistingClientsServer();
            }

            NetworkSpawned?.Invoke();
            LogDebug($"네트워크 스폰 완료. IsServer={IsServer}, IsClient={IsClient}");
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null && IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            NetworkDespawned?.Invoke();
            LogDebug("네트워크 디스폰 완료.");
        }

        /// <summary>
        /// 로컬 플레이어의 표시 이름 제출 요청을 서버에서 처리
        /// ClientId는 클라이언트가 보내지 않고 SenderClientId로 판별합니다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SubmitLocalPlayerInfoServerRpc(
            FixedString64Bytes displayName, RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            UpdatePlayerInfoServer(senderClientId, SanitizeDisplayName(displayName));
        }
        
        /// <summary>
        /// Ready 상태 변경 요청을 서버에서 처리
        /// 실제 변경 대상은 SenderClientId 기준으로 서버가 결정합니다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            int index = FindPlayerIndex(senderClientId);

            if (index < 0)
            {
                Debug.LogWarning(
                    $"[NetworkLobbyState] 미등록 client의 Ready 요청입니다." +
                    $"fallback 등록 후 처리합니다. ClientId={senderClientId}", this);

                EnsurePlayerExistsServer(senderClientId);
                index = FindPlayerIndex(senderClientId);
            }

            if (index < 0) return;

            LobbyPlayerState state = players[index];
            state.IsReady = ready;
            players[index] = state;

            LogDebug($"Ready 상태 변경. ClientId={senderClientId}, Ready={ready}");
        }
        
        /// <summary>
        /// 특정 client의 로비 상태를 조회
        /// </summary>
        public bool TryGetPlayer(ulong clientId, out LobbyPlayerState state)
        {
            int index = FindPlayerIndex(clientId);

            if (index < 0)
            {
                state = default;
                return false;
            }

            state = players[index];
            return true;
        }

        private void RegisterExistingClientsServer()
        {
            if (!IsServer || NetworkManager.Singleton == null) return;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                EnsurePlayerExistsServer(clientId);
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            EnsurePlayerExistsServer(clientId);
        }
        
        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            RemovePlayerServer(clientId);
        }

        /// <summary>
        /// 접속한 clientId가 목록에 없으면 기본 표시 이름으로 등록
        /// 실제 Cloud Save 표시 이름 갱신은 SubmitLocalPlayerInfoServerRpc에서 처리합니다.
        /// </summary>
        private void EnsurePlayerExistsServer(ulong clientId)
        {
            if (!IsServer) return;
            if (FindPlayerIndex(clientId) >= 0) return;

            if (players.Count >= maxPlayers)
            {
                Debug.LogWarning(
                    $"[NetworkLobbyState] 최대 인원을 초과해 player 추가를 무시합니다. ClientId={clientId}", this);
                return;
            }
            
            players.Add(new LobbyPlayerState
            {
                ClientId = clientId,
                DisplayName = ToFixedDisplayName(fallbackDisplayName),
                IsHost = clientId == NetworkManager.ServerClientId,
                IsReady = false
            });

            LogDebug($"플레이어 등록. ClientId={clientId}");
        }
        
        /// <summary>
        /// 서버의 로비 플레이어 목록에 표시 이름을 갱신
        /// 접속 직후 목록 등록보다 정보 제출이 먼저 도착한 경우 기본 등록 후 갱신
        /// </summary>
        private void UpdatePlayerInfoServer(ulong clientId, FixedString64Bytes displayName)
        {
            if (!IsServer) return;
            
            int index = FindPlayerIndex(clientId);

            if (index < 0)
            {
                LogDebug($"등록 전 client의 정보 제출입니다. 기본 등록 후 갱신합니다. ClientId={clientId}");

                EnsurePlayerExistsServer(clientId);
                index = FindPlayerIndex(clientId);
            }
            
            if (index < 0) return;
            
            LobbyPlayerState state = players[index];
            state.DisplayName = displayName;
            state.IsHost = clientId == NetworkManager.ServerClientId;
            players[index] = state;

            LogDebug($"플레이어 정보 갱신. ClientId={clientId}, 표시이름={displayName}");
        }

        private void RemovePlayerServer(ulong clientId)
        {
            if (!IsServer) return;

            int index = FindPlayerIndex(clientId);

            if (index < 0)
            {
                LogDebug($"제거할 플레이어가 없습니다. ClientId={clientId}");
                return;
            }

            players.RemoveAt(index);

            LogDebug($"플레이어 제거. ClientId={clientId}");
        }

        private int FindPlayerIndex(ulong clientId)
        {
            if (players == null) return -1;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].ClientId == clientId) return i;
            }
            
            return -1;
        }

        private FixedString64Bytes SanitizeDisplayName(FixedString64Bytes displayName)
        {
            return ToFixedDisplayName(displayName.ToString());
        }

        private FixedString64Bytes ToFixedDisplayName(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value)
                ? fallbackDisplayName
                : value.Trim();

            if (sanitized.Length > maxDisplayNameCharacters)
                sanitized = sanitized.Substring(0, maxDisplayNameCharacters);

            return new FixedString64Bytes(sanitized);
        }

        private void LogDebug(string msg)
        {
            if (!logDebug) return;
            
            Debug.Log($"[NetworkLobbyState] {msg}", this);
        }
    }
}
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Network
{
    /// <summary>
    /// 서버가 결정한 SpawnPoint를 Owner Client에 전달해
    /// NetworkTransform.Teleport로 초기 위치를 보정한다.
    /// </summary>
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class PlayerSpawnInitializer : NetworkBehaviour
    {
        [Header("==== 참조 ====")]
        [Tooltip("Teleport 대상 NetworkTransform입니다. " +
                 "Player Root에 붙은 NetworkTransform을 연결합니다. " +
                 "비워두면 Awake에서 GetComponent로 자동 폴백합니다.")]
        [SerializeField] private NetworkTransform networkTransform;

        [Header("==== 디버그 ====")]
        [Tooltip("스폰 위치 전송/적용을 로그로 출력합니다. 평소엔 끄고, 문제 진단 시 켭니다.")]
        [SerializeField] private bool logDebug = false;

        private Coroutine spawnTeleportRoutine;

        private void Awake()
        {
            if (networkTransform == null)
                networkTransform = GetComponent<NetworkTransform>();
        }

        /// <summary>
        /// 로컬 Owner 플레이어가 스폰되면 컷아웃, 카메라 등 로컬 연출 시스템이 사용할 플레이어 루트 Transform을 알린다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
                return;

            EventBus.Publish(new OwnerPlayerRootRegisteredEvent
            {
                playerRoot = transform
            });
        }

        /// <summary>
        /// 로컬 Owner 플레이어가 디스폰되면 로컬 연출 시스템이 플레이어 루트 Transform 참조를 정리할 수 있도록 알린다.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                EventBus.Publish(new OwnerPlayerRootUnregisteredEvent
                {
                    playerRoot = transform
                });
            }

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// 서버에서 호출. 해당 Player의 Owner Client 한 명에게만 SpawnPoint를 전달한다.
        /// </summary>
        public void SendSpawnPointToOwner(Vector3 position, Quaternion rotation, ulong ownerClientId)
        {
            if (!IsServer)
                return;

            if (logDebug)
                Debug.Log(
                    $"[PlayerSpawnInitializer] SpawnPoint 전송. " +
                    $"OwnerClientId={ownerClientId}, Position={position}",
                    this);

            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { ownerClientId } }
            };

            ApplySpawnPointClientRpc(position, rotation, rpcParams);
        }

        // Owner 권위 NetworkTransform이므로 서버가 정한 SpawnPoint를 Owner Client에서 적용한다.
        // TargetClientIds로 1차 제한, IsOwner로 2차 방어한다.
        [ClientRpc]
        private void ApplySpawnPointClientRpc(
            Vector3 position,
            Quaternion rotation,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            if (networkTransform == null)
            {
                Debug.LogError(
                    "[PlayerSpawnInitializer] NetworkTransform 참조가 비어 있어 Teleport를 중단합니다.",
                    this);
                return;
            }

            if (spawnTeleportRoutine != null)
                StopCoroutine(spawnTeleportRoutine);

            spawnTeleportRoutine = StartCoroutine(TeleportAfterDelay(position, rotation));
        }

        // 스폰 직후 초기 NetworkTransform 동기화와의 경합을 피하기 위해 2프레임 뒤 적용한다.
        private System.Collections.IEnumerator TeleportAfterDelay(Vector3 position, Quaternion rotation)
        {
            yield return null;
            yield return null;

            if (networkTransform != null)
                networkTransform.Teleport(position, rotation, transform.localScale);

            if (logDebug)
                Debug.Log(
                    $"[PlayerSpawnInitializer] Teleport 완료. " +
                    $"OwnerClientId={OwnerClientId}, Position={transform.position}",
                    this);

            spawnTeleportRoutine = null;
        }
    }
}

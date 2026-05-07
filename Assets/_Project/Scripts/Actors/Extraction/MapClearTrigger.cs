using DeadZone.Network;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors.Extraction
{
    /// <summary>
    /// 게임 씬의 클리어 지점 Trigger를 감지하고 서버에서 Clear 요청을 전달한다.
    /// 실제 저장, 결과 처리, 로비 복귀는 RaidClearCoordinator가 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MapClearTrigger : MonoBehaviour
    {
        [Header("==== Clear 연결 ====")]
        [Tooltip("Clear 요청을 처리할 RaidClearCoordinator입니다. " +
                 "비어 있으면 씬에서 자동으로 찾습니다. Cloud Save 저장이나 Lobby 복귀는 이 Trigger가 직접 처리하지 않습니다.")]
        [SerializeField] private RaidClearCoordinator coordinator;

        [Header("==== 디버그 ====")]
        [Tooltip("Trigger 진입, 서버 판정, PlayerObject 검증, 중복 Clear 방지 로그를 출력합니다.")]
        [SerializeField] private bool logDebug = true;

        private bool hasRequestedClear;

        private void Reset()
        {
            BoxCollider triggerCollider = GetComponent<BoxCollider>();

            if (triggerCollider != null)
                triggerCollider.isTrigger = true;
        }

        private void OnValidate()
        {
            BoxCollider triggerCollider = GetComponent<BoxCollider>();

            if (triggerCollider != null)
                triggerCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (hasRequestedClear)
            {
                LogDebug("이미 Clear 요청을 전달한 Trigger입니다. 추가 진입을 무시합니다.");
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                LogDebug("NetworkManager.Singleton이 없어 Trigger 처리를 건너뜁니다.");
                return;
            }

            if (!networkManager.IsServer)
                return;

            NetworkObject playerNetworkObject = other.GetComponentInParent<NetworkObject>();

            if (playerNetworkObject == null)
            {
                LogDebug($"NetworkObject가 아닌 Collider 진입을 무시합니다. Collider={other.name}");
                return;
            }

            if (!playerNetworkObject.IsPlayerObject)
            {
                LogDebug(
                    $"PlayerObject가 아닌 NetworkObject 진입을 무시합니다. " +
                    $"Object={playerNetworkObject.name}, OwnerClientId={playerNetworkObject.OwnerClientId}");
                return;
            }

            if (!TryResolveCoordinator(out RaidClearCoordinator resolvedCoordinator))
            {
                Debug.LogWarning("[맵 클리어] RaidClearCoordinator를 찾을 수 없어 Clear 요청을 전달하지 못했습니다.", this);
                return;
            }

            LogDebug(
                $"PlayerObject Trigger 진입 감지. " +
                $"Object={playerNetworkObject.name}, OwnerClientId={playerNetworkObject.OwnerClientId}");

            bool accepted = resolvedCoordinator.TryRequestPartyClear(playerNetworkObject.OwnerClientId);

            if (accepted || resolvedCoordinator.HasClearHandled)
                hasRequestedClear = true;
        }

        private bool TryResolveCoordinator(out RaidClearCoordinator resolvedCoordinator)
        {
            if (coordinator != null)
            {
                resolvedCoordinator = coordinator;
                return true;
            }

            coordinator = FindFirstObjectByType<RaidClearCoordinator>();
            resolvedCoordinator = coordinator;

            return resolvedCoordinator != null;
        }

        private void LogDebug(string message)
        {
            if (!logDebug)
                return;

            Debug.Log($"[맵 클리어] {message}", this);
        }
    }
}
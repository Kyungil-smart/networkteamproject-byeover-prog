using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// UI 없이 Console 창으로 파밍 상자 테스트를 하기 위한 F키 상호작용 테스트 컴포넌트입니다.
    ///
    /// 역할:
    /// 1. 플레이어가 파밍 상자 근처에 있는지 거리로 검사합니다.
    /// 2. 근처에 있을 때 F 키 입력을 감지합니다.
    /// 3. 같은 오브젝트의 KHWLootContainer.OnInteract(clientId)를 호출합니다.
    /// 4. KHWLootContainer가 랜덤 아이템 6개를 생성하고 Console에 출력합니다.
    ///
    /// 주의:
    /// - 이 스크립트는 Player가 아니라 파밍 상자 오브젝트에 붙입니다.
    /// - CharacterController를 Trigger로 바꾸지 않기 위해 Collider Trigger 방식이 아니라 거리 검사 방식을 사용합니다.
    /// - 실제 완성 버전에서는 기존 InteractionSystem으로 교체할 수 있습니다.
    /// </summary>
    public class KHWContainerFInteractTester : MonoBehaviour
    {
        [Header("연결 대상")]
        [Tooltip("같은 상자 오브젝트에 붙어 있는 KHWLootContainer입니다. 비워두면 Awake에서 자동으로 찾습니다.")]
        [SerializeField] private KHWLootContainer lootContainer;

        [Header("플레이어 찾기")]
        [Tooltip("체크하면 NetworkManager의 LocalClient.PlayerObject를 먼저 찾습니다. 네트워크 테스트에서는 체크 권장입니다.")]
        [SerializeField] private bool useNetworkLocalPlayer = true;

        [Tooltip("NetworkManager로 플레이어를 못 찾을 때 사용할 태그 이름입니다. PlayerPrefab Root의 Tag가 Player여야 합니다.")]
        [SerializeField] private string playerTag = "Player";

        [Header("상호작용 거리와 키")]
        [Tooltip("플레이어와 상자의 거리가 이 값 이하일 때만 F 입력을 받습니다.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("파밍 상자를 열 테스트 키입니다.")]
        [SerializeField] private Key interactKey = Key.F;

        [Header("디버그")]
        [Tooltip("접근 가능 상태, F 입력, 호출 실패 원인을 Console에 출력합니다.")]
        [SerializeField] private bool printDebugLog = true;

        [Tooltip("접근 가능 상태가 바뀔 때만 로그를 출력합니다. Console 도배 방지용입니다.")]
        [SerializeField] private bool printOnlyStateChange = true;

        private Transform cachedPlayer;
        private NetworkObject cachedPlayerNetworkObject;
        private bool wasInRange;

        private void Awake()
        {
            if (lootContainer == null)
            {
                lootContainer = GetComponent<KHWLootContainer>();
            }
        }

        private void Update()
        {
            FindPlayerIfNeeded();

            if (cachedPlayer == null)
            {
                if (printDebugLog && !printOnlyStateChange)
                {
                    Debug.LogWarning("[KHW 상자 테스트] 플레이어를 찾지 못했습니다. Player 태그 또는 NetworkManager LocalClient를 확인하세요.", this);
                }
                return;
            }

            float distance = Vector3.Distance(transform.position, cachedPlayer.position);
            bool isInRange = distance <= interactRange;

            if (printDebugLog && wasInRange != isInRange)
            {
                if (isInRange)
                {
                    Debug.Log("[KHW 상자 테스트] 플레이어가 파밍 상자 상호작용 범위에 들어왔습니다. F 키를 누르면 상자를 엽니다. 거리=" + distance.ToString("F2"), this);
                }
                else
                {
                    Debug.Log("[KHW 상자 테스트] 플레이어가 파밍 상자 상호작용 범위를 벗어났습니다. 거리=" + distance.ToString("F2"), this);
                }
            }

            wasInRange = isInRange;

            if (!isInRange)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (!Keyboard.current[interactKey].wasPressedThisFrame)
            {
                return;
            }

            if (lootContainer == null)
            {
                Debug.LogWarning("[KHW 상자 테스트] KHWLootContainer가 연결되지 않았습니다. 상자 오브젝트에 KHWLootContainer를 붙이세요.", this);
                return;
            }

            ulong clientId = GetClientId();

            if (printDebugLog)
            {
                Debug.Log("[KHW 상자 테스트] F 키 입력 감지. KHWLootContainer.OnInteract 호출 / ClientId=" + clientId, this);
            }

            lootContainer.OnInteract(clientId);
        }

        private void FindPlayerIfNeeded()
        {
            if (cachedPlayer != null)
            {
                return;
            }

            if (useNetworkLocalPlayer && NetworkManager.Singleton != null)
            {
                if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                {
                    cachedPlayerNetworkObject = NetworkManager.Singleton.LocalClient.PlayerObject;
                    cachedPlayer = cachedPlayerNetworkObject.transform;
                    return;
                }
            }

            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
            {
                cachedPlayer = playerObject.transform;
                cachedPlayerNetworkObject = playerObject.GetComponentInParent<NetworkObject>();
            }
        }

        private ulong GetClientId()
        {
            if (NetworkManager.Singleton != null)
            {
                return NetworkManager.Singleton.LocalClientId;
            }

            if (cachedPlayerNetworkObject != null)
            {
                return cachedPlayerNetworkObject.OwnerClientId;
            }

            return 0;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}

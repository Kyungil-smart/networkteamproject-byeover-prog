// ============================================================================
// KHWContainerFInteractTester.cs
// 목적: 기존 상호작용 시스템 없이 테스트하기 위해, 파밍 상자 근처에서 F 키를 누르면
//       KHWLootContainer.OnInteract(clientId)를 직접 호출합니다.
// 패턴: 테스트 어댑터(Test Adapter) + 폴링 입력(Update) + 거리 기반 상호작용.
// 적용: Player가 아니라 파밍 상자 오브젝트에 붙입니다.
// ============================================================================
using DeadZone.Actors;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
public class ContainerFInteractTester : MonoBehaviour
{
    [Header("연결 대상")]
    [Tooltip("같은 오브젝트에 있는 LootContainer입니다.")]
    [SerializeField] private LootContainer lootContainer;

    [Header("플레이어 찾기")]
    [Tooltip("체크하면 NetworkManager.LocalClient.PlayerObject를 우선 사용합니다.")]
    [SerializeField] private bool useNetworkLocalPlayer = true;

    [Tooltip("Network PlayerObject를 찾지 못했을 때 사용할 플레이어 태그입니다.")]
    [SerializeField] private string playerTag = "Player";

    [Header("상호작용 입력")]
    [Tooltip("상자와 플레이어 사이 거리가 이 값 이하일 때만 F 입력을 받습니다.")]
    [SerializeField] private float interactRange = 2.5f;

    [Tooltip("테스트 상호작용 키입니다. 기본값은 F입니다.")]
    [SerializeField] private Key interactKey = Key.F;

    [Header("디버그")]
    [Tooltip("접근 상태와 F 입력 로그를 Console에 출력합니다.")]
    [SerializeField] private bool printDebugLog = true;

    [Tooltip("체크하면 범위 진입/이탈 상태가 바뀔 때만 접근 로그를 출력합니다.")]
    [SerializeField] private bool printOnlyStateChange = true;

    private bool wasInRange;
    private Transform cachedPlayer;

    private void Awake()
    {
        if (lootContainer == null)
        {
            lootContainer = GetComponent<LootContainer>();
        }
    }

    private void Update()
    {
        Transform player = FindPlayerTransform();
        if (player == null)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        bool inRange = distance <= interactRange;

        PrintRangeDebugIfNeeded(inRange, distance);

        if (!inRange)
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

        ulong clientId = GetClientId(player);

        if (printDebugLog)
        {
            Debug.Log("[ContainerFInteractTester] F 입력 감지. LootContainer.OnInteract 호출 / ClientId=" + clientId, this);
        }

        if (lootContainer != null)
        {
            lootContainer.OnInteract(clientId);
        }
    }

    private Transform FindPlayerTransform()
    {
        if (useNetworkLocalPlayer && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            if (NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                cachedPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
                return cachedPlayer;
            }
        }

        if (cachedPlayer != null)
        {
            return cachedPlayer;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject != null)
        {
            cachedPlayer = playerObject.transform;
        }

        return cachedPlayer;
    }

    private ulong GetClientId(Transform player)
    {
        NetworkObject networkObject = player.GetComponentInParent<NetworkObject>();
        if (networkObject != null)
        {
            return networkObject.OwnerClientId;
        }

        if (NetworkManager.Singleton != null)
        {
            return NetworkManager.Singleton.LocalClientId;
        }

        return 0;
    }

    private void PrintRangeDebugIfNeeded(bool inRange, float distance)
    {
        if (!printDebugLog)
        {
            return;
        }

        if (printOnlyStateChange && inRange == wasInRange)
        {
            return;
        }

        wasInRange = inRange;

        if (inRange)
        {
            Debug.Log("[ContainerFInteractTester] 플레이어가 상호작용 범위에 들어왔습니다. 거리=" + distance.ToString("0.00"), this);
        }
        else
        {
            Debug.Log("[ContainerFInteractTester] 플레이어가 상호작용 범위에서 벗어났습니다. 거리=" + distance.ToString("0.00"), this);
        }
    }
}

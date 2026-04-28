using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 역할: 기존 LootContainer.cs를 수정하지 않고 사용할 수 있는 KHW 전용 루팅 상자.
    /// 플레이어가 상호작용하면 서버에서 한 번만 열리고, LootTableSO 기반 아이템을 주변에 스폰한다.
    ///
    /// 코드 해석:
    /// - IsOpened: 상자가 이미 열렸는지 네트워크 동기화한다.
    /// - OnInteract(): InteractionSystem에서 호출된다.
    /// - TryOpenServerRpc(): 서버에서 잠금/중복 오픈을 검사하고 아이템을 생성한다.
    /// - SpawnLootItem(): KHWLootInteractable이 있으면 item + amount를 같이 초기화한다.
    /// </summary>
    public class KHWLootContainer : NetworkBehaviour, IInteractable
    {
        [Header("[KHW] 루팅 테이블")]
        [Tooltip("상자를 열었을 때 사용할 기존 LootTableSO입니다.")]
        [SerializeField] private LootTableSO lootTable;

        [Header("[KHW] 월드 아이템 프리팹")]
        [Tooltip("NetworkObject + KHWLootInteractable이 붙은 프리팹을 넣습니다.")]
        [SerializeField] private GameObject lootItemPrefab;

        [Header("[KHW] 잠금 설정")]
        [Tooltip("비워두면 잠금 없음. 값이 있으면 지금 버전에서는 잠긴 상태로 처리합니다. 열쇠 소비는 추후 확장합니다.")]
        [SerializeField] private string requiredKeyId = "";

        [Header("[KHW] 스폰 설정")]
        [Tooltip("상자 개봉 시 생성할 아이템 개수")]
        [SerializeField] private int spawnCount = 3;

        [Tooltip("상자 주변에 흩뿌릴 반경")]
        [SerializeField] private float spawnRadius = 0.5f;

        [Tooltip("아이템이 바닥에 파묻히지 않도록 위로 올리는 높이")]
        [SerializeField] private float spawnHeightOffset = 0.5f;

        [Header("[KHW] 연출")]
        [Tooltip("Open 트리거가 있는 Animator를 넣으면 상자 열림 애니메이션이 재생됩니다.")]
        [SerializeField] private Animator animator;

        [Header("[KHW] 네트워크 상태")]
        [Tooltip("상자가 열렸는지 모든 클라이언트에 동기화됩니다.")]
        public NetworkVariable<bool> IsOpened = new(false);

        public string GetPromptText()
        {
            if (IsOpened.Value) return "";
            return string.IsNullOrEmpty(requiredKeyId) ? "[F] 상자 열기" : "[F] 잠김";
        }

        public void OnInteract(ulong clientId)
        {
            TryOpenServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryOpenServerRpc(ServerRpcParams rpc = default)
        {
            if (IsOpened.Value) return;

            // [KHW 추가 기능]
            // 지금 단계에서는 requiredKeyId가 있으면 잠긴 상자로만 처리한다.
            // 추후 GridInventory.HasItem(requiredKeyId, 1)과 ConsumeItem으로 열쇠 소비를 붙일 수 있다.
            if (!string.IsNullOrEmpty(requiredKeyId)) return;

            if (lootTable == null || lootItemPrefab == null) return;

            IsOpened.Value = true;
            if (animator != null) animator.SetTrigger("Open");

            int count = Mathf.Max(0, spawnCount);
            for (int i = 0; i < count; i++)
            {
                if (!KHWLootRollUtility.TryRollItemWithAmount(lootTable, out ItemDataSO item, out int amount))
                    continue;

                SpawnLootItem(item, amount);
            }
        }

        private void SpawnLootItem(ItemDataSO item, int amount)
        {
            Vector3 offset = Random.insideUnitSphere * spawnRadius;
            offset.y = spawnHeightOffset;
            Vector3 spawnPosition = transform.position + offset;

            GameObject go = Instantiate(lootItemPrefab, spawnPosition, Quaternion.identity);

            KHWLootInteractable khwLoot = go.GetComponent<KHWLootInteractable>();
            if (khwLoot != null)
                khwLoot.InitializeWithAmount(item, amount);
            else
                go.GetComponent<ILootCarrier>()?.Initialize(item);

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn(destroyWithScene: true);
            else
                Debug.LogError("[KHWLootContainer] lootItemPrefab에 NetworkObject가 없습니다.", go);
        }
    }
}

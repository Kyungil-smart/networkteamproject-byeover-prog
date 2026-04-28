using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 역할: 기존 LootSpawner.cs를 수정하지 않고 사용할 수 있는 KHW 전용 스폰 포인트.
    /// 기존 LootTableSO를 그대로 사용하되, countRange까지 반영해서 KHWLootInteractable에 수량을 넘긴다.
    ///
    /// 코드 해석:
    /// - OnNetworkSpawn(): 서버에서만 루팅 아이템을 생성한다.
    /// - KHWLootRollUtility.TryRollItemWithAmount(): 아이템과 수량을 같이 뽑는다.
    /// - SpawnLootItem(): 프리팹을 Instantiate한 뒤 ILootCarrier 또는 KHWLootInteractable을 초기화한다.
    /// </summary>
    public class KHWLootSpawner : NetworkBehaviour
    {
        [Header("[KHW] 루팅 테이블")]
        [Tooltip("기존 LootTableSO를 그대로 사용합니다. entries의 item/weight/countRange를 사용합니다.")]
        [SerializeField] private LootTableSO lootTable;

        [Header("[KHW] 월드 아이템 프리팹")]
        [Tooltip("NetworkObject + KHWLootInteractable이 붙은 프리팹을 넣습니다.")]
        [SerializeField] private GameObject lootItemPrefab;

        [Header("[KHW] 스폰 개수")]
        [Tooltip("최소 스폰 개수")]
        [SerializeField] private int minItems = 2;

        [Tooltip("최대 스폰 개수")]
        [SerializeField] private int maxItems = 4;

        [Header("[KHW] 스폰 위치")]
        [Tooltip("스폰 포인트 주변 랜덤 반경")]
        [SerializeField] private float spawnRadius = 1.5f;

        [Tooltip("아이템이 바닥에 파묻히지 않도록 위로 올리는 높이")]
        [SerializeField] private float spawnHeightOffset = 0.15f;

        [Header("[KHW] 디버그")]
        [SerializeField] private bool drawGizmos = true;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            SpawnAll();
        }

        public void SpawnAll()
        {
            if (!IsServer) return;
            if (lootTable == null || lootItemPrefab == null) return;

            int safeMin = Mathf.Max(0, minItems);
            int safeMax = Mathf.Max(safeMin, maxItems);
            int count = Random.Range(safeMin, safeMax + 1);

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

            // [KHW 추가 기능]
            // KHWLootInteractable이 있으면 수량까지 세팅한다.
            KHWLootInteractable khwLoot = go.GetComponent<KHWLootInteractable>();
            if (khwLoot != null)
                khwLoot.InitializeWithAmount(item, amount);
            else
                go.GetComponent<ILootCarrier>()?.Initialize(item);

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn(destroyWithScene: true);
            else
                Debug.LogError("[KHWLootSpawner] lootItemPrefab에 NetworkObject가 없습니다.", go);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }
}

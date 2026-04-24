using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    /// <summary>
    /// 서버 시작 시 루팅 아이템을 스폰한다. 각 Map_X 씬의 Systems > LootSpawner에 부착
    /// (또는 각 개별 SP_X 스폰 포인트에 부착).
    /// </summary>
    public class LootSpawner : NetworkBehaviour
    {
        [Header("Spawn Config")]
        [SerializeField] private LootTableSO lootTable;
        [SerializeField] private GameObject lootItemPrefab;
        [SerializeField] private int minItems = 2;
        [SerializeField] private int maxItems = 4;
        [SerializeField] private float spawnRadius = 1.5f;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (lootTable == null || lootItemPrefab == null) return;

            int count = Random.Range(minItems, maxItems + 1);
            for (int i = 0; i < count; i++)
            {
                ItemDataSO item = lootTable.RollOne();
                if (item == null) continue;

                Vector3 offset = Random.insideUnitSphere * spawnRadius;
                offset.y = 0;
                Vector3 pos = transform.position + offset;

                GameObject go = Instantiate(lootItemPrefab, pos, Quaternion.identity);
                var interactable = go.GetComponent<ILootCarrier>();
                interactable?.Initialize(item);

                var netObj = go.GetComponent<NetworkObject>();
                if (netObj != null) netObj.Spawn(destroyWithScene: true);
                else Debug.LogError("[LootSpawner] lootItemPrefab missing NetworkObject");
            }
        }
    }

    /// <summary>
    /// 월드에 스폰된 루팅 아이템이 구현한다.
    /// </summary>
    public interface ILootCarrier
    {
        void Initialize(ItemDataSO item);
    }
}

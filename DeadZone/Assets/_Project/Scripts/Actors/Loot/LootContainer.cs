using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// 처음 열릴 때 루팅을 스폰하는 컨테이너. 열쇠가 필요할 수 있다.
    /// </summary>
    public class LootContainer : NetworkBehaviour, IInteractable
    {
        [SerializeField] private LootTableSO lootTable;
        [SerializeField] private GameObject lootItemPrefab;
        [SerializeField] private string requiredKeyId = "";
        [SerializeField] private int spawnCount = 3;
        [SerializeField] private float spawnRadius = 0.5f;
        [SerializeField] private Animator anim;

        public NetworkVariable<bool> IsOpened = new(false);

        public string GetPromptText()
        {
            if (IsOpened.Value) return "";
            return string.IsNullOrEmpty(requiredKeyId) ? "[F] Open" : "[F] Locked";
        }

        public void OnInteract(ulong clientId)
        {
            TryOpenServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryOpenServerRpc(ServerRpcParams rpc = default)
        {
            if (IsOpened.Value) return;
            if (!string.IsNullOrEmpty(requiredKeyId)) return;
            if (lootTable == null || lootItemPrefab == null) return;

            IsOpened.Value = true;
            if (anim != null) anim.SetTrigger("Open");

            for (int i = 0; i < spawnCount; i++)
            {
                var item = lootTable.RollOne();
                if (item == null) continue;

                Vector3 offset = Random.insideUnitSphere * spawnRadius;
                offset.y = 0.5f;
                var go = Instantiate(lootItemPrefab, transform.position + offset, Quaternion.identity);
                var carrier = go.GetComponent<ILootCarrier>();
                carrier?.Initialize(item);
                var net = go.GetComponent<NetworkObject>();
                if (net != null) net.Spawn(destroyWithScene: true);
            }
        }
    }
}

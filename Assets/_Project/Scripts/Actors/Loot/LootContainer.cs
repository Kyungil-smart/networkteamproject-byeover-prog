using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    public class LootContainer : NetworkBehaviour, IInteractable
    {
        [SerializeField] private LootTableSO lootTable;
        [SerializeField] private GameObject lootItemPrefab;

        [Tooltip("비어있으면 누구나 오픈 가능. 값이 있으면 해당 itemID 열쇠 보유자만 오픈.")]
        [SerializeField] private string requiredKeyId = "";

        [Tooltip("루팅 굴림 횟수. CommonBox=2~3, RareBox=1~2, MedicalCase=2~4 등.")]
        [SerializeField] private int spawnCount = 3;

        [Tooltip("스폰된 아이템들이 박스 주변에 흩어지는 반경 (월드 단위)")]
        [SerializeField] private float spawnRadius = 0.5f;

        [SerializeField] private Animator anim;

        public NetworkVariable<bool> IsOpened = new(false);

        public string GetPromptText()
        {
            if (IsOpened.Value) return "";
            return string.IsNullOrEmpty(requiredKeyId) ? "[F] Open" : "[F] Locked (Key Required)";
        }

        public void OnInteract(ulong clientId)
        {
            TryOpenServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryOpenServerRpc(ServerRpcParams rpc = default)
        {
            // 이미 열렸으면 거부
            if (IsOpened.Value) return;
            if (lootTable == null || lootItemPrefab == null) return;

            ulong clientId = rpc.Receive.SenderClientId;

            // 잠긴 컨테이너: 플레이어 인벤토리에서 열쇠 보유 검증
            if (!string.IsNullOrEmpty(requiredKeyId))
            {
                if (!ValidateKeyOwnership(clientId, requiredKeyId)) return;
                // 열쇠는 소모하지 않음 (타르코프 방식)
            }

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

        /// <summary>
        /// 플레이어 인벤토리에 해당 열쇠가 있는지 검증 (서버 전용).
        /// </summary>
        private bool ValidateKeyOwnership(ulong clientId, string keyId)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                return false;

            var playerObj = client.PlayerObject;
            if (playerObj == null) return false;

            var inv = playerObj.GetComponent<IInventory>();
            if (inv == null) return false;

            return inv.HasItem(keyId, 1);
        }
    }
}
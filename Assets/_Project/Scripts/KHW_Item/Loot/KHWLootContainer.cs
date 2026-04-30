using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 맵 바닥 랜덤 스폰을 사용하지 않고, 오직 파밍 상자를 열었을 때만 아이템을 생성하는 컨테이너입니다.
    /// 기존 LootSpawner는 사용하지 않아도 됩니다.
    /// </summary>
    public class KHWLootContainer : NetworkBehaviour, IInteractable
    {
        [Header("아이템 데이터베이스")]
        [Tooltip("총기/탄약/방어구/헬멧/일반 아이템을 ID로 찾는 Pool SO입니다.")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("루팅 테이블")]
        [Tooltip("상자를 열었을 때 어떤 아이템이 나올지 정하는 LootTableSO입니다.")]
        [SerializeField] private LootTableSO lootTable;

        [Header("월드 아이템 프리팹")]
        [Tooltip("NetworkObject + KHWLootInteractable이 붙어 있는 프리팹을 넣습니다.")]
        [SerializeField] private GameObject lootItemPrefab;

        [Header("상자 잠금 설정")]
        [Tooltip("비워두면 잠금 없음. 값이 있으면 플레이어 인벤토리에 해당 itemID가 있어야 열립니다.")]
        [SerializeField] private string requiredKeyId = "";

        [Tooltip("체크하면 열쇠로 상자를 열 때 열쇠 아이템 1개를 소모합니다.")]
        [SerializeField] private bool consumeKeyOnOpen = false;

        [Header("생성 설정")]
        [Tooltip("상자를 열 때 생성할 아이템 개수")]
        [SerializeField] private int spawnCount = 3;

        [Tooltip("Spawn Points가 비어 있을 때 상자 주변 랜덤 반경")]
        [SerializeField] private float spawnRadius = 0.5f;

        [Tooltip("생성 위치를 바닥에서 위로 올리는 높이")]
        [SerializeField] private float spawnHeightOffset = 0.5f;

        [Tooltip("지정 위치에 아이템을 생성하고 싶으면 여기에 자식 Transform들을 넣습니다.")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("연출")]
        [Tooltip("상자 열림 애니메이션이 있으면 Animator를 넣습니다.")]
        [SerializeField] private Animator animator;

        [Tooltip("Animator Trigger 이름")]
        [SerializeField] private string openTriggerName = "Open";

        [Header("상태 - 런타임 확인용")]
        public NetworkVariable<bool> IsOpened = new NetworkVariable<bool>(false);

        public string GetPromptText()
        {
            if (IsOpened.Value)
            {
                return "";
            }

            if (string.IsNullOrEmpty(requiredKeyId))
            {
                return "[F] 파밍 상자 열기";
            }

            return "[F] 잠긴 파밍 상자";
        }

        public void OnInteract(ulong clientId)
        {
            TryOpenServerRpc();
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// 상자 열기와 아이템 생성은 서버에서만 처리합니다.
        /// 코드 역할: 모든 클라이언트가 같은 아이템 결과를 보게 하고, 중복 생성 버그를 막습니다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void TryOpenServerRpc(ServerRpcParams rpcParams = default)
        {
            if (IsOpened.Value) return;
            if (lootTable == null || lootItemPrefab == null) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            IInventory inventory = FindPlayerInventory(senderClientId);

            if (!CanOpen(inventory))
            {
                Debug.Log("[KHWLootContainer] 상자를 열 수 없습니다. 필요한 열쇠: " + requiredKeyId);
                return;
            }

            if (consumeKeyOnOpen && !string.IsNullOrEmpty(requiredKeyId) && inventory != null)
            {
                inventory.ConsumeItem(requiredKeyId, 1);
            }

            IsOpened.Value = true;
            PlayOpenAnimationClientRpc();

            for (int i = 0; i < spawnCount; i++)
            {
                SpawnLootItem(i);
            }
        }

        private IInventory FindPlayerInventory(ulong clientId)
        {
            if (NetworkManager.Singleton == null) return null;
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return null;

            NetworkClient client = NetworkManager.Singleton.ConnectedClients[clientId];
            if (client.PlayerObject == null) return null;

            return client.PlayerObject.GetComponent<IInventory>();
        }

        private bool CanOpen(IInventory inventory)
        {
            if (string.IsNullOrEmpty(requiredKeyId))
            {
                return true;
            }

            if (inventory == null)
            {
                return false;
            }

            return inventory.HasItem(requiredKeyId, 1);
        }

        private void SpawnLootItem(int index)
        {
            ItemDataSO item;
            int amount;
            bool rolled = KHWLootRollUtility.TryRoll(lootTable, out item, out amount);
            if (!rolled || item == null) return;

            Vector3 spawnPosition = GetSpawnPosition(index);
            GameObject go = Instantiate(lootItemPrefab, spawnPosition, Quaternion.identity);

            KHWLootInteractable khwInteractable = go.GetComponent<KHWLootInteractable>();
            if (khwInteractable != null)
            {
                khwInteractable.InitializeWithAmount(item, amount);
            }
            else
            {
                ILootCarrier carrier = go.GetComponent<ILootCarrier>();
                if (carrier != null)
                {
                    carrier.Initialize(item);
                }
            }

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn(true);
            }
            else
            {
                Debug.LogError("[KHWLootContainer] lootItemPrefab에 NetworkObject가 없습니다.", go);
            }
        }

        private Vector3 GetSpawnPosition(int index)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform point = spawnPoints[index % spawnPoints.Length];
                if (point != null)
                {
                    return point.position;
                }
            }

            Vector3 offset = Random.insideUnitSphere * spawnRadius;
            offset.y = spawnHeightOffset;
            return transform.position + offset;
        }

        [ClientRpc]
        private void PlayOpenAnimationClientRpc()
        {
            if (animator == null) return;
            if (string.IsNullOrEmpty(openTriggerName)) return;
            animator.SetTrigger(openTriggerName);
        }
    }
}

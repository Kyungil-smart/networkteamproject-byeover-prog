using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 역할: 기존 LootInteractable.cs를 수정하지 않고 대체로 사용할 수 있는 루팅 아이템 컴포넌트.
    /// 기존 LootSpawner/LootContainer는 ILootCarrier.Initialize(ItemDataSO item)을 호출하므로,
    /// 이 스크립트가 ILootCarrier를 구현하면 기존 스폰 시스템과 바로 연동된다.
    ///
    /// 코드 해석:
    /// - ItemId/Amount는 NetworkVariable이라 모든 클라이언트가 같은 루팅 정보를 본다.
    /// - OnInteract()가 호출되면 ServerRpc로 서버에 루팅 요청을 보낸다.
    /// - 서버는 플레이어의 IInventory를 찾아 TryAddItem(item, amount)를 호출한다.
    /// - 성공하면 ItemLootedEvent를 발행하고 NetworkObject를 Despawn한다.
    /// </summary>
    public class KHWLootInteractable : NetworkBehaviour, IInteractable, ILootCarrier
    {
        [Header("[KHW] 아이템 Pool 연결")]
        [Tooltip("ItemId를 실제 ItemDataSO로 변환하기 위한 ScriptObjectPoolSO입니다.")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("[KHW] 표시 문구")]
        [Tooltip("아이템 이름이 있을 때 상호작용 안내 문구입니다. {0}=아이템 이름, {1}=수량")]
        [SerializeField] private string promptFormat = "[F] 줍기: {0} x{1}";

        [Tooltip("아이템 정보를 찾지 못했을 때 표시할 기본 문구입니다.")]
        [SerializeField] private string fallbackPrompt = "[F] 줍기";

        [Header("[KHW] 루팅 규칙")]
        [Tooltip("켜두면 루팅 성공 시 월드 아이템 NetworkObject를 제거합니다.")]
        [SerializeField] private bool despawnOnLootSuccess = true;

        [Tooltip("켜두면 루팅 직전에 아이템 gridSize를 1x1로 보정합니다.")]
        [SerializeField] private bool normalizeOneCellOnLoot = true;

        [Tooltip("Pool이 비어있을 때 임시로 검색할 예비 아이템 배열입니다. 가능하면 Pool 사용을 권장합니다.")]
        [SerializeField] private ItemDataSO[] fallbackItemDatabase;

        [Header("[KHW] 네트워크 동기화 값")]
        [Tooltip("월드에 떨어진 아이템의 itemID입니다. 서버에서 세팅되고 클라이언트에 동기화됩니다.")]
        public NetworkVariable<FixedString64Bytes> ItemId = new("");

        [Tooltip("월드에 떨어진 아이템 수량입니다. 서버에서 세팅되고 클라이언트에 동기화됩니다.")]
        public NetworkVariable<int> Amount = new(1);

        private ItemDataSO cachedItem;

        public string GetPromptText()
        {
            ItemDataSO item = ResolveItem();
            if (item == null) return fallbackPrompt;

            int amount = Mathf.Max(1, Amount.Value);
            return string.Format(promptFormat, item.displayName, amount);
        }

        public void Initialize(ItemDataSO item)
        {
            // [KHW 추가 기능]
            // 기존 LootSpawner/LootContainer가 호출하는 기본 ILootCarrier 함수.
            // 기존 시스템은 수량을 넘기지 않으므로 기본 수량 1로 처리한다.
            InitializeInternal(item, scriptObjectPool != null ? scriptObjectPool.DefaultAmount : 1);
        }

        public void InitializeWithAmount(ItemDataSO item, int amount)
        {
            // [KHW 추가 기능]
            // KHWLootSpawner/KHWLootContainer처럼 countRange를 쓰는 새 스크립트에서 호출한다.
            InitializeInternal(item, amount);
        }

        public void OnInteract(ulong clientId)
        {
            // [KHW 추가 기능]
            // InteractionSystem이 IInteractable.OnInteract를 호출하면 서버에 루팅 요청을 보낸다.
            TryLootServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryLootServerRpc(ServerRpcParams rpc = default)
        {
            // [KHW 추가 기능]
            // 루팅/인벤토리 변경은 서버 권위로 처리한다.
            ulong clientId = rpc.Receive.SenderClientId;

            if (NetworkManager.Singleton == null) return;
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
            if (client.PlayerObject == null) return;

            IInventory inventory = client.PlayerObject.GetComponent<IInventory>();
            if (inventory == null)
            {
                Debug.LogWarning($"[KHWLootInteractable] 플레이어에게 IInventory가 없습니다. clientId={clientId}");
                return;
            }

            ItemDataSO item = ResolveItem();
            if (item == null)
            {
                Debug.LogWarning($"[KHWLootInteractable] ItemId를 ItemDataSO로 변환하지 못했습니다. itemId={ItemId.Value}");
                return;
            }

            if (normalizeOneCellOnLoot)
                NormalizeOneCell(item);

            int amount = Mathf.Max(1, Amount.Value);

            if (!inventory.TryAddItem(item, amount))
            {
                Debug.Log($"[KHWLootInteractable] 인벤토리에 빈 칸이 없어 루팅 실패: {item.itemID} x{amount}");
                return;
            }

            EventBus.Publish(new ItemLootedEvent
            {
                clientId = clientId,
                itemId = ItemId.Value,
                amount = amount,
            });

            if (despawnOnLootSuccess && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(destroy: true);
        }

        private void InitializeInternal(ItemDataSO item, int amount)
        {
            if (!IsServer || item == null) return;

            if (normalizeOneCellOnLoot)
                NormalizeOneCell(item);

            ItemId.Value = item.itemID;
            Amount.Value = Mathf.Max(1, amount);
            cachedItem = item;
        }

        private ItemDataSO ResolveItem()
        {
            string id = ItemId.Value.ToString();

            if (cachedItem != null && cachedItem.itemID == id)
                return cachedItem;

            if (scriptObjectPool != null && scriptObjectPool.TryGetItem(id, out ItemDataSO pooledItem))
            {
                cachedItem = pooledItem;
                return cachedItem;
            }

            if (fallbackItemDatabase != null)
            {
                foreach (ItemDataSO item in fallbackItemDatabase)
                {
                    if (item != null && item.itemID == id)
                    {
                        cachedItem = item;
                        return cachedItem;
                    }
                }
            }

            return null;
        }

        private void NormalizeOneCell(ItemDataSO item)
        {
            if (scriptObjectPool != null)
            {
                scriptObjectPool.NormalizeOneCell(item);
                return;
            }

            if (item != null)
                item.gridSize = Vector2Int.one;
        }
    }
}

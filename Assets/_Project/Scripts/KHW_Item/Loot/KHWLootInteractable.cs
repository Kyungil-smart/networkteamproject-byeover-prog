using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Actors
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 파밍 상자에서 생성된 월드 아이템입니다.
    /// 플레이어가 상호작용하면 서버에서 플레이어 IInventory를 찾아 TryAddItem을 호출합니다.
    /// 기존 GridInventory.cs는 수정하지 않고 IInventory 인터페이스로만 연결합니다.
    /// </summary>
    public class KHWLootInteractable : NetworkBehaviour, IInteractable, ILootCarrier
    {
        [Header("아이템 데이터베이스")]
        [Tooltip("Project 창에서 만든 KHWScriptObjectPoolSO 에셋을 넣습니다. itemID로 실제 ItemDataSO를 찾습니다.")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("상호작용 문구")]
        [Tooltip("{0}=아이템 이름, {1}=수량")]
        [SerializeField] private string promptFormat = "[F] 줍기: {0} x{1}";

        [Tooltip("아이템 정보를 찾지 못했을 때 표시할 기본 문구")]
        [SerializeField] private string fallbackPrompt = "[F] 줍기";

        [Header("획득 처리")]
        [Tooltip("체크하면 인벤토리에 넣기 성공 후 네트워크 오브젝트를 제거합니다.")]
        [SerializeField] private bool despawnOnLootSuccess = true;

        [Header("예비 아이템 목록")]
        [Tooltip("Pool SO를 못 찾을 때를 대비한 예비 목록입니다. 가능하면 Pool SO를 사용하세요.")]
        [SerializeField] private ItemDataSO[] fallbackItemDatabase;

        [Header("네트워크 동기화 값 - 런타임 확인용")]
        [Tooltip("파밍 상자에서 Initialize될 때 자동으로 세팅됩니다.")]
        public NetworkVariable<FixedString64Bytes> ItemId = new NetworkVariable<FixedString64Bytes>(new FixedString64Bytes(""));

        [Tooltip("파밍 상자에서 Initialize될 때 자동으로 세팅됩니다.")]
        public NetworkVariable<ushort> Amount = new NetworkVariable<ushort>(1);

        private ItemDataSO cachedItem;

        /// <summary>
        /// [KHW 추가 기능]
        /// 기존 LootSpawner/LootContainer 계열이 ILootCarrier.Initialize(item)을 호출해도 동작하도록 유지합니다.
        /// 코드 역할: 기존 인터페이스와 호환됩니다.
        /// </summary>
        public void Initialize(ItemDataSO item)
        {
            InitializeWithAmount(item, 1);
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// KHWLootContainer가 아이템 수량까지 같이 넣을 때 사용하는 초기화 함수입니다.
        /// </summary>
        public void InitializeWithAmount(ItemDataSO item, int amount)
        {
            if (!IsServer) return;
            if (item == null) return;

            ItemId.Value = new FixedString64Bytes(item.itemID);
            Amount.Value = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue);
            cachedItem = item;
        }

        public string GetPromptText()
        {
            ItemDataSO item = GetItem();
            if (item == null)
            {
                return fallbackPrompt;
            }

            return string.Format(promptFormat, item.displayName, Amount.Value);
        }

        public void OnInteract(ulong clientId)
        {
            TryLootServerRpc();
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// 실제 아이템 획득은 서버에서만 처리합니다.
        /// 코드 역할: 서버 권위 네트워크 구조를 지키기 위해 클라이언트는 요청만 보내고, 서버가 인벤토리에 넣습니다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void TryLootServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (NetworkManager.Singleton == null) return;
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(senderClientId)) return;

            NetworkClient client = NetworkManager.Singleton.ConnectedClients[senderClientId];
            if (client.PlayerObject == null) return;

            IInventory inventory = client.PlayerObject.GetComponent<IInventory>();
            if (inventory == null)
            {
                Debug.LogWarning("[KHWLootInteractable] 플레이어에게 IInventory 구현 컴포넌트가 없습니다.");
                return;
            }

            ItemDataSO item = GetItem();
            if (item == null)
            {
                Debug.LogWarning("[KHWLootInteractable] itemID에 해당하는 ItemDataSO를 찾지 못했습니다: " + ItemId.Value.ToString());
                return;
            }

            bool added = inventory.TryAddItem(item, Amount.Value);
            if (!added)
            {
                Debug.Log("[KHWLootInteractable] 인벤토리에 빈 칸이 없거나 추가 실패: " + item.itemID);
                return;
            }

            if (despawnOnLootSuccess)
            {
                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        private ItemDataSO GetItem()
        {
            if (cachedItem != null)
            {
                return cachedItem;
            }

            string itemId = ItemId.Value.ToString();
            if (scriptObjectPool != null)
            {
                cachedItem = scriptObjectPool.Lookup(itemId);
                if (cachedItem != null)
                {
                    return cachedItem;
                }
            }

            if (fallbackItemDatabase != null)
            {
                for (int i = 0; i < fallbackItemDatabase.Length; i++)
                {
                    ItemDataSO item = fallbackItemDatabase[i];
                    if (item == null) continue;
                    if (item.itemID == itemId)
                    {
                        cachedItem = item;
                        return cachedItem;
                    }
                }
            }

            return null;
        }
    }
}

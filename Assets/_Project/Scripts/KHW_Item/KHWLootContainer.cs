using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// LootSpawner 없이 파밍 상자에서만 아이템을 생성하고, 6칸 상자 UI에 표시하는 컨테이너입니다.
    /// 기존 LootContainer.cs는 수정하지 않고 이 컴포넌트를 상자 오브젝트에 새로 붙여 사용합니다.
    /// </summary>
    public class KHWLootContainer : NetworkBehaviour, IInteractable
    {
        [Header("파밍 상자 데이터")]
        [Tooltip("상자에서 어떤 아이템이 나올지 정하는 기존 LootTableSO입니다.")]
        [SerializeField] private LootTableSO lootTable;

        [Tooltip("itemID로 실제 ItemDataSO / WeaponDataSO / AmmoDataSO / HelmetDataSO / ArmorDataSO를 찾는 데이터베이스 SO입니다.")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("상자 슬롯 설정")]
        [Tooltip("테스트 UI에 표시할 상자 슬롯 개수입니다. 요청 조건에 맞춰 기본값은 6칸입니다.")]
        [SerializeField] private int slotCount = 6;

        [Tooltip("상자 안에 실제로 생성할 아이템 개수입니다. slotCount보다 크면 slotCount까지만 생성됩니다.")]
        [SerializeField] private int rollCount = 6;

        [Header("상호작용 문구")]
        [Tooltip("상자가 아직 열리지 않았을 때 표시할 문구입니다.")]
        [SerializeField] private string closedPrompt = "[F] 파밍 상자 열기";

        [Tooltip("상자가 이미 열린 후 다시 확인할 때 표시할 문구입니다.")]
        [SerializeField] private string openedPrompt = "[F] 파밍 상자 확인";

        [Header("잠금 설정")]
        [Tooltip("비워두면 잠금 없음. 값이 있으면 현재 테스트 버전에서는 잠긴 상자로 처리합니다.")]
        [SerializeField] private string requiredKeyId = "";

        [Header("연출")]
        [Tooltip("상자 열림 애니메이션이 있으면 Animator를 연결합니다. Trigger 이름은 Open입니다.")]
        [SerializeField] private Animator animator;

        public NetworkVariable<bool> IsOpened = new NetworkVariable<bool>(false);
        public NetworkList<KHWContainerSlotNetData> Slots;

        public KHWScriptObjectPoolSO ScriptObjectPool
        {
            get
            {
                return scriptObjectPool;
            }
        }

        private void Awake()
        {
            Slots = new NetworkList<KHWContainerSlotNetData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                EnsureEmptySlots();
            }
        }

        public string GetPromptText()
        {
            if (!string.IsNullOrEmpty(requiredKeyId))
            {
                return "[F] 잠김";
            }

            if (IsOpened.Value)
            {
                return openedPrompt;
            }

            return closedPrompt;
        }

        public void OnInteract(ulong clientId)
        {
            // [KHW 추가 기능]
            // 기존 InteractionSystem은 IInteractable.OnInteract만 호출합니다.
            // 여기서는 상자 오픈 요청을 서버로 보내고, 서버가 성공하면 해당 클라이언트에게 UI를 열라고 TargetClientRpc를 보냅니다.
            TryOpenAndShowServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryOpenAndShowServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!string.IsNullOrEmpty(requiredKeyId))
            {
                return;
            }

            if (!IsOpened.Value)
            {
                GenerateLootSlots();
                IsOpened.Value = true;

                if (animator != null)
                {
                    animator.SetTrigger("Open");
                }
            }

            ClientRpcParams target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderClientId }
                }
            };

            ShowContainerUiClientRpc(target);
        }

        [ClientRpc]
        private void ShowContainerUiClientRpc(ClientRpcParams clientRpcParams = default)
        {
            // [KHW 추가 기능]
            // 네트워크 오브젝트 자체는 모든 클라이언트에 존재하므로, TargetClientRpc를 받은 클라이언트만 로컬 UI를 엽니다.
            KHWContainerLootWindowUI ui = FindObjectOfType<KHWContainerLootWindowUI>(true);
            if (ui != null)
            {
                ui.Open(this);
            }
        }

        private void EnsureEmptySlots()
        {
            if (Slots == null) return;

            while (Slots.Count < slotCount)
            {
                Slots.Add(new KHWContainerSlotNetData
                {
                    itemId = new FixedString64Bytes(""),
                    amount = 0
                });
            }
        }

        private void GenerateLootSlots()
        {
            if (!IsServer) return;
            if (lootTable == null) return;

            Slots.Clear();
            EnsureEmptySlots();

            int createCount = Mathf.Clamp(rollCount, 0, slotCount);

            for (int i = 0; i < createCount; i++)
            {
                ItemDataSO item;
                int amount;

                bool success = KHWLootRollUtility.TryRoll(lootTable, out item, out amount);
                if (!success || item == null) continue;

                int emptyIndex = FindEmptySlotIndex();
                if (emptyIndex < 0) break;

                Slots[emptyIndex] = new KHWContainerSlotNetData
                {
                    itemId = new FixedString64Bytes(item.itemID),
                    amount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize)
                };
            }
        }

        private int FindEmptySlotIndex()
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].IsEmpty)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool TryGetSlot(int index, out KHWContainerSlotNetData data)
        {
            data = default;

            if (Slots == null) return false;
            if (index < 0 || index >= Slots.Count) return false;

            data = Slots[index];
            return true;
        }

        public ItemDataSO LookupItem(string itemId)
        {
            if (scriptObjectPool == null) return null;
            return scriptObjectPool.Lookup(itemId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TransferSlotToPlayerInventoryServerRpc(int slotIndex, ServerRpcParams rpcParams = default)
        {
            // [KHW 추가 기능]
            // 상자 슬롯에서 플레이어 인벤토리로 드래그 앤 드롭했을 때 서버에서만 실제 이동을 처리합니다.
            // 기존 GridInventory.cs는 수정하지 않고 IInventory.TryAddItem(item, amount)만 호출합니다.
            if (!IsServer) return;
            if (Slots == null) return;
            if (slotIndex < 0 || slotIndex >= Slots.Count) return;

            KHWContainerSlotNetData slot = Slots[slotIndex];
            if (slot.IsEmpty) return;

            string itemId = slot.itemId.ToString();
            ItemDataSO item = LookupItem(itemId);
            if (item == null) return;

            ulong clientId = rpcParams.Receive.SenderClientId;
            NetworkClient client;
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out client)) return;
            if (client.PlayerObject == null) return;

            IInventory inventory = client.PlayerObject.GetComponent<IInventory>();
            if (inventory == null) return;

            bool added = inventory.TryAddItem(item, slot.amount);
            if (!added) return;

            EventBus.Publish(new ItemLootedEvent
            {
                clientId = clientId,
                itemId = slot.itemId,
                amount = slot.amount
            });

            Slots[slotIndex] = new KHWContainerSlotNetData
            {
                itemId = new FixedString64Bytes(""),
                amount = 0
            };
        }
    }
}

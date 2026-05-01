using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가/수정 스크립트]
    /// LootSpawner와 UI 없이, 파밍 상자를 열었을 때 랜덤 아이템 6개를 생성하고 Console 창에 출력하는 테스트용 컨테이너입니다.
    /// 기존 LootContainer.cs, LootInteractable.cs, GridInventory.cs는 수정하지 않고 이 KHW 전용 컨테이너만 사용합니다.
    /// </summary>
    public class KHWLootContainer : NetworkBehaviour, IInteractable
    {
        [Header("파밍 상자 데이터")]
        [Tooltip("상자에서 랜덤으로 뽑을 아이템 후보 테이블입니다. 기존 LootTableSO를 그대로 사용합니다.")]
        [SerializeField] private LootTableSO lootTable;

        [Tooltip("itemID로 ItemDataSO, WeaponDataSO, AmmoDataSO, HelmetDataSO, ArmorDataSO를 찾는 데이터베이스 SO입니다. GameObject에 붙이는 컴포넌트가 아닙니다.")]
        [SerializeField] private KHWScriptObjectPoolSO scriptObjectPool;

        [Header("랜덤 생성 설정")]
        [Tooltip("테스트용 파밍 상자 슬롯 개수입니다. 요청 조건에 맞춰 기본값은 6입니다.")]
        [SerializeField] private int slotCount = 6;

        [Tooltip("상자를 처음 열 때 랜덤으로 생성할 아이템 개수입니다. 기본값은 6입니다.")]
        [SerializeField] private int rollCount = 6;

        [Tooltip("체크하면 상자를 다시 열 때마다 새로 랜덤 아이템을 생성합니다. 해제하면 한 번 열린 상자는 같은 결과를 유지합니다.")]
        [SerializeField] private bool rerollEveryOpen = false;

        [Header("콘솔 디버그")]
        [Tooltip("체크하면 상자를 열 때 Console 창에 어떤 아이템이 나왔는지 출력합니다.")]
        [SerializeField] private bool printDebugLogOnOpen = true;

        [Tooltip("로그 앞에 붙일 상자 이름입니다. 비워두면 GameObject 이름을 사용합니다.")]
        [SerializeField] private string debugContainerName = "";

        [Header("테스트 인벤토리 이동")]
        [Tooltip("체크하면 상자를 열자마자 생성된 아이템을 플레이어 GridInventory에 넣어보는 테스트를 합니다. 단순 로그 테스트면 해제하세요.")]
        [SerializeField] private bool addGeneratedLootToPlayerInventoryOnOpen = false;

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
            // [KHW 수정 기능]
            // NetworkManager가 시작되지 않은 상태에서 ServerRpc를 호출하면
            // "Rpc methods can only be invoked after starting the NetworkManager!" 오류가 발생한다.
            // 그래서 네트워크가 실행 중이면 ServerRpc 경로를 사용하고,
            // 네트워크가 꺼진 일반 Play 테스트에서는 로컬 로그 출력만 실행한다.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                OpenAndPrintDebugLocal(clientId);
                return;
            }

            TryOpenAndPrintDebugServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void TryOpenAndPrintDebugServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!string.IsNullOrEmpty(requiredKeyId))
            {
                Debug.Log("[KHWLootContainer] 잠긴 상자입니다. requiredKeyId=" + requiredKeyId, this);
                return;
            }

            bool shouldGenerate = !IsOpened.Value || rerollEveryOpen;
            if (shouldGenerate)
            {
                GenerateLootSlots();
                IsOpened.Value = true;

                if (animator != null)
                {
                    animator.SetTrigger("Open");
                }
            }

            if (printDebugLogOnOpen)
            {
                PrintLootDebugLog(senderClientId);
            }

            if (addGeneratedLootToPlayerInventoryOnOpen)
            {
                TryAddAllSlotsToPlayerInventory(senderClientId);
            }
        }

        private void OpenAndPrintDebugLocal(ulong clientId)
        {
            // [KHW 추가 기능]
            // NetworkManager가 시작되지 않은 일반 Play 테스트에서는
            // NetworkList / NetworkVariable을 절대 수정하지 않고,
            // 임시 List에만 랜덤 아이템을 생성해서 Console에 출력합니다.

            if (!string.IsNullOrEmpty(requiredKeyId))
            {
                Debug.Log("[KHWLootContainer] 잠긴 상자입니다. requiredKeyId=" + requiredKeyId, this);
                return;
            }

            List<KHWContainerSlotNetData> localSlots = GenerateLootSlotsForLocalDebug();

            if (printDebugLogOnOpen)
            {
                PrintLootDebugLogLocal(clientId, localSlots);
            }

            if (animator != null)
            {
                animator.SetTrigger("Open");
            }
        }

        private void PrintLootDebugLogLocal(ulong clientId, List<KHWContainerSlotNetData> localSlots)
        {
            // [KHW 추가 기능]
            // 로컬 테스트용 Console 출력 함수입니다.
            // NetworkList인 Slots 대신 일반 List를 읽어서 출력합니다.

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.AppendLine("========== [KHW 로컬 파밍 상자 오픈 로그] ==========");
            sb.AppendLine("상자 이름: " + gameObject.name);
            sb.AppendLine("상호작용 ClientId: " + clientId);
            sb.AppendLine("생성 아이템 수: " + localSlots.Count);
            sb.AppendLine("---------------------------------------------");

            for (int i = 0; i < localSlots.Count; i++)
            {
                string itemId = localSlots[i].itemId.ToString();
                int amount = localSlots[i].amount;

                ItemDataSO item = null;

                if (scriptObjectPool != null)
                {
                    item = scriptObjectPool.Lookup(itemId);
                }

                string itemName = "알 수 없음";
                if (item != null)
                {
                    itemName = item.displayName;
                    if (string.IsNullOrEmpty(itemName))
                    {
                        itemName = item.name;
                    }
                }

                string category = GetCategoryName(item);

                sb.AppendLine(
                    "[" + i + "] "
                    + "분류=" + category
                    + " / ID=" + itemId
                    + " / 이름=" + itemName
                    + " / 수량=" + amount
                );
            }

            sb.AppendLine("=============================================");

            Debug.Log(sb.ToString(), this);
        }

        private string GetCategoryName(ItemDataSO item)
        {
            // [KHW 추가 기능]
            // ItemDataSO의 실제 타입을 검사해서 Console에 표시할 분류명을 반환합니다.

            if (item == null)
            {
                return "알 수 없음";
            }

            if (item is WeaponDataSO)
            {
                return "무기";
            }

            if (item is AmmoDataSO)
            {
                return "탄약";
            }

            if (item is HelmetDataSO)
            {
                return "헬멧";
            }

            if (item is ArmorDataSO)
            {
                return "방어구";
            }

            return "일반 아이템";
        }

        private List<KHWContainerSlotNetData> GenerateLootSlotsForLocalDebug()
        {
            // [KHW 추가 기능]
            // 로컬 테스트 전용 랜덤 아이템 생성 함수입니다.
            // NetworkList를 수정하지 않고 일반 List만 사용합니다.

            List<KHWContainerSlotNetData> localSlots = new List<KHWContainerSlotNetData>();

            if (lootTable == null)
            {
                Debug.LogWarning("[KHWLootContainer] LootTable이 비어 있어서 아이템을 생성할 수 없습니다.", this);
                return localSlots;
            }

            int createCount = Mathf.Clamp(rollCount, 0, slotCount);

            for (int i = 0; i < createCount; i++)
            {
                ItemDataSO item;
                int amount;

                bool success = KHWLootRollUtility.TryRoll(lootTable, out item, out amount);
                if (!success || item == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(item.itemID))
                {
                    Debug.LogWarning("[KHWLootContainer] itemID가 비어 있는 아이템은 파밍 결과에 넣을 수 없습니다: " + item.name, this);
                    continue;
                }

                KHWContainerSlotNetData slotData = new KHWContainerSlotNetData
                {
                    itemId = new FixedString64Bytes(item.itemID),
                    amount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize)
                };

                localSlots.Add(slotData);
            }

            return localSlots;
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
            // [KHW 수정 기능]
            // UI 슬롯 표시용이 아니라 Console 출력용 데이터로 NetworkList에 6개 랜덤 결과를 저장합니다.
            if (!IsServer) return;

            Slots.Clear();
            EnsureEmptySlots();

            if (lootTable == null)
            {
                Debug.LogWarning("[KHWLootContainer] LootTable이 비어 있어서 아이템을 생성할 수 없습니다.", this);
                return;
            }

            int createCount = Mathf.Clamp(rollCount, 0, slotCount);

            for (int i = 0; i < createCount; i++)
            {
                ItemDataSO item;
                int amount;
                bool success = KHWLootRollUtility.TryRoll(lootTable, out item, out amount);
                if (!success || item == null)
                {
                    continue;
                }

                int emptyIndex = FindEmptySlotIndex();
                if (emptyIndex < 0)
                {
                    break;
                }

                Slots[emptyIndex] = new KHWContainerSlotNetData
                {
                    itemId = new FixedString64Bytes(item.itemID),
                    amount = (ushort)Mathf.Clamp(amount, 1, item.maxStackSize)
                };
            }
        }

        private void GenerateLootSlotsLocal()
        {
            // [KHW 추가 기능]
            // NetworkList는 원래 서버에서 수정하는 것이 맞지만,
            // 네트워크가 시작되지 않은 에디터 테스트에서는 서버 권한이 없으므로
            // Console 확인용으로만 로컬에서 슬롯 데이터를 채웁니다.

            if (Slots == null)
            {
                Debug.LogWarning("[KHWLootContainer] Slots가 초기화되지 않았습니다.", this);
                return;
            }

            if (lootTable == null)
            {
                Debug.LogWarning("[KHWLootContainer] LootTable이 비어 있어서 아이템을 생성할 수 없습니다.", this);
                return;
            }

            int createCount = Mathf.Clamp(rollCount, 0, slotCount);

            for (int i = 0; i < createCount; i++)
            {
                ItemDataSO item;
                int amount;

                bool success = KHWLootRollUtility.TryRoll(lootTable, out item, out amount);
                if (!success || item == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(item.itemID))
                {
                    Debug.LogWarning("[KHWLootContainer] itemID가 비어 있는 아이템은 파밍 결과에 넣을 수 없습니다: " + item.name, this);
                    continue;
                }

                int emptyIndex = FindEmptySlotIndex();
                if (emptyIndex < 0)
                {
                    break;
                }
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

        private void PrintLootDebugLog(ulong clientId)
        {
            // [KHW 추가 기능]
            // 상자 오픈 결과를 한 번에 보기 좋게 Console 창에 출력합니다.
            string title = debugContainerName;
            if (string.IsNullOrEmpty(title))
            {
                title = gameObject.name;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("========== [KHW 파밍 상자 오픈 로그] ==========");
            builder.AppendLine("상자 이름: " + title);
            builder.AppendLine("상호작용 ClientId: " + clientId);
            builder.AppendLine("생성 슬롯 수: " + slotCount + " / 랜덤 생성 수: " + rollCount);
            builder.AppendLine("---------------------------------------------");

            if (Slots == null || Slots.Count == 0)
            {
                builder.AppendLine("슬롯 데이터가 없습니다.");
            }
            else
            {
                for (int i = 0; i < Slots.Count; i++)
                {
                    KHWContainerSlotNetData slot = Slots[i];
                    if (slot.IsEmpty)
                    {
                        builder.AppendLine("[" + i + "] 비어 있음");
                        continue;
                    }

                    ItemDataSO item = LookupItem(slot.itemId.ToString());
                    builder.AppendLine(KHWLootDebugFormatter.BuildItemLogLine(i, item, slot.amount));
                }
            }

            builder.AppendLine("=============================================");
            Debug.Log(builder.ToString(), this);
        }

        private void TryAddAllSlotsToPlayerInventory(ulong clientId)
        {
            // [KHW 선택 기능]
            // Console 테스트 중에도 실제 GridInventory 연동을 보고 싶을 때만 사용합니다.
            // 기존 GridInventory.cs는 수정하지 않고 IInventory.TryAddItem(item, amount)만 호출합니다.
            if (!IsServer) return;
            if (NetworkManager.Singleton == null) return;
            if (Slots == null) return;

            NetworkClient client;
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out client))
            {
                Debug.LogWarning("[KHWLootContainer] ClientId를 찾을 수 없습니다: " + clientId, this);
                return;
            }

            if (client.PlayerObject == null)
            {
                Debug.LogWarning("[KHWLootContainer] PlayerObject가 없습니다: " + clientId, this);
                return;
            }

            IInventory inventory = client.PlayerObject.GetComponent<IInventory>();
            if (inventory == null)
            {
                Debug.LogWarning("[KHWLootContainer] PlayerPrefab에 IInventory/GridInventory가 없습니다.", this);
                return;
            }

            for (int i = 0; i < Slots.Count; i++)
            {
                KHWContainerSlotNetData slot = Slots[i];
                if (slot.IsEmpty)
                {
                    continue;
                }

                ItemDataSO item = LookupItem(slot.itemId.ToString());
                if (item == null)
                {
                    Debug.LogWarning("[KHWLootContainer] itemID를 Pool에서 찾지 못했습니다: " + slot.itemId.ToString(), this);
                    continue;
                }

                bool added = inventory.TryAddItem(item, slot.amount);
                Debug.Log("[KHWLootContainer] 인벤토리 추가 테스트 / 슬롯=" + i + " / itemID=" + item.itemID + " / 수량=" + slot.amount + " / 결과=" + added, this);

                if (added)
                {
                    Slots[i] = new KHWContainerSlotNetData
                    {
                        itemId = new FixedString64Bytes(""),
                        amount = 0
                    };
                }
            }
        }
    }
}

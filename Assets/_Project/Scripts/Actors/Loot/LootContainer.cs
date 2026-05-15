using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;
using DeadZone.Actors.UI;
using DeadZone.Systems;
using DeadZone.Systems.Audio;

namespace DeadZone.Actors
{
    public class LootContainer : NetworkBehaviour, IInteractable
    {
        [Header("상자 기본 데이터")]
        [Tooltip("이 상자에서 직접 검색 후보로 사용할 아이템 목록입니다. 비어 있으면 호환용 LootTableSO를 사용합니다.")]
        [SerializeField] private LootEntry[] searchEntries;

        [Tooltip("호환용 후보 테이블입니다. 새 상자는 searchEntries를 우선 사용하세요.")]
        [SerializeField] private LootTableSO lootTable;

        [Tooltip("Console 로그에 표시할 상자 등급 이름입니다.")]
        [SerializeField] private string containerGradeName = "Common";

        [Header("슬롯/랜덤 생성")]
        [Tooltip("상자 내부 슬롯 수입니다.")]
        [SerializeField] private int slotCount = 6;

        [Tooltip("상자를 처음 열 때 실제로 생성할 품목 수 범위입니다.")]
        [SerializeField] private Vector2Int itemCountRange = new(1, 6);

        [Tooltip("처음 열 때 랜덤 생성할 아이템 개수입니다.")]
        [SerializeField] private int rollCount = 6;

        [Tooltip("기존 상자 설정과의 직렬화 호환을 위해 유지합니다. 실제 추첨은 유효한 weight 총합을 기준으로 수행합니다.")]
        [SerializeField] private bool requireTotalWeight100 = true;

        [Header("총기 상자 규칙")]
        [Tooltip("총기 상자 전용 추첨 규칙을 사용할지 여부입니다.")]
        [SerializeField] private bool weaponBoxMode = false;

        [Tooltip("총기 상자에서 보장할 최소 무기 개수입니다.")]
        [SerializeField] private int minWeaponCount = 1;

        [Tooltip("총기 상자에서 허용할 최대 무기 개수입니다.")]
        [SerializeField] private int maxWeaponCount = 2;

        [Header("Console 디버그")]
        [Tooltip("상자를 열었을 때 서버 슬롯 결과를 Console에 출력합니다.")]
        [SerializeField] private bool printDebugLogOnOpen = true;

        [Tooltip("Console에 표시할 상자 이름입니다. 비어 있으면 GameObject 이름을 사용합니다.")]
        [SerializeField] private string debugContainerName = "";
        [SerializeField] private bool warnWhenChildCollidersShareContainer = false;

        [Header("상호작용 문구")]
        [Tooltip("닫힌 상자에 표시할 상호작용 문구입니다.")]
        [SerializeField] private string closedPrompt = "[F] 파밍 상자 열기";

        [Tooltip("열린 상자에 표시할 상호작용 문구입니다.")]
        [SerializeField] private string openedPrompt = "[F] 파밍 상자 확인";
        [SerializeField] private string searchingPrompt = "Searching...";

        [Header("Search")]
        [SerializeField, Min(0f)] private float searchDurationSeconds = 2f;

        [Header("파밍 오디오")]
        [Tooltip("켜면 F키로 상자 열기/확인에 성공했을 때 파밍 사운드를 재생합니다.")]
        [SerializeField] private bool playLootSoundOnOpen = true;

        [Tooltip("켜면 상자 슬롯의 아이템을 플레이어 인벤토리로 가져왔을 때 파밍 사운드를 재생합니다.")]
        [SerializeField] private bool playLootSoundOnTake = true;

        [Tooltip("상자 파밍 사운드 볼륨 배율입니다. AudioLibrary의 개별 볼륨과 AudioManager의 SFX 볼륨이 함께 적용됩니다.")]
        [SerializeField, Range(0f, 2f)] private float lootSoundVolumeMultiplier = 1f;

        public NetworkVariable<bool> IsOpened =
            new NetworkVariable<bool>(false);

        public NetworkVariable<bool> IsSearching =
            new NetworkVariable<bool>(false);

        public NetworkVariable<double> SearchEndsAt =
            new NetworkVariable<double>(0d);

        public NetworkList<global::ContainerSlotNetData> Slots;

        private bool localOpened;
        private List<global::ContainerSlotNetData> localSlots;
        private int lastGeneratedItemCount;
        private Coroutine searchRoutine;
        private bool hasGeneratedLoot;

        public LootTableSO LootTable => lootTable;
        public int SlotCount => slotCount;

        private void Awake()
        {
            Slots = new NetworkList<global::ContainerSlotNetData>(
                values: null,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);
        }

        private void OnValidate()
        {
            slotCount = Mathf.Max(1, slotCount);
            itemCountRange.x = Mathf.Clamp(itemCountRange.x, 0, slotCount);
            itemCountRange.y = Mathf.Clamp(itemCountRange.y, itemCountRange.x, slotCount);
            rollCount = Mathf.Clamp(rollCount, 1, slotCount);
            minWeaponCount = Mathf.Clamp(minWeaponCount, 0, Mathf.Max(1, itemCountRange.y));
            maxWeaponCount = Mathf.Clamp(maxWeaponCount, minWeaponCount, Mathf.Max(1, itemCountRange.y));
        }

        public override void OnNetworkSpawn()
        {
            WarnIfChildCollidersShareContainer();

            if (IsServer)
            {
                EnsureEmptySlots();
            }
        }

        private void WarnIfChildCollidersShareContainer()
        {
            if (!warnWhenChildCollidersShareContainer)
                return;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length <= 1)
                return;

            int childColliderCount = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].transform != transform)
                    childColliderCount++;
            }

            if (childColliderCount <= 0)
                return;

            Debug.Log(
                "[LootContainer] Multiple child colliders are attached to this container. " +
                "This is allowed for a single physical box; use one LootContainer per separate box.",
                this);
        }

        public string GetPromptText()
        {
            if (IsNetworkActive())
            {
                if (IsSearching.Value)
                    return searchingPrompt;

                return IsOpened.Value ? openedPrompt : closedPrompt;
            }

            return localOpened ? openedPrompt : closedPrompt;
        }

        public void OnInteract(ulong clientId)
        {
            if (IsNetworkActive())
            {
                if (IsSearching.Value)
                    return;

                if (IsOpened.Value)
                {
                    OpenLootingUI();
                    return;
                }

                TryOpenRpc();
                return;
            }

            OpenLocalForEditorDebug(clientId);
            OpenLootingUI();
        }

        public bool TryGetLocalSlot(int index, out global::ContainerSlotNetData slotData)
        {
            slotData = default;

            if (localSlots == null || index < 0 || index >= localSlots.Count)
                return false;

            slotData = localSlots[index];
            return true;
        }

        public void OpenLocalForLootingUITest(ulong clientId = 0)
        {
            OpenLocalForEditorDebug(clientId);
        }

        public void RequestTakeSlotToPlayer(int slotIndex)
        {
            if (IsNetworkActive())
            {
                TryTakeSlotToPlayerRpc(slotIndex);
                return;
            }

            Debug.LogWarning("[LootContainer] 네트워크가 실행 중이 아니어서 상자 아이템 이동은 서버 검증 없이 처리하지 않습니다.", this);
        }

        public void RequestDepositFromPlayer(string itemId, int amount, int targetSlotIndex)
        {
            if (IsNetworkActive())
            {
                TryDepositFromPlayerRpc(new FixedString64Bytes(itemId), amount, targetSlotIndex);
                return;
            }

            Debug.LogWarning("[LootContainer] 네트워크가 실행 중이 아니어서 플레이어 아이템 보관은 서버 검증 없이 처리하지 않습니다.", this);
        }

        public void RequestMoveSlot(int sourceSlotIndex, int targetSlotIndex)
        {
            if (IsNetworkActive())
            {
                TryMoveSlotRpc(sourceSlotIndex, targetSlotIndex);
                return;
            }

            Debug.LogWarning("[LootContainer] 네트워크가 실행 중이 아니어서 상자 내부 이동은 서버 검증 없이 처리하지 않습니다.", this);
        }

        public void RequestEquipSlotToPlayer(int sourceSlotIndex, EquipmentTargetSlot targetSlot)
        {
            if (IsNetworkActive())
            {
                TryEquipSlotToPlayerRpc(sourceSlotIndex, targetSlot);
                return;
            }

            Debug.LogWarning("[LootContainer] Network is not active. Container to equipment move skipped.", this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryOpenRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (IsOpened.Value)
            {
                if (!IsSearching.Value)
                    OpenLootingUiRpc(RpcTarget.Single(senderClientId, RpcTargetUse.Temp));

                return;
            }

            if (IsSearching.Value)
                return;

            if (!CanRollByProbabilityRule())
                return;

            if (searchRoutine != null)
            {
                StopCoroutine(searchRoutine);
                searchRoutine = null;
            }

            IsOpened.Value = true;
            OpenImmediatelyForClient(senderClientId);
        }

        private void OpenImmediatelyForClient(ulong requesterClientId)
        {
            IsSearching.Value = false;
            SearchEndsAt.Value = 0d;

            if (!hasGeneratedLoot)
            {
                List<global::ContainerSlotNetData> generated = GenerateSlotList();
                ApplyGeneratedSlotsToNetworkList(generated);
                hasGeneratedLoot = true;
            }

            if (printDebugLogOnOpen)
                PrintNetworkSlotsDebug(requesterClientId);

            if (playLootSoundOnOpen)
                PlayLootSoundForClient(requesterClientId, AudioCueId.Loot1);

            OpenLootingUiRpc(RpcTarget.Single(requesterClientId, RpcTargetUse.Temp));
        }

        private IEnumerator SearchAndOpenRoutine(ulong requesterClientId)
        {
            IsSearching.Value = true;
            SearchEndsAt.Value = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time + searchDurationSeconds
                : Time.timeAsDouble + searchDurationSeconds;

            if (searchDurationSeconds > 0f)
                yield return new WaitForSeconds(searchDurationSeconds);

            if (!hasGeneratedLoot)
            {
                List<global::ContainerSlotNetData> generated = GenerateSlotList();
                ApplyGeneratedSlotsToNetworkList(generated);
                hasGeneratedLoot = true;
            }

            IsSearching.Value = false;
            SearchEndsAt.Value = 0d;
            searchRoutine = null;

            if (printDebugLogOnOpen)
                PrintNetworkSlotsDebug(requesterClientId);

            if (playLootSoundOnOpen)
                PlayLootSoundForClient(requesterClientId, AudioCueId.Loot1);

            OpenLootingUiRpc(RpcTarget.Single(requesterClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void OpenLootingUiRpc(RpcParams rpcParams = default)
        {
            OpenLootingUI();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryTakeSlotToPlayerRpc(
            int slotIndex,
            RpcParams rpcParams = default)
        {
            if (!TryGetServerSlot(slotIndex, out global::ContainerSlotNetData slotData) || slotData.IsEmpty)
                return;

            GridInventory inventory = ResolvePlayerInventory(rpcParams.Receive.SenderClientId);
            if (inventory == null)
                return;

            ItemDataSO itemData = ResolveItemData(slotData.itemId.ToString());
            if (itemData == null)
                return;

            int amount = Mathf.Max(1, slotData.amount);
            ItemSlotData itemSlot = CreateInventorySlotData(itemData, slotData);
            if (!inventory.CanAddItemSlot(itemData, itemSlot))
                return;

            if (!inventory.TryAddItemSlot(itemData, itemSlot))
                return;

            Slots[slotIndex] = new global::ContainerSlotNetData();

            if (playLootSoundOnTake)
                PlayLootSoundForClient(rpcParams.Receive.SenderClientId, AudioCueId.Loot2);

            PublishItemLootedForClient(rpcParams.Receive.SenderClientId, slotData.itemId, amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryDepositFromPlayerRpc(
            FixedString64Bytes itemId,
            int amount,
            int targetSlotIndex,
            RpcParams rpcParams = default)
        {
            string itemIdString = itemId.ToString();
            if (string.IsNullOrEmpty(itemIdString) || amount <= 0)
                return;

            if (!TryGetServerSlot(targetSlotIndex, out global::ContainerSlotNetData targetSlot))
                return;

            GridInventory inventory = ResolvePlayerInventory(rpcParams.Receive.SenderClientId);
            if (inventory == null || !inventory.HasItem(itemIdString, amount))
                return;

            ItemDataSO itemData = ResolveItemData(itemIdString);
            if (itemData == null || !CanAcceptItem(targetSlot, itemData, amount))
                return;

            if (!inventory.ConsumeItem(itemIdString, amount))
                return;

            AddItemToSlot(targetSlotIndex, targetSlot, itemData, amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryMoveSlotRpc(
            int sourceSlotIndex,
            int targetSlotIndex,
            RpcParams rpcParams = default)
        {
            if (sourceSlotIndex == targetSlotIndex)
                return;

            if (!TryGetServerSlot(sourceSlotIndex, out global::ContainerSlotNetData sourceSlot) ||
                !TryGetServerSlot(targetSlotIndex, out global::ContainerSlotNetData targetSlot) ||
                sourceSlot.IsEmpty)
            {
                return;
            }

            ItemDataSO sourceItem = ResolveItemData(sourceSlot.itemId.ToString());
            if (sourceItem == null)
                return;

            if (!targetSlot.IsEmpty &&
                targetSlot.itemId.Equals(sourceSlot.itemId) &&
                CanAcceptItem(targetSlot, sourceItem, sourceSlot.amount))
            {
                AddItemToSlot(targetSlotIndex, targetSlot, sourceItem, sourceSlot.amount);
                Slots[sourceSlotIndex] = new global::ContainerSlotNetData();
                return;
            }

            Slots[sourceSlotIndex] = targetSlot;
            Slots[targetSlotIndex] = sourceSlot;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TryEquipSlotToPlayerRpc(
            int sourceSlotIndex,
            EquipmentTargetSlot targetSlot,
            RpcParams rpcParams = default)
        {
            if (targetSlot == EquipmentTargetSlot.None)
                return;

            if (!TryGetServerSlot(sourceSlotIndex, out global::ContainerSlotNetData sourceSlot) || sourceSlot.IsEmpty)
                return;

            EquipmentSlots equipmentSlots = ResolvePlayerEquipment(rpcParams.Receive.SenderClientId);
            if (equipmentSlots == null)
                return;

            GridInventory inventory = ResolvePlayerInventory(rpcParams.Receive.SenderClientId);

            ItemDataSO itemData = ResolveItemData(sourceSlot.itemId.ToString());
            if (itemData == null || !equipmentSlots.CanEquipItemToSlot(itemData, targetSlot))
                return;

            if (!equipmentSlots.IsEquipmentSlotEmpty(targetSlot))
            {
                if (inventory == null || !inventory.TryMoveEquipmentSlotToInventoryOnServer(targetSlot))
                    return;
            }

            ItemSlotData itemSlot = CreateInventorySlotData(itemData, sourceSlot);
            if (!equipmentSlots.TryEquipItemToSlot(itemData, itemSlot, targetSlot))
                return;

            if (sourceSlot.amount <= 1)
            {
                Slots[sourceSlotIndex] = new global::ContainerSlotNetData();
                return;
            }

            sourceSlot.amount--;
            Slots[sourceSlotIndex] = sourceSlot;
        }

        private static ItemSlotData CreateInventorySlotData(ItemDataSO itemData, global::ContainerSlotNetData sourceSlot)
        {
            return new ItemSlotData
            {
                itemId = sourceSlot.itemId,
                stackCount = (ushort)Mathf.Clamp(sourceSlot.amount, 1, Mathf.Max(1, itemData.maxStackSize)),
                gridX = 0,
                gridY = 0,
                rotated = false,
                currentDurability = itemData switch
                {
                    WeaponDataSO weapon => weapon.maxDurability,
                    ArmorDataSO armor => armor.maxDurability,
                    HelmetDataSO helmet => helmet.maxDurability,
                    _ => 0f
                },
                currentAmmo = 0
            };
        }

        private void OpenLocalForEditorDebug(ulong clientId)
        {
            if (!localOpened)
            {
                if (!CanRollByProbabilityRule())
                    return;

                localSlots = GenerateSlotList();
                localOpened = true;
            }

            if (printDebugLogOnOpen)
            {
                PrintLocalSlotsDebug(clientId);
            }
        }

        private void PlayLootSoundForClient(ulong clientId, AudioCueId cueId)
        {
            if (!IsServer)
                return;

            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            PlayLootSoundClientRpc(cueId, lootSoundVolumeMultiplier, rpcParams);
        }

        private void PublishItemLootedForClient(ulong clientId, FixedString64Bytes itemId, int amount)
        {
            if (!IsServer)
                return;

            EventBus.Publish(new ItemLootedEvent
            {
                clientId = clientId,
                itemId = itemId,
                amount = amount,
                suppressAudio = true,
            });

            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            PublishItemLootedClientRpc(clientId, itemId, amount, rpcParams);
        }

        [ClientRpc]
        private void PlayLootSoundClientRpc(
            AudioCueId cueId,
            float volumeMultiplier,
            ClientRpcParams rpcParams = default)
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = cueId,
                position = Vector3.zero,
                use3D = false,
                volumeMultiplier = volumeMultiplier
            });
        }

        [ClientRpc]
        private void PublishItemLootedClientRpc(
            ulong clientId,
            FixedString64Bytes itemId,
            int amount,
            ClientRpcParams rpcParams = default)
        {
            if (IsServer)
                return;

            EventBus.Publish(new ItemLootedEvent
            {
                clientId = clientId,
                itemId = itemId,
                amount = amount,
                suppressAudio = true,
            });
        }

        private bool CanRollByProbabilityRule()
        {
            IReadOnlyList<LootEntry> entries = ResolveActiveEntries();

            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning("[LootContainer] 검색 후보 아이템 목록이 비어 있습니다.", this);
                return false;
            }

            int totalWeight = global::LootRollUtility.GetTotalWeight(entries);
            if (totalWeight <= 0)
            {
                Debug.LogWarning("[LootContainer] 검색 후보에 유효한 item과 weight가 없습니다.", this);
                return false;
            }

            return true;
        }

        private List<global::ContainerSlotNetData> GenerateSlotList()
        {
            int generatedItemCount = RollItemCountForOpen();
            lastGeneratedItemCount = generatedItemCount;

            IReadOnlyList<LootEntry> entries = ResolveActiveEntries();
            if (entries != null && entries.Count > 0)
            {
                return global::LootRollUtility.RollSlots(
                    entries,
                    slotCount,
                    generatedItemCount,
                    requireTotalWeight100,
                    weaponBoxMode,
                    minWeaponCount,
                    maxWeaponCount);
            }

            return global::LootRollUtility.RollSlots(
                lootTable,
                slotCount,
                generatedItemCount,
                requireTotalWeight100,
                weaponBoxMode,
                minWeaponCount,
                maxWeaponCount);
        }

        private int RollItemCountForOpen()
        {
            int min = Mathf.Clamp(itemCountRange.x, 0, slotCount);
            int max = Mathf.Clamp(itemCountRange.y, min, slotCount);

            if (max <= 0)
                return 0;

            return Random.Range(min, max + 1);
        }

        private void ApplyGeneratedSlotsToNetworkList(List<global::ContainerSlotNetData> generated)
        {
            if (!IsServer)
                return;

            Slots.Clear();

            if (generated == null)
            {
                EnsureEmptySlots();
                return;
            }

            for (int i = 0; i < generated.Count && i < slotCount; i++)
            {
                Slots.Add(generated[i]);
            }

            EnsureEmptySlots();
        }

        private void EnsureEmptySlots()
        {
            if (Slots == null)
                return;

            while (Slots.Count < slotCount)
            {
                Slots.Add(new global::ContainerSlotNetData());
            }
        }

        private void PrintNetworkSlotsDebug(ulong clientId)
        {
            global::ContainerSlotNetData[] slotArray =
                new global::ContainerSlotNetData[Slots.Count];

            for (int i = 0; i < Slots.Count; i++)
            {
                slotArray[i] = Slots[i];
            }

            PrintSlotArrayDebug(clientId, slotArray);
        }

        private void PrintLocalSlotsDebug(ulong clientId)
        {
            if (localSlots == null)
            {
                Debug.LogWarning("[LootContainer] 로컬 슬롯 데이터가 없습니다.", this);
                return;
            }

            PrintSlotArrayDebug(clientId, localSlots.ToArray());
        }

        private void PrintSlotArrayDebug(
            ulong clientId,
            global::ContainerSlotNetData[] slotArray)
        {
            string containerName = debugContainerName;

            if (string.IsNullOrEmpty(containerName))
            {
                containerName = gameObject.name;
            }

            string log =
                global::LootDebugFormatter.BuildContainerLog(
                    containerName,
                    containerGradeName,
                    clientId,
                    slotCount,
                    lastGeneratedItemCount > 0 ? lastGeneratedItemCount : rollCount,
                    global::LootRollUtility.GetTotalWeight(ResolveActiveEntries()),
                    slotArray,
                    lootTable);

            Debug.Log(log, this);
        }

        private IReadOnlyList<LootEntry> ResolveActiveEntries()
        {
            if (searchEntries != null && searchEntries.Length > 0)
                return searchEntries;

            return lootTable != null ? lootTable.entries : null;
        }

        private bool TryGetServerSlot(int slotIndex, out global::ContainerSlotNetData slotData)
        {
            slotData = default;

            if (!IsServer || Slots == null || slotIndex < 0 || slotIndex >= Slots.Count)
                return false;

            slotData = Slots[slotIndex];
            return true;
        }

        private GridInventory ResolvePlayerInventory(ulong clientId)
        {
            if (!IsServer || NetworkManager.Singleton == null)
                return null;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return null;

            if (client.PlayerObject == null)
                return null;

            return client.PlayerObject.GetComponent<GridInventory>();
        }

        private EquipmentSlots ResolvePlayerEquipment(ulong clientId)
        {
            if (!IsServer || NetworkManager.Singleton == null)
                return null;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return null;

            if (client.PlayerObject == null)
                return null;

            return client.PlayerObject.GetComponent<EquipmentSlots>();
        }

        private ItemDataSO ResolveItemData(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            IItemDatabase itemDb = ServiceLocator.Get<IItemDatabase>();
            return itemDb?.GetById(itemId);
        }

        private bool CanAcceptItem(global::ContainerSlotNetData targetSlot, ItemDataSO itemData, int amount)
        {
            if (itemData == null || amount <= 0)
                return false;

            if (targetSlot.IsEmpty)
                return true;

            if (!targetSlot.itemId.ToString().Equals(itemData.itemID))
                return false;

            int maxStackSize = Mathf.Max(1, itemData.maxStackSize);
            return targetSlot.amount + amount <= maxStackSize;
        }

        private void AddItemToSlot(
            int slotIndex,
            global::ContainerSlotNetData currentSlot,
            ItemDataSO itemData,
            int amount)
        {
            if (!IsServer || itemData == null || slotIndex < 0 || slotIndex >= Slots.Count)
                return;

            if (currentSlot.IsEmpty)
            {
                Slots[slotIndex] = new global::ContainerSlotNetData
                {
                    itemId = new FixedString64Bytes(itemData.itemID),
                    amount = (ushort)Mathf.Clamp(amount, 1, ushort.MaxValue)
                };
                return;
            }

            currentSlot.amount = (ushort)Mathf.Clamp(
                currentSlot.amount + amount,
                1,
                Mathf.Max(1, itemData.maxStackSize));

            Slots[slotIndex] = currentSlot;
        }

        private void OpenLootingUI()
        {
            // 로컬 클라이언트에 떠 있는 루팅 UI를 찾아 이 상자 컨테이너를 바인딩합니다.
            LootingUIController controller = LootingUIController.ActiveInstance != null
                ? LootingUIController.ActiveInstance
                : Object.FindFirstObjectByType<LootingUIController>(FindObjectsInactive.Include);

            if (controller == null)
            {
                Debug.LogWarning("[LootContainer] LootingUIController를 찾지 못했습니다. 씬 UI에 LootingUIController를 배치하세요.", this);
                return;
            }

            controller.Open(this);
        }

        private bool IsNetworkActive()
        {
            if (NetworkManager.Singleton == null)
                return false;

            if (!NetworkManager.Singleton.IsListening)
                return false;

            return true;
        }
    }
}

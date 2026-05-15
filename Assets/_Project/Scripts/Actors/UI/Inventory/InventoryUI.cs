using System.Collections.Generic;
using DeadZone.Actors;
using DeadZone.Core;
using DeadZone.Systems;
using DeadZone.Systems.Raid;
using Unity.Collections;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI ActiveInstance { get; private set; }

        [BoxGroup("루트")]
        [Tooltip("InventoryVisibleRoot입니다. Inventory와 QuickSlotPanel 구조는 이 루트 아래에서 유지합니다.")]
        [SerializeField] private GameObject inventoryRoot;

        [BoxGroup("가방 설정")]
        [Tooltip("현재 가방 레벨입니다.")]
        [Range(0, 4)]
        [SerializeField] private int bagLevel;

        [BoxGroup("가방 슬롯")]
        [Tooltip("가방 슬롯 40개를 '순서'대로 넣으세요.")]
        [SerializeField] private List<InventorySlotUI> bagSlots = new();

        [BoxGroup("툴팁")]
        [Tooltip("씬에 배치된 툴팁 인스턴스입니다. 비워두면 Inventory Root 아래 비활성 자식까지 포함해 자동으로 찾습니다.")]
        [SerializeField] private ItemTooltipUI itemTooltipUI;

        [BoxGroup("드래그 앤 드롭")]
        [Tooltip("EquipmentPanel/QuickSlotPanel 하위 슬롯에 InventorySlotUI가 없으면 Play 시 자동으로 추가합니다.")]
        [SerializeField] private bool autoCreateDropSlots = true;

        [BoxGroup("런타임 인벤토리")]
        [Tooltip("인벤토리를 열 때 Owner Player의 GridInventory를 자동으로 찾아 UI에 표시합니다.")]
        [SerializeField] private bool autoBindOwnerGridInventory = true;

        [BoxGroup("런타임 인벤토리")]
        [Tooltip("GridInventory 표시 갱신 과정을 Console에 출력합니다.")]
        [SerializeField] private bool debugGridInventoryView;

        [BoxGroup("장비 연동")]
        [Tooltip("플레이어에 붙은 EquipmentSlotsBridge입니다. 비워두면 Owner 플레이어에서 자동 탐색합니다.")]
        [SerializeField] private EquipmentSlotsBridge equipmentSlotsBridge;

        [BoxGroup("장비 연동")]
        [Tooltip("브릿지가 없을 때 Host 테스트용으로만 사용하는 EquipmentSlots입니다. 클라이언트 권한 장착은 브릿지를 사용하세요.")]
        [SerializeField] private EquipmentSlots equipmentSlots;

        [BoxGroup("장비 연동")]
        [Tooltip("장착 직후 기능 테스트용으로 탄창을 채웁니다. 실제 탄약 인벤토리 연동 전까지 발사 검증에 사용합니다.")]
        [SerializeField] private bool fillMagazineOnEquip = true;

        [BoxGroup("장비 연동")]
        [Tooltip("테스트용 기본 장착 탄약입니다. 비워도 WeaponDataSO만으로 발사 이벤트와 투사체 생성은 동작합니다.")]
        [SerializeField] private AmmoDataSO defaultLoadedAmmo;

        [BoxGroup("장비 연동")]
        [Tooltip("무기 장착 요청이 어느 경로로 처리되는지 콘솔에 출력합니다.")]
        [SerializeField] private bool debugWeaponEquipEvents = true;

        private bool warnedMissingEquipmentBridge;
        private bool warnedUnsupportedClear;
        private IItemDatabase itemDatabase;

        private GridInventory boundGridInventory;
        private bool gridInventorySubscribed;
        private bool quickSlotsSubscribed;
        private EquipmentSlots subscribedEquipmentSlots;
        private bool equipmentSlotsSubscribed;
        private bool raidQuickSlotLoadoutApplied;

        [BoxGroup("ItemDataSO 테스트")]
        [Tooltip("랜덤 아이템 배치 테스트에 사용할 ItemDataSO 목록입니다.")]
        [SerializeField] private List<ItemDataSO> testItemPool = new();

        public bool IsOpen => inventoryRoot != null && inventoryRoot.activeSelf;

        private void Awake()
        {
            ActiveInstance = this;
            bagLevel = 0;
            ResolveTooltipUI();
            EnsureDropSlots();
            InitializeSlots();
            ApplyRaidQuickSlotLoadoutIfAvailable();
            RefreshBagSlots();

            Close();
        }

        private void OnEnable()
        {
            ActiveInstance = this;
            EventBus.Subscribe<BackpackChangedEvent>(HandleBackpackChanged);
            SubscribeGridInventory();
            SubscribeEquipmentSlots();
            ApplyRaidQuickSlotLoadoutIfAvailable();
        }

        private void Start()
        {
            ApplyRaidQuickSlotLoadoutIfAvailable();
        }

        private void OnDisable()
        {
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Inventory, false);
            EventBus.Unsubscribe<BackpackChangedEvent>(HandleBackpackChanged);
            UnsubscribeGridInventory();
            UnsubscribeEquipmentSlots();
        }

        private void OnDestroy()
        {
            UnsubscribeGridInventory();
            UnsubscribeEquipmentSlots();

            if (ActiveInstance == this)
                ActiveInstance = null;
        }

        private void OnValidate()
        {
            bagLevel = Mathf.Clamp(bagLevel, 0, 4);
        }

        private void InitializeSlots()
        {
            ResolveTooltipUI();
            EnsureDropSlots();

            for (int i = 0; i < bagSlots.Count; i++)
            {
                if (bagSlots[i] == null)
                    continue;

                bagSlots[i].PrepareDropSlot(itemTooltipUI, i, this);
                bagSlots[i].Initialize(i);
            }
        }

        public void Open()
        {
            if (inventoryRoot == null)
            {
                Debug.LogWarning("[InventoryUI] Inventory Root가 연결되지 않았습니다.", this);
                return;
            }

            inventoryRoot.SetActive(true);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Inventory, true);
            CursorStateController.PushUiOwner(this);
            ResolveTooltipUI();
            EnsureDropSlots();
            AssignTooltipToSlots();
            ApplyRaidQuickSlotLoadoutIfAvailable();
            RefreshBagSlots();

            if (autoBindOwnerGridInventory)
                BindOwnerGridInventoryIfNeeded();

            SubscribeEquipmentSlots();
            RefreshGridInventorySlots();
            RefreshEquipmentSlotViews();
            DeadZone.Systems.Raid.RaidLoadoutTransferService.ApplyLocalQuickSlotsToUi();
            RefreshQuickSlotViews();
        }

        public void Close()
        {
            if (inventoryRoot == null)
                return;

            if (itemTooltipUI != null)
                itemTooltipUI.Hide();

            inventoryRoot.SetActive(false);
            GameplayInputBlocker.SetBlocked(GameplayInputBlockReason.Inventory, false);
            CursorStateController.PopUiOwner(this);
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public bool TryEquipWeaponSlot(WeaponSlot weaponSlot, WeaponDataSO weaponData)
        {
            if (weaponData == null)
                return TryClearWeaponSlot(weaponSlot);

            FixedString64Bytes ammoId = defaultLoadedAmmo != null ? defaultLoadedAmmo.itemID : "";
            int ammoCount = fillMagazineOnEquip ? Mathf.Max(0, weaponData.magSize) : 0;

            if (ResolveEquipmentSlotsBridge() && equipmentSlotsBridge.CanEquipItem(weaponData.itemID))
            {
                if (debugWeaponEquipEvents)
                    Debug.Log($"[InventoryUI] 브릿지 경로로 무기 장착 요청: slot={weaponSlot}, itemID={weaponData.itemID}, ammo={ammoCount}", this);

                equipmentSlotsBridge.EquipItemServerRpc(
                    new FixedString64Bytes(weaponData.itemID),
                    weaponSlot,
                    ammoId,
                    (ushort)Mathf.Clamp(ammoCount, 0, ushort.MaxValue));

                return true;
            }

            if (ResolveEquipmentSlots() && equipmentSlots.IsServer)
            {
                WeaponState state = new()
                {
                    loadedAmmoId = ammoId,
                    currentAmmo = ammoCount
                };

                if (debugWeaponEquipEvents)
                    Debug.Log($"[InventoryUI] 서버 직접 경로로 무기 장착: slot={weaponSlot}, itemID={weaponData.itemID}, ammo={ammoCount}, equipment={equipmentSlots.name}", this);

                equipmentSlots.UpdateSlot(weaponSlot, weaponData.itemID, state);
                return true;
            }

            if (ResolveEquipmentSlots() && equipmentSlots.IsSpawned)
            {
                if (debugWeaponEquipEvents)
                    Debug.Log($"[InventoryUI] EquipmentSlots ServerRpc 경로로 무기 장착 요청: slot={weaponSlot}, itemID={weaponData.itemID}, ammo={ammoCount}, equipment={equipmentSlots.name}", this);

                equipmentSlots.EquipWeaponSlotServerRpc(
                    new FixedString64Bytes(weaponData.itemID),
                    weaponSlot,
                    ammoId,
                    (ushort)Mathf.Clamp(ammoCount, 0, ushort.MaxValue));

                return true;
            }

            WarnMissingEquipmentBridgeOnce();
            if (debugWeaponEquipEvents)
                Debug.LogWarning($"[InventoryUI] 무기 장착 실패: EquipmentSlots를 찾지 못했거나 아직 Spawn되지 않았습니다. slot={weaponSlot}, itemID={weaponData.itemID}", this);

            return false;
        }

        public bool TryClearWeaponSlot(WeaponSlot weaponSlot)
        {
            if (ResolveEquipmentSlots() && equipmentSlots.IsServer)
            {
                equipmentSlots.UpdateSlot(weaponSlot, string.Empty, default);
                return true;
            }

            if (ResolveEquipmentSlots() && equipmentSlots.IsSpawned)
            {
                equipmentSlots.ClearWeaponSlotServerRpc(weaponSlot);
                return true;
            }

            WarnUnsupportedClearOnce();
            return false;
        }

        public bool OwnsSlot(InventorySlotUI slot)
        {
            if (slot == null)
                return false;

            Transform slotTransform = slot.transform;
            if (slotTransform.IsChildOf(transform))
                return true;

            return inventoryRoot != null && slotTransform.IsChildOf(inventoryRoot.transform);
        }

        public void SetBagLevel(int level)
        {
            bagLevel = Mathf.Clamp(level, 0, 4);
            RefreshGridInventorySlots();
        }

        public void BindGridInventory(GridInventory inventory)
        {
            if (boundGridInventory == inventory)
            {
                RefreshGridInventorySlots();
                return;
            }

            UnsubscribeGridInventory();
            boundGridInventory = inventory;
            SubscribeGridInventory();
            RefreshGridInventorySlots();

            if (debugGridInventoryView)
            {
                string inventoryName = boundGridInventory != null ? boundGridInventory.name : "null";
                Debug.Log($"[InventoryUI] GridInventory 바인딩: {inventoryName}", this);
            }
        }

        private void HandleBackpackChanged(BackpackChangedEvent evt)
        {
            if (!ResolveEquipmentSlots() || equipmentSlots.OwnerClientId != evt.clientId)
                return;

            SetBagLevel(GetBagLevelFromBackpackId(evt.newBackpackId.ToString()));
            RefreshEquipmentSlotViews();
        }

        [BoxGroup("디버그")]
        [Button("슬롯 잠금 상태 새로고침")]
        public void RefreshBagSlots()
        {
            if (bagSlots == null)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.", this);
                return;
            }

            int unlockedCount = GetCapacityByBagLevel(bagLevel);

            for (int i = 0; i < bagSlots.Count; i++)
            {
                if (bagSlots[i] == null)
                    continue;

                bool locked = i >= unlockedCount;
                bagSlots[i].SetLocked(locked);
            }
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 1개 배치")]
        private void FillRandomItem1()
        {
            FillRandomItems(1);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 5개 배치")]
        private void FillRandomItems5()
        {
            FillRandomItems(5);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 10개 배치")]
        private void FillRandomItems10()
        {
            FillRandomItems(10);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 15개 배치")]
        private void FillRandomItems15()
        {
            FillRandomItems(15);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("랜덤 아이템 20개 배치")]
        private void FillRandomItems20()
        {
            FillRandomItems(20);
        }

        [BoxGroup("ItemDataSO 테스트")]
        [Button("모든 슬롯 비우기")]
        public void ClearAllSlots()
        {
            if (bagSlots == null || bagSlots.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.", this);
                return;
            }

            foreach (InventorySlotUI slot in bagSlots)
            {
                if (slot == null)
                    continue;

                slot.ClearItem();
            }

            if (itemTooltipUI != null)
                itemTooltipUI.Hide();

            RefreshBagSlots();
        }

        public void FillRandomItems(int count)
        {
            if (bagSlots == null || bagSlots.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Bag Slots가 연결되지 않았습니다.", this);
                return;
            }

            ResolveTooltipUI();
            EnsureDropSlots();
            AssignTooltipToSlots();
            ClearAssignedSlots();

            if (testItemPool == null || testItemPool.Count == 0)
            {
                Debug.LogWarning("[InventoryUI] Test Item Pool이 비어 있습니다.", this);
                RefreshBagSlots();
                return;
            }

            int unlockedCount = Mathf.Min(GetCapacityByBagLevel(bagLevel), bagSlots.Count);
            int fillCount = Mathf.Clamp(count, 0, unlockedCount);

            for (int i = 0; i < fillCount; i++)
            {
                InventorySlotUI slot = bagSlots[i];
                if (slot == null)
                    continue;

                ItemDataSO itemData = GetRandomTestItem();
                if (itemData == null)
                    continue;

                slot.SetItem(itemData, GetRandomStackCount(itemData));
            }

            RefreshBagSlots();
        }

        [BoxGroup("디버그")]
        [Button("인벤토리 열기")]
        private void TestOpen()
        {
            Open();
        }

        [BoxGroup("디버그")]
        [Button("인벤토리 닫기")]
        private void TestClose()
        {
            Close();
        }

        [BoxGroup("디버그")]
        [Button("인벤토리 토글")]
        private void TestToggle()
        {
            Toggle();
        }

        [BoxGroup("디버그")]
        [Button("가방 기본")]
        private void TestBagLevel0()
        {
            SetBagLevel(0);
        }

        [BoxGroup("디버그")]
        [Button("가방 1레벨")]
        private void TestBagLevel1()
        {
            SetBagLevel(1);
        }

        [BoxGroup("디버그")]
        [Button("가방 2레벨")]
        private void TestBagLevel2()
        {
            SetBagLevel(2);
        }

        [BoxGroup("디버그")]
        [Button("가방 3레벨")]
        private void TestBagLevel3()
        {
            SetBagLevel(3);
        }

        [BoxGroup("디버그")]
        [Button("가방 4레벨")]
        private void TestBagLevel4()
        {
            SetBagLevel(4);
        }

        private void BindOwnerGridInventoryIfNeeded()
        {
            if (boundGridInventory != null && boundGridInventory.IsSpawned && boundGridInventory.IsOwner)
                return;

            GridInventory[] candidates = FindObjectsByType<GridInventory>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (GridInventory candidate in candidates)
            {
                if (candidate != null && candidate.IsSpawned && candidate.IsOwner)
                {
                    BindGridInventory(candidate);
                    return;
                }
            }

            foreach (GridInventory candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                {
                    BindGridInventory(candidate);
                    return;
                }
            }

            if (debugGridInventoryView)
                Debug.LogWarning("[InventoryUI] Owner GridInventory를 찾지 못했습니다.", this);
        }

        private void SubscribeGridInventory()
        {
            if (gridInventorySubscribed || boundGridInventory == null || boundGridInventory.ServerGrid == null)
                return;

            boundGridInventory.ServerGrid.OnListChanged += HandleGridInventoryChanged;
            if (boundGridInventory.QuickSlots != null)
            {
                boundGridInventory.QuickSlots.OnListChanged += HandleQuickSlotsChanged;
                quickSlotsSubscribed = true;
            }

            gridInventorySubscribed = true;
        }

        private void UnsubscribeGridInventory()
        {
            if (!gridInventorySubscribed || boundGridInventory == null || boundGridInventory.ServerGrid == null)
            {
                gridInventorySubscribed = false;
                quickSlotsSubscribed = false;
                return;
            }

            boundGridInventory.ServerGrid.OnListChanged -= HandleGridInventoryChanged;
            if (quickSlotsSubscribed && boundGridInventory.QuickSlots != null)
                boundGridInventory.QuickSlots.OnListChanged -= HandleQuickSlotsChanged;

            gridInventorySubscribed = false;
            quickSlotsSubscribed = false;
        }

        private void HandleGridInventoryChanged(Unity.Netcode.NetworkListEvent<ItemSlotData> changeEvent)
        {
            RefreshGridInventorySlots();
        }

        private void HandleQuickSlotsChanged(Unity.Netcode.NetworkListEvent<QuickSlotData> changeEvent)
        {
            RefreshQuickSlotViews();
        }

        private void HandleEquipmentSlotIdChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
        {
            RefreshEquipmentSlotViews();
        }

        private void RefreshGridInventorySlots()
        {
            if (bagSlots == null || bagSlots.Count == 0)
                return;

            ClearAssignedSlots();

            if (boundGridInventory == null || boundGridInventory.ServerGrid == null)
            {
                RefreshBagSlots();
                return;
            }

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();

            for (int i = 0; i < boundGridInventory.ServerGrid.Count; i++)
            {
                ItemSlotData slotData = boundGridInventory.ServerGrid[i];
                string itemId = slotData.itemId.ToString();

                if (string.IsNullOrWhiteSpace(itemId) || slotData.stackCount <= 0)
                    continue;

                ItemDataSO itemData = itemDatabase?.GetById(itemId);
                if (itemData == null)
                {
                    if (debugGridInventoryView)
                        Debug.LogWarning($"[InventoryUI] ItemDatabase 조회 실패: itemId={itemId}", this);

                    continue;
                }

                int slotIndex = slotData.gridY * GridInventory.BASE_WIDTH + slotData.gridX;
                if (slotIndex < 0 || slotIndex >= bagSlots.Count || bagSlots[slotIndex] == null)
                {
                    if (debugGridInventoryView)
                    {
                        Debug.LogWarning(
                            $"[InventoryUI] 표시 슬롯 범위 오류: itemId={itemId}, grid=({slotData.gridX},{slotData.gridY}), slotIndex={slotIndex}",
                            this);
                    }

                    continue;
                }

                bagSlots[slotIndex].SetItem(itemData, slotData.stackCount, itemId);
            }

            RefreshBagSlots();

            if (debugGridInventoryView)
                Debug.Log($"[InventoryUI] GridInventory 표시 갱신 완료. count={boundGridInventory.ServerGrid.Count}", this);
        }

        private void ClearAssignedSlots()
        {
            foreach (InventorySlotUI slot in bagSlots)
            {
                if (slot == null)
                    continue;

                slot.ClearItem();
            }
        }

        private void RefreshEquipmentSlotViews()
        {
            List<InventorySlotUI> slots = GetEquipmentDisplaySlots();
            ClearEquipmentSlots(slots);

            if (slots.Count == 0 || !ResolveEquipmentSlots())
                return;

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                return;

            string backpackId = equipmentSlots.BackpackSlotId.Value.ToString();
            bagLevel = GetBagLevelFromBackpackId(backpackId);

            ApplyEquipmentSlot(slots, "EquipmentHead", equipmentSlots.HeadSlotId.Value.ToString());
            ApplyEquipmentSlot(slots, "EquipmentArmor", equipmentSlots.TorsoSlotId.Value.ToString());
            ApplyEquipmentSlot(slots, "EquipmentBackpack", backpackId);
            ApplyEquipmentSlot(slots, "EquipmentPrimaryWeapon", equipmentSlots.Primary1Id.Value.ToString());
            ApplyEquipmentSlot(slots, "Primary2", equipmentSlots.Primary2Id.Value.ToString());
            ApplyEquipmentSlot(slots, "EquipmentSecondaryWeapon", equipmentSlots.SecondaryId.Value.ToString());
            ApplyEquipmentSlot(slots, "EquipmentMeleeWeapon", equipmentSlots.MeleeId.Value.ToString());

            RefreshBagSlots();
        }

        private void ApplyEquipmentSlot(List<InventorySlotUI> slots, string slotId, string itemId)
        {
            if (slots == null || string.IsNullOrWhiteSpace(slotId) || string.IsNullOrWhiteSpace(itemId))
                return;

            InventorySlotUI slot = FindEquipmentSlot(slots, slotId);
            if (slot == null)
            {
                if (debugWeaponEquipEvents)
                    Debug.LogWarning($"[InventoryUI] Equipment UI slot not found. slotId={slotId}, itemId={itemId}", this);

                return;
            }

            ItemDataSO itemData = itemDatabase?.GetById(itemId);
            if (itemData == null)
            {
                if (debugWeaponEquipEvents)
                    Debug.LogWarning($"[InventoryUI] Equipment item data not found. itemId={itemId}", this);

                return;
            }

            slot.SetItem(itemData, 1);
        }

        private List<InventorySlotUI> GetEquipmentDisplaySlots()
        {
            List<InventorySlotUI> slots = new();
            HashSet<InventorySlotUI> visited = new();

            foreach (InventorySlotUI slot in GetAllKnownSlots())
            {
                if (slot == null || !visited.Add(slot))
                    continue;

                string slotId = slot.GetEquipmentSaveSlotId();
                if (!string.IsNullOrWhiteSpace(slotId))
                    slots.Add(slot);
            }

            return slots;
        }

        private static void ClearEquipmentSlots(List<InventorySlotUI> slots)
        {
            if (slots == null)
                return;

            for (int i = 0; i < slots.Count; i++)
                slots[i]?.ClearItem();
        }

        private static InventorySlotUI FindEquipmentSlot(List<InventorySlotUI> slots, string slotId)
        {
            string normalizedTarget = NormalizeEquipmentSlotId(slotId);

            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot == null)
                    continue;

                string normalizedSlot = NormalizeEquipmentSlotId(slot.GetEquipmentSaveSlotId());
                if (normalizedSlot == normalizedTarget)
                    return slot;
            }

            return null;
        }

        private static string NormalizeEquipmentSlotId(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                return string.Empty;

            return slotId.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant() switch
            {
                "head" => "equipmenthead",
                "torso" => "equipmentarmor",
                "backpack" => "equipmentbackpack",
                "primary1" => "equipmentprimaryweapon",
                "secondary" => "equipmentsecondaryweapon",
                "melee" => "equipmentmeleeweapon",
                var normalized => normalized
            };
        }

        private void AssignTooltipToSlots()
        {
            foreach (InventorySlotUI slot in GetAllKnownSlots())
            {
                if (slot == null)
                    continue;

                slot.PrepareDropSlot(itemTooltipUI, inventoryUI: this);
            }
        }

        private void EnsureDropSlots()
        {
            if (!autoCreateDropSlots || inventoryRoot == null)
                return;

            Transform equipmentPanel = FindChildByName(inventoryRoot.transform, "EquipmentPanel");
            if (equipmentPanel != null)
                EnsureDropSlotsUnder(equipmentPanel, directChildrenOnly: true);

            Transform quickSlotPanel = FindChildByName(inventoryRoot.transform, "QuickSlotPanel");
            if (quickSlotPanel != null)
                EnsureQuickSlotsUnder(quickSlotPanel);
        }

        private void EnsureDropSlotsUnder(Transform panel, bool directChildrenOnly)
        {
            if (panel == null)
                return;

            int index = 0;
            foreach (Transform child in panel)
            {
                if (child == null || child.GetComponent<RectTransform>() == null)
                    continue;

                if (!IsDropSlotCandidate(child, panel))
                    continue;

                InventorySlotUI slot = child.GetComponent<InventorySlotUI>();
                if (slot == null)
                    slot = child.gameObject.AddComponent<InventorySlotUI>();

                slot.PrepareDropSlot(itemTooltipUI, index, this);
                index++;
            }

            if (directChildrenOnly)
                return;

            foreach (InventorySlotUI slot in panel.GetComponentsInChildren<InventorySlotUI>(true))
                slot.PrepareDropSlot(itemTooltipUI, inventoryUI: this);
        }

        private void EnsureQuickSlotsUnder(Transform panel)
        {
            if (panel == null)
                return;

            int index = 0;
            foreach (Transform child in panel)
            {
                if (child == null || child.GetComponent<RectTransform>() == null)
                    continue;

                if (!IsDropSlotCandidate(child, panel))
                    continue;

                InventorySlotUI slot = child.GetComponent<InventorySlotUI>();
                if (slot == null)
                    slot = child.gameObject.AddComponent<InventorySlotUI>();

                slot.PrepareDropSlotAsKind(itemTooltipUI, InventorySlotKind.QuickSlot, index, this);
                index++;
            }

            foreach (InventorySlotUI slot in panel.GetComponentsInChildren<InventorySlotUI>(true))
            {
                if (slot != null)
                    slot.PrepareDropSlotAsKind(itemTooltipUI, InventorySlotKind.QuickSlot, inventoryUI: this);
            }
        }

        private static bool IsDropSlotCandidate(Transform child, Transform panel)
        {
            string childName = child.name.ToLowerInvariant();
            string panelName = panel.name.ToLowerInvariant();

            if (childName.StartsWith("text_") || child.GetComponent<TMPro.TMP_Text>() != null)
                return false;

            if (childName.StartsWith("slot_"))
                return true;

            return panelName.Contains("quickslotpanel") && child.GetComponent<UnityEngine.UI.Image>() != null;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
                return null;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }

        private IEnumerable<InventorySlotUI> GetAllKnownSlots()
        {
            HashSet<InventorySlotUI> result = new();

            if (bagSlots != null)
            {
                foreach (InventorySlotUI slot in bagSlots)
                {
                    if (slot != null)
                        result.Add(slot);
                }
            }

            if (inventoryRoot != null)
            {
                foreach (InventorySlotUI slot in inventoryRoot.GetComponentsInChildren<InventorySlotUI>(true))
                {
                    if (slot != null)
                        result.Add(slot);
                }
            }

            foreach (InventorySlotUI slot in GetComponentsInChildren<InventorySlotUI>(true))
            {
                if (slot != null)
                    result.Add(slot);
            }

            return result;
        }

        private void ApplyRaidQuickSlotLoadoutIfAvailable()
        {
            if (raidQuickSlotLoadoutApplied)
                return;

            if (!RaidLoadoutTransferService.TryGetQuickSlotItemsForLocalClient(out IReadOnlyList<QuickSlotSaveData> quickSlotItems))
                return;

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                return;

            List<InventorySlotUI> quickSlots = GetQuickSlotDisplaySlots();
            if (quickSlots.Count == 0)
                return;

            for (int i = 0; i < quickSlots.Count; i++)
                quickSlots[i]?.ClearItem();

            for (int i = 0; i < quickSlotItems.Count; i++)
            {
                QuickSlotSaveData savedItem = quickSlotItems[i];
                if (savedItem == null || string.IsNullOrWhiteSpace(savedItem.itemId))
                    continue;

                ItemDataSO itemData = itemDatabase.GetById(savedItem.itemId);
                if (itemData == null)
                    continue;

                SetQuickSlotsByIndex(quickSlots, Mathf.Max(0, savedItem.slotIndex), itemData, Mathf.Max(1, savedItem.stackCount), savedItem.itemId);
            }

            RefreshQuickSlotViews();
        }

        private void RefreshQuickSlotViews()
        {
            if (boundGridInventory == null && autoBindOwnerGridInventory)
                BindOwnerGridInventoryIfNeeded();

            if (boundGridInventory == null || boundGridInventory.QuickSlots == null)
                return;

            List<InventorySlotUI> quickSlots = GetQuickSlotDisplaySlots();
            if (quickSlots.Count == 0)
                return;

            for (int i = 0; i < quickSlots.Count; i++)
                quickSlots[i]?.ClearItem();

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();
            if (itemDatabase == null)
                return;

            for (int i = 0; i < boundGridInventory.QuickSlots.Count; i++)
            {
                QuickSlotData slotData = boundGridInventory.QuickSlots[i];
                if (slotData.IsEmpty)
                    continue;

                ItemDataSO itemData = itemDatabase.GetById(slotData.itemId.ToString());
                if (itemData == null)
                    continue;

                SetQuickSlotsByIndex(quickSlots, slotData.slotIndex, itemData, Mathf.Max(1, slotData.stackCount), slotData.itemId.ToString());
            }

            if (boundGridInventory.QuickSlots != null)
                return;

            for (int i = 0; i < quickSlots.Count; i++)
            {
                InventorySlotUI slot = quickSlots[i];
                if (slot == null || !slot.HasItem || slot.CurrentItemData == null)
                    continue;

                int availableCount = boundGridInventory.GetItemCount(slot.CurrentItemData.itemID);
                if (availableCount <= 0)
                {
                    // 퀵슬롯은 실제 아이템이 아니라 바로가기이므로, 원본 아이템이 없으면 즉시 비운다.
                    slot.ClearItem();
                    continue;
                }

                if (slot.CurrentStackCount != availableCount)
                    slot.SetItem(slot.CurrentItemData, availableCount);
            }
        }

        private List<InventorySlotUI> GetQuickSlotDisplaySlots()
        {
            List<InventorySlotUI> slots = new();
            HashSet<InventorySlotUI> visited = new();

            foreach (InventorySlotUI slot in GetAllKnownSlots())
            {
                if (slot == null || !visited.Add(slot))
                    continue;

                slot.PrepareForSaveSnapshot();
                if (slot.SlotKind == InventorySlotKind.QuickSlot)
                    slots.Add(slot);
            }

            return slots;
        }

        private static void SetQuickSlotsByIndex(List<InventorySlotUI> slots, int slotIndex, ItemDataSO itemData, int stackCount, string sourceItemId)
        {
            if (slots == null || itemData == null)
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotUI slot = slots[i];
                if (slot != null && slot.SlotIndex == slotIndex)
                    slot.SetItem(itemData, stackCount, sourceItemId);
            }
        }

        private void ResolveTooltipUI()
        {
            if (itemTooltipUI == null && inventoryRoot != null)
                itemTooltipUI = inventoryRoot.GetComponentInChildren<ItemTooltipUI>(true);

            if (itemTooltipUI == null)
                itemTooltipUI = GetComponentInChildren<ItemTooltipUI>(true);

            if (itemTooltipUI == null)
                Debug.LogWarning("[InventoryUI] ItemTooltipUI를 찾지 못했습니다. 씬의 Tooltip 오브젝트를 InventoryUI.itemTooltipUI에 연결하세요.", this);
        }

        private bool ResolveEquipmentSlots()
        {
            if (equipmentSlots != null)
                return true;

            EquipmentSlots[] candidates = FindObjectsByType<EquipmentSlots>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null && candidate.IsSpawned && candidate.IsOwner)
                {
                    equipmentSlots = candidate;
                    return true;
                }
            }

            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                {
                    equipmentSlots = candidate;
                    return true;
                }
            }

            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null && candidate.IsSpawned)
                {
                    equipmentSlots = candidate;
                    return true;
                }
            }

            return false;
        }

        private void SubscribeEquipmentSlots()
        {
            if (!ResolveEquipmentSlots())
                return;

            if (equipmentSlotsSubscribed && subscribedEquipmentSlots == equipmentSlots)
                return;

            UnsubscribeEquipmentSlots();

            subscribedEquipmentSlots = equipmentSlots;
            subscribedEquipmentSlots.HeadSlotId.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.TorsoSlotId.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.BackpackSlotId.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.Primary1Id.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.Primary2Id.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.SecondaryId.OnValueChanged += HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.MeleeId.OnValueChanged += HandleEquipmentSlotIdChanged;
            equipmentSlotsSubscribed = true;
        }

        private void UnsubscribeEquipmentSlots()
        {
            if (!equipmentSlotsSubscribed || subscribedEquipmentSlots == null)
            {
                subscribedEquipmentSlots = null;
                equipmentSlotsSubscribed = false;
                return;
            }

            subscribedEquipmentSlots.HeadSlotId.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.TorsoSlotId.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.BackpackSlotId.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.Primary1Id.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.Primary2Id.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.SecondaryId.OnValueChanged -= HandleEquipmentSlotIdChanged;
            subscribedEquipmentSlots.MeleeId.OnValueChanged -= HandleEquipmentSlotIdChanged;

            subscribedEquipmentSlots = null;
            equipmentSlotsSubscribed = false;
        }

        private bool ResolveEquipmentSlotsBridge()
        {
            if (equipmentSlotsBridge != null)
                return true;

            EquipmentSlotsBridge[] candidates = FindObjectsByType<EquipmentSlotsBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                {
                    equipmentSlotsBridge = candidate;
                    return true;
                }
            }

            foreach (EquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null)
                {
                    equipmentSlotsBridge = candidate;
                    return true;
                }
            }

            return false;
        }

        private void WarnMissingEquipmentBridgeOnce()
        {
            if (warnedMissingEquipmentBridge)
                return;

            warnedMissingEquipmentBridge = true;
            Debug.LogWarning("[InventoryUI] EquipmentSlotsBridge를 찾지 못했습니다. 클라이언트 장비 장착 동기화를 위해 PlayerPrefab에 브릿지를 붙이고 InventoryUI에 연결하세요.", this);
        }

        private void WarnUnsupportedClearOnce()
        {
            if (warnedUnsupportedClear)
                return;

            warnedUnsupportedClear = true;
            Debug.LogWarning("[InventoryUI] 현재 기존 브릿지에는 무기 슬롯 해제 ServerRpc가 없어 클라이언트에서 장비 해제를 서버에 반영할 수 없습니다. 필요하면 UI 외 스크립트 수정 승인이 필요합니다.", this);
        }

        private ItemDataSO GetRandomTestItem()
        {
            return testItemPool[Random.Range(0, testItemPool.Count)];
        }

        private int GetRandomStackCount(ItemDataSO itemData)
        {
            if (itemData == null || itemData.maxStackSize <= 1)
                return 1;

            int maxStackCount = Mathf.Min(itemData.maxStackSize, 99);
            return Random.Range(1, maxStackCount + 1);
        }

        private int GetCapacityByBagLevel(int level)
        {
            return Mathf.Clamp(level, 0, 4) switch
            {
                0 => 20,
                1 => 25,
                2 => 30,
                3 => 35,
                4 => 40,
                _ => 20
            };
        }

        private int GetBagLevelFromBackpackId(string backpackId)
        {
            if (string.IsNullOrWhiteSpace(backpackId))
                return 0;

            itemDatabase ??= ServiceLocator.Get<IItemDatabase>();
            BackpackDataSO backpackData = itemDatabase?.GetById<BackpackDataSO>(backpackId);
            return backpackData != null ? Mathf.Clamp(backpackData.backpackLevel, 0, 4) : 0;
        }
    }
}

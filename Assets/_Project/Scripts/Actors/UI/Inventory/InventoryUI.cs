using System.Collections.Generic;
using DeadZone.Actors;
using DeadZone.Core;
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

        [BoxGroup("장비 연동")]
        [Tooltip("플레이어에 붙은 KHWEquipmentSlotsBridge입니다. 비워두면 Owner 플레이어에서 자동 탐색합니다.")]
        [SerializeField] private KHWEquipmentSlotsBridge equipmentSlotsBridge;

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

        [BoxGroup("ItemDataSO 테스트")]
        [Tooltip("랜덤 아이템 배치 테스트에 사용할 ItemDataSO 목록입니다.")]
        [SerializeField] private List<ItemDataSO> testItemPool = new();

        public bool IsOpen => inventoryRoot != null && inventoryRoot.activeSelf;

        private void Awake()
        {
            ActiveInstance = this;
            ResolveTooltipUI();
            EnsureDropSlots();
            InitializeSlots();
            RefreshBagSlots();

            Close();
        }

        private void OnEnable()
        {
            ActiveInstance = this;
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;
        }

        private void OnValidate()
        {
            if (bagSlots == null)
                return;

            if (!Application.isPlaying)
                return;

            ResolveTooltipUI();
            EnsureDropSlots();
            AssignTooltipToSlots();
            RefreshBagSlots();
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
            CursorStateController.PushUiOwner(this);
            ResolveTooltipUI();
            EnsureDropSlots();
            AssignTooltipToSlots();
            RefreshBagSlots();
        }

        public void Close()
        {
            if (inventoryRoot == null)
                return;

            if (itemTooltipUI != null)
                itemTooltipUI.Hide();

            inventoryRoot.SetActive(false);
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
            RefreshBagSlots();
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

        private void ClearAssignedSlots()
        {
            foreach (InventorySlotUI slot in bagSlots)
            {
                if (slot == null)
                    continue;

                slot.ClearItem();
            }
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
                EnsureDropSlotsUnder(quickSlotPanel, directChildrenOnly: true);
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
                if (candidate != null && candidate.IsOwner)
                {
                    equipmentSlots = candidate;
                    return true;
                }
            }

            foreach (EquipmentSlots candidate in candidates)
            {
                if (candidate != null)
                {
                    equipmentSlots = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool ResolveEquipmentSlotsBridge()
        {
            if (equipmentSlotsBridge != null)
                return true;

            KHWEquipmentSlotsBridge[] candidates = FindObjectsByType<KHWEquipmentSlotsBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (KHWEquipmentSlotsBridge candidate in candidates)
            {
                if (candidate != null && candidate.IsOwner)
                {
                    equipmentSlotsBridge = candidate;
                    return true;
                }
            }

            foreach (KHWEquipmentSlotsBridge candidate in candidates)
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
            Debug.LogWarning("[InventoryUI] KHWEquipmentSlotsBridge를 찾지 못했습니다. 클라이언트 장비 장착 동기화를 위해 PlayerPrefab에 브릿지를 붙이고 InventoryUI에 연결하세요.", this);
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
    }
}

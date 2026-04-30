using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Core
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 여러 종류의 ItemDataSO를 한 곳에서 ID로 찾기 위한 ScriptableObject 데이터베이스입니다.
    /// 이 파일은 씬 오브젝트에 Add Component 하는 컴포넌트가 아니라,
    /// Project 창에서 Create 메뉴로 생성한 뒤 LootContainer / LootInteractable / Equipment Bridge에 연결하는 SO 에셋입니다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/KHW/Script Object Pool", fileName = "KHWScriptObjectPoolSO")]
    public class KHWScriptObjectPoolSO : ScriptableObject
    {
        [Header("총기 아이템 목록")]
        [Tooltip("WeaponDataSO를 넣습니다. 예: M4, MP5, 권총, 근접무기 등")]
        [SerializeField] private WeaponDataSO[] weaponItems;

        [Header("탄약 아이템 목록")]
        [Tooltip("AmmoDataSO를 넣습니다. 예: 9mm, 5.56mm, 샷건 탄 등")]
        [SerializeField] private AmmoDataSO[] ammoItems;

        [Header("헬멧 아이템 목록")]
        [Tooltip("HelmetDataSO를 넣습니다. 예: 방탄헬멧, 경량헬멧 등")]
        [SerializeField] private HelmetDataSO[] helmetItems;

        [Header("방어구 아이템 목록")]
        [Tooltip("ArmorDataSO를 넣습니다. 예: 방탄조끼, 플레이트 캐리어 등")]
        [SerializeField] private ArmorDataSO[] armorItems;

        [Header("일반 아이템 목록")]
        [Tooltip("기본 ItemDataSO를 넣습니다. 예: 음식, 치료템, 재료, 귀중품, 퀘스트 아이템 등")]
        [SerializeField] private ItemDataSO[] normalItems;

        [Header("아이템 공통 규칙")]
        [Tooltip("체크하면 검증할 때 1칸 아이템이 아닌 데이터가 있는지 경고를 출력합니다. 실제 SO 값은 수정하지 않습니다.")]
        [SerializeField] private bool warnIfNotOneCell = true;

        private Dictionary<string, ItemDataSO> itemMap;

        public IReadOnlyList<WeaponDataSO> WeaponItems
        {
            get { return weaponItems; }
        }

        public IReadOnlyList<AmmoDataSO> AmmoItems
        {
            get { return ammoItems; }
        }

        public IReadOnlyList<HelmetDataSO> HelmetItems
        {
            get { return helmetItems; }
        }

        public IReadOnlyList<ArmorDataSO> ArmorItems
        {
            get { return armorItems; }
        }

        public IReadOnlyList<ItemDataSO> NormalItems
        {
            get { return normalItems; }
        }

        private void OnEnable()
        {
            BuildMap();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            BuildMap();
            ValidateOneCellItems();
        }
#endif

        /// <summary>
        /// [KHW 추가 기능]
        /// 각 분류 배열에 들어간 SO를 itemID 기준 Dictionary로 캐싱합니다.
        /// 코드 역할: 매번 배열 전체를 찾지 않고 itemID로 빠르게 ItemDataSO를 찾습니다.
        /// </summary>
        private void BuildMap()
        {
            itemMap = new Dictionary<string, ItemDataSO>();
            RegisterArray(weaponItems);
            RegisterArray(ammoItems);
            RegisterArray(helmetItems);
            RegisterArray(armorItems);
            RegisterArray(normalItems);
        }

        private void RegisterArray(ItemDataSO[] items)
        {
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
            {
                ItemDataSO item = items[i];
                if (item == null) continue;

                if (string.IsNullOrEmpty(item.itemID))
                {
                    Debug.LogWarning("[KHWScriptObjectPoolSO] itemID가 비어 있습니다: " + item.name, item);
                    continue;
                }

                if (itemMap.ContainsKey(item.itemID))
                {
                    Debug.LogError("[KHWScriptObjectPoolSO] 중복 itemID 발견: " + item.itemID, item);
                    continue;
                }

                itemMap.Add(item.itemID, item);
            }
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// itemID로 아이템 SO를 찾습니다.
        /// 코드 역할: LootInteractable, EquipmentSlots 연동 스크립트가 문자열 ID만 가지고 실제 SO를 찾게 합니다.
        /// </summary>
        public ItemDataSO Lookup(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (itemMap == null) BuildMap();

            ItemDataSO item;
            if (itemMap.TryGetValue(itemId, out item))
            {
                return item;
            }

            return null;
        }

        public WeaponDataSO LookupWeapon(string itemId)
        {
            return Lookup(itemId) as WeaponDataSO;
        }

        public AmmoDataSO LookupAmmo(string itemId)
        {
            return Lookup(itemId) as AmmoDataSO;
        }

        public HelmetDataSO LookupHelmet(string itemId)
        {
            return Lookup(itemId) as HelmetDataSO;
        }

        public ArmorDataSO LookupArmor(string itemId)
        {
            return Lookup(itemId) as ArmorDataSO;
        }

        public bool IsWeapon(string itemId)
        {
            return LookupWeapon(itemId) != null;
        }

        public bool IsAmmo(string itemId)
        {
            return LookupAmmo(itemId) != null;
        }

        public bool IsHelmet(string itemId)
        {
            return LookupHelmet(itemId) != null;
        }

        public bool IsArmor(string itemId)
        {
            return LookupArmor(itemId) != null;
        }

        private void ValidateOneCellItems()
        {
            if (!warnIfNotOneCell) return;

            ValidateOneCellArray(weaponItems);
            ValidateOneCellArray(ammoItems);
            ValidateOneCellArray(helmetItems);
            ValidateOneCellArray(armorItems);
            ValidateOneCellArray(normalItems);
        }

        private void ValidateOneCellArray(ItemDataSO[] items)
        {
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
            {
                ItemDataSO item = items[i];
                if (item == null) continue;

                if (item.gridSize != Vector2Int.one)
                {
                    Debug.LogWarning("[KHWScriptObjectPoolSO] 현재 기획은 모든 아이템 1칸입니다. gridSize를 (1,1)로 맞춰주세요: " + item.itemID, item);
                }
            }
        }
    }
}

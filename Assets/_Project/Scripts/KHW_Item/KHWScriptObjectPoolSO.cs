using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.KHWItem
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// ItemDataSO / WeaponDataSO / AmmoDataSO / HelmetDataSO / ArmorDataSO를 분류해서 보관하는 ScriptableObject 데이터베이스입니다.
    /// 이 파일은 GameObject에 붙이는 컴포넌트가 아니라 Project 창에서 생성하는 SO 에셋입니다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/KHW/Script Object Pool", fileName = "KHWScriptObjectPoolSO")]
    public class KHWScriptObjectPoolSO : ScriptableObject
    {
        [Header("일반 아이템 목록")]
        [Tooltip("물, 음식, 치료템, 재료, 열쇠 같은 기본 ItemDataSO를 등록합니다.")]
        [SerializeField] private ItemDataSO[] normalItems;

        [Header("무기 목록")]
        [Tooltip("AK, 권총, 근접무기 같은 WeaponDataSO를 등록합니다.")]
        [SerializeField] private WeaponDataSO[] weaponItems;

        [Header("탄약 목록")]
        [Tooltip("5.56mm, 9mm 같은 AmmoDataSO를 등록합니다.")]
        [SerializeField] private AmmoDataSO[] ammoItems;

        [Header("헬멧 목록")]
        [Tooltip("머리 방어구 HelmetDataSO를 등록합니다.")]
        [SerializeField] private HelmetDataSO[] helmetItems;

        [Header("방어구 목록")]
        [Tooltip("상의/몸통 방어구 ArmorDataSO를 등록합니다.")]
        [SerializeField] private ArmorDataSO[] armorItems;

        [Header("인벤토리 규칙")]
        [Tooltip("체크하면 이 데이터베이스를 통해 찾은 아이템은 모두 1x1 아이템처럼 취급합니다. 실제 SO 파일은 수정하지 않습니다.")]
        [SerializeField] private bool forceOneCellRule = true;

        private Dictionary<string, ItemDataSO> itemMap;

        public bool ForceOneCellRule
        {
            get
            {
                return forceOneCellRule;
            }
        }

        private void OnEnable()
        {
            BuildCache();
        }

        private void OnValidate()
        {
            BuildCache();
        }

        /// <summary>
        /// [KHW 추가 기능]
        /// 분류별 배열을 하나의 Dictionary로 합쳐 itemID 검색 속도를 빠르게 만듭니다.
        /// </summary>
        private void BuildCache()
        {
            itemMap = new Dictionary<string, ItemDataSO>();
            AddItemsToCache(normalItems, "일반 아이템");
            AddItemsToCache(weaponItems, "무기");
            AddItemsToCache(ammoItems, "탄약");
            AddItemsToCache(helmetItems, "헬멧");
            AddItemsToCache(armorItems, "방어구");
        }

        private void AddItemsToCache(ItemDataSO[] items, string groupName)
        {
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
            {
                ItemDataSO item = items[i];
                if (item == null) continue;

                if (string.IsNullOrEmpty(item.itemID))
                {
                    Debug.LogWarning("[KHWScriptObjectPoolSO] " + groupName + " itemID가 비어 있습니다: " + item.name, this);
                    continue;
                }

                if (itemMap.ContainsKey(item.itemID))
                {
                    Debug.LogWarning("[KHWScriptObjectPoolSO] 중복 itemID 발견: " + item.itemID + " / 나중에 등록된 아이템으로 덮어씁니다.", this);
                }

                itemMap[item.itemID] = item;
            }
        }

        public ItemDataSO Lookup(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (itemMap == null) BuildCache();

            ItemDataSO item;
            if (itemMap.TryGetValue(itemId, out item))
            {
                return item;
            }

            return null;
        }

        public bool TryLookup(string itemId, out ItemDataSO item)
        {
            item = Lookup(itemId);
            return item != null;
        }

        public bool IsWeapon(string itemId)
        {
            return Lookup(itemId) is WeaponDataSO;
        }

        public bool IsAmmo(string itemId)
        {
            return Lookup(itemId) is AmmoDataSO;
        }

        public bool IsHelmet(string itemId)
        {
            return Lookup(itemId) is HelmetDataSO;
        }

        public bool IsArmor(string itemId)
        {
            return Lookup(itemId) is ArmorDataSO;
        }
    }
}

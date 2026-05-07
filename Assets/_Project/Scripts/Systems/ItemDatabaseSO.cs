using System.Collections.Generic;
using UnityEngine;
using DeadZone.Core;

namespace DeadZone.Systems
{
    [CreateAssetMenu(
        fileName = "ItemDatabaseSO",
        menuName = "DeadZone/Item Database",
        order = 0)]
    public class ItemDatabaseSO : ScriptableObject
    {
        [Tooltip("Editor 스크립트가 자동 채움 — 수동 편집 금지")]
        [SerializeField] public List<ItemDataSO> allItems = new();

        private Dictionary<string, ItemDataSO> _cache;

        /// <summary>런타임 캐시 빌드. 중복 시 첫 등록 유지.</summary>
        public void BuildCache()
        {
            _cache = new Dictionary<string, ItemDataSO>(allItems.Count);
            foreach (var item in allItems)
            {
                if (item == null) continue;
                if (_cache.ContainsKey(item.itemID))
                {
                    Debug.LogError(
                        $"[ItemDB] 중복 itemID '{item.itemID}': " +
                        $"'{_cache[item.itemID].name}' vs '{item.name}'. " +
                        $"Editor에서 Rebuild 실행 필요.");
                    continue;
                }
                _cache.Add(item.itemID, item);
            }
            Debug.Log($"[ItemDB] {_cache.Count}개 아이템 등록 완료.");
        }

        public ItemDataSO GetByID(string itemID)
        {
            if (_cache == null) BuildCache();
            _cache.TryGetValue(itemID, out var result);
            return result;
        }

        public IReadOnlyDictionary<string, ItemDataSO> GetAll()
        {
            if (_cache == null) BuildCache();
            return _cache;
        }

        public int Count => _cache?.Count ?? allItems.Count;
    }
}
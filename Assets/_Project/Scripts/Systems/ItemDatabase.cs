using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems
{
    public class ItemDatabase : MonoBehaviour, IItemDatabase
    {
        [Tooltip("프로젝트의 모든 ItemDataSO 자산을 등록. " +
                 "Resources 폴더에 두면 Resources.LoadAll로 자동화 가능.")]
        [SerializeField] private ItemDataSO[] allItems;

        private Dictionary<string, ItemDataSO> cache;

        private void Awake()
        {
            BuildCache();
            ServiceLocator.Register<IItemDatabase>(this);
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister<IItemDatabase>();
        }

        private void BuildCache()
        {
            cache = new Dictionary<string, ItemDataSO>(allItems != null ? allItems.Length : 0);
            if (allItems == null) return;

            int duplicateCount = 0;
            int emptyIdCount = 0;

            foreach (var so in allItems)
            {
                if (so == null) continue;

                if (string.IsNullOrEmpty(so.itemID))
                {
                    emptyIdCount++;
                    Debug.LogWarning($"[ItemDatabase] '{so.name}' 의 itemID가 비어있음. 등록 스킵.", so);
                    continue;
                }

                if (cache.ContainsKey(so.itemID))
                {
                    duplicateCount++;
                    Debug.LogError($"[ItemDatabase] 중복 itemID '{so.itemID}' — '{so.name}' 와 " +
                                   $"'{cache[so.itemID].name}'. 등록 스킵.", so);
                    continue;
                }

                cache[so.itemID] = so;
            }

            Debug.Log($"[ItemDatabase] {cache.Count}개 아이템 등록 완료. " +
                      $"(중복 {duplicateCount}, 빈 ID {emptyIdCount})");
        }

        public ItemDataSO GetById(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            return cache.TryGetValue(itemId, out var so) ? so : null;
        }

        public T GetById<T>(string itemId) where T : ItemDataSO
        {
            return GetById(itemId) as T;
        }
    }
}
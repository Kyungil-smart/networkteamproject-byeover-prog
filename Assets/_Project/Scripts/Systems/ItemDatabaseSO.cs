using System.Collections.Generic;
using DeadZone.Core;
using UnityEngine;

namespace DeadZone.Systems
{
    [CreateAssetMenu(
        fileName = "ItemDatabaseSO",
        menuName = "DeadZone/Item Database",
        order = 0)]
    public class ItemDatabaseSO : ScriptableObject
    {
        [Tooltip("Editor scripts populate this list. Do not edit it manually unless rebuilding item data.")]
        [SerializeField] public List<ItemDataSO> allItems = new();

        private static readonly Dictionary<string, string> LegacyWeaponAliases = new()
        {
            { "DefensePistol", "Weapon_SelfDefense" },
            { "DragSniper", "Weapon_BoltSR" },
            { "PumpShotgun", "Weapon_PumpSG" }
        };

        private Dictionary<string, ItemDataSO> _cache;
        private readonly HashSet<string> loggedLegacyWeaponAliases = new();

        public void BuildCache()
        {
            _cache = new Dictionary<string, ItemDataSO>(allItems.Count);
            int duplicateCount = 0;
            int specializedReplacementCount = 0;
            List<string> duplicateSamples = new();

            foreach (ItemDataSO item in allItems)
            {
                if (item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(item.itemID))
                {
                    Debug.LogWarning($"[ItemDB] Skipped item with empty itemID: {item.name}", item);
                    continue;
                }

                if (_cache.TryGetValue(item.itemID, out ItemDataSO existing))
                {
                    duplicateCount++;
                    if (duplicateSamples.Count < 8)
                        duplicateSamples.Add($"{item.itemID}: {existing.name} vs {item.name}");

                    if (GetDataPriority(item) > GetDataPriority(existing))
                    {
                        _cache[item.itemID] = item;
                        specializedReplacementCount++;
                    }

                    continue;
                }

                _cache.Add(item.itemID, item);
            }

            if (duplicateCount > 0)
            {
                Debug.LogWarning(
                    $"[ItemDB] Duplicate itemIDs detected: {duplicateCount}. " +
                    $"Specialized replacements={specializedReplacementCount}. " +
                    $"Samples: {string.Join(", ", duplicateSamples)}",
                    this);
            }

            Debug.Log($"[ItemDB] Registered {_cache.Count} items.");
        }

        public ItemDataSO GetByID(string itemID)
        {
            if (string.IsNullOrWhiteSpace(itemID))
                return null;

            if (_cache == null)
                BuildCache();

            if (_cache.TryGetValue(itemID, out ItemDataSO result))
            {
                ItemDataSO specialized = ResolveSpecializedAlias(itemID, result);
                return specialized != null ? specialized : result;
            }

            return TryGetLegacyWeaponAlias(itemID, out ItemDataSO aliasResult)
                ? aliasResult
                : null;
        }

        public IReadOnlyDictionary<string, ItemDataSO> GetAll()
        {
            if (_cache == null)
                BuildCache();

            return _cache;
        }

        public int Count => _cache?.Count ?? allItems.Count;

        private ItemDataSO ResolveSpecializedAlias(string requestedItemId, ItemDataSO result)
        {
            if (result == null)
                return null;

            if (result is WeaponDataSO)
                return result;

            if (result.category == ItemCategory.Weapon &&
                TryGetLegacyWeaponAlias(requestedItemId, out ItemDataSO weaponAlias))
            {
                if (loggedLegacyWeaponAliases.Add(requestedItemId))
                {
                    Debug.LogWarning(
                        $"[ItemDB] Legacy weapon itemID '{requestedItemId}' resolved to '{weaponAlias.itemID}'. " +
                        "Replace old ItemDataSO references with WeaponDataSO assets.",
                        weaponAlias);
                }

                return weaponAlias;
            }

            return null;
        }

        private bool TryGetLegacyWeaponAlias(string itemID, out ItemDataSO result)
        {
            result = null;

            if (_cache == null || string.IsNullOrWhiteSpace(itemID))
                return false;

            if (LegacyWeaponAliases.TryGetValue(itemID, out string explicitAlias) &&
                TryGetWeaponData(explicitAlias, out result))
            {
                return true;
            }

            string prefixedAlias = itemID.StartsWith("Weapon_")
                ? itemID
                : $"Weapon_{itemID}";

            return TryGetWeaponData(prefixedAlias, out result);
        }

        private bool TryGetWeaponData(string itemID, out ItemDataSO result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(itemID))
                return false;

            if (!_cache.TryGetValue(itemID, out ItemDataSO candidate))
                return false;

            if (candidate is not WeaponDataSO)
                return false;

            result = candidate;
            return true;
        }

        private static int GetDataPriority(ItemDataSO item)
        {
            return item switch
            {
                WeaponDataSO => 100,
                ArmorDataSO => 90,
                HelmetDataSO => 90,
                BackpackDataSO => 90,
                AmmoDataSO => 80,
                _ => 0
            };
        }
    }
}

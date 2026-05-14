#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;

using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems.Save;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadZone.Systems.Housing
{
    [DisallowMultipleComponent]
    public sealed class HousingCraftMaterialDebugInventory : MonoBehaviour, IInventory
    {
        private static HousingCraftMaterialDebugInventory instance;

        private readonly Dictionary<string, InventoryEntry> items = new();
        private readonly Dictionary<string, MaterialGrant> testGrants = new();

        public static HousingCraftMaterialDebugInventory Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                HousingCraftMaterialDebugInventory existing =
                    FindFirstObjectByType<HousingCraftMaterialDebugInventory>(FindObjectsInactive.Include);

                if (existing != null)
                {
                    instance = existing;
                    return instance;
                }

                GameObject go = new GameObject(nameof(HousingCraftMaterialDebugInventory));
                DontDestroyOnLoad(go);
                instance = go.AddComponent<HousingCraftMaterialDebugInventory>();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool TryAddItem(ItemDataSO item, int amount = 1)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemID) || amount <= 0)
                return false;

            string itemId = item.itemID;

            if (!items.TryGetValue(itemId, out InventoryEntry entry))
            {
                entry = new InventoryEntry
                {
                    item = item,
                    count = 0
                };
            }

            entry.item = item;
            entry.count += amount;
            items[itemId] = entry;
            return true;
        }

        public bool HasItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            return GetItemCount(itemId) >= count;
        }

        public bool ConsumeItem(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return false;

            if (!items.TryGetValue(itemId, out InventoryEntry entry))
                return false;

            if (entry.count < count)
                return false;

            entry.count -= count;

            if (entry.count <= 0)
                items.Remove(itemId);
            else
                items[itemId] = entry;

            return true;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            return items.TryGetValue(itemId, out InventoryEntry entry)
                ? Mathf.Max(0, entry.count)
                : 0;
        }

        // F12 테스트용 재료를 메모리 디버그 인벤토리에 넣고, 보이는 보관함 UI가 있으면 같은 수량을 함께 넣습니다.
        public void AddAllCraftMaterialsForTest(IInventory mirrorInventory = null)
        {
            // F12를 여러 번 눌러도 테스트 재료가 계속 누적되지 않도록 이전 지급분부터 정리합니다.
            if (testGrants.Count > 0)
                RemoveTestCraftMaterials(mirrorInventory);

            Dictionary<string, MaterialGrant> requirements = CollectCraftMaterialRequirements();

            if (requirements.Count == 0)
            {
                Debug.LogWarning("[HousingCraftMaterialDebugInventory] 제작 테스트 재료를 찾지 못했습니다.", this);
                return;
            }

            testGrants.Clear();

            int addedKinds = 0;
            int addedTotal = 0;

            foreach (MaterialGrant grant in requirements.Values)
            {
                if (grant == null || grant.item == null || grant.grantedCount <= 0)
                    continue;

                grant.baselineCount = GetItemCount(grant.item.itemID);

                if (!TryAddItem(grant.item, grant.grantedCount))
                    continue;

                // 로비 보관함처럼 화면에 보이는 인벤토리가 있으면 디버그 인벤토리와 같은 재료를 반영합니다.
                if (TryMirrorAdd(mirrorInventory, grant.item, grant.grantedCount, out int mirrorBaseline))
                {
                    grant.mirrorBaselineCount = mirrorBaseline;
                    grant.mirrorGrantedCount = grant.grantedCount;
                }

                testGrants[grant.item.itemID] = grant;
                addedKinds++;
                addedTotal += grant.grantedCount;
            }

            Debug.Log(
                $"[HousingCraftMaterialDebugInventory] F12 오프라인 제작 재료 추가 완료\n" +
                $"아이템 종류: {addedKinds}\n" +
                $"총 수량: {addedTotal}",
                this);
        }

        // Shift+F12에서 테스트로 추가된 수량만 제거합니다. 기존에 가지고 있던 재료는 보존합니다.
        public void RemoveTestCraftMaterials(IInventory mirrorInventory = null)
        {
            if (testGrants.Count == 0)
            {
                Debug.Log("[HousingCraftMaterialDebugInventory] Shift+F12 제거할 테스트 제작 재료 기록이 없습니다.", this);
                return;
            }

            int removedKinds = 0;
            int removedTotal = 0;

            foreach (MaterialGrant grant in testGrants.Values)
            {
                if (grant == null || grant.item == null || string.IsNullOrWhiteSpace(grant.item.itemID))
                    continue;

                int currentCount = GetItemCount(grant.item.itemID);
                int removableCount = Mathf.Min(
                    Mathf.Max(0, grant.grantedCount),
                    Mathf.Max(0, currentCount - Mathf.Max(0, grant.baselineCount)));

                // 보이는 보관함에 같이 넣었던 테스트 수량도 같은 기준으로 제거합니다.
                TryMirrorRemove(mirrorInventory, grant);

                if (removableCount <= 0)
                    continue;

                if (!ConsumeItem(grant.item.itemID, removableCount))
                    continue;

                removedKinds++;
                removedTotal += removableCount;
            }

            testGrants.Clear();

            Debug.Log(
                $"[HousingCraftMaterialDebugInventory] Shift+F12 오프라인 제작 재료 제거 완료\n" +
                $"아이템 종류: {removedKinds}\n" +
                $"총 수량: {removedTotal}",
                this);
        }

        public static int ResolveFacilityLevel(FacilityType facilityType, int fallbackLevel)
        {
            LobbyFacilityState facilityState = FindFirstObjectByType<LobbyFacilityState>(FindObjectsInactive.Include);

            if (facilityState == null || facilityState.Facilities == null)
                return Mathf.Clamp(fallbackLevel, 1, 4);

            string expectedId = GetFacilitySaveId(facilityType);

            for (int i = 0; i < facilityState.Facilities.Count; i++)
            {
                FacilitySaveDTO facility = facilityState.Facilities[i];

                if (facility == null)
                    continue;

                if (!string.Equals(facility.facilityId, expectedId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                return Mathf.Clamp(facility.level, 1, 4);
            }

            return Mathf.Clamp(fallbackLevel, 1, 4);
        }

        // 디버그 인벤토리와 별개로, 현재 씬의 실제 보관함 UI에도 재료를 복사해 화면에서 확인할 수 있게 합니다.
        private static bool TryMirrorAdd(IInventory mirrorInventory, ItemDataSO item, int amount, out int baselineCount)
        {
            baselineCount = 0;

            if (mirrorInventory == null || mirrorInventory is HousingCraftMaterialDebugInventory)
                return false;

            if (item == null || string.IsNullOrWhiteSpace(item.itemID) || amount <= 0)
                return false;

            baselineCount = mirrorInventory.GetItemCount(item.itemID);

            if (mirrorInventory.TryAddItem(item, amount))
                return true;

            Debug.LogWarning($"[HousingCraftMaterialDebugInventory] Visible inventory mirror add failed. ItemId={item.itemID}, Amount={amount}");
            return false;
        }

        // F12 이전 보유량을 기준으로 테스트 지급분만 보이는 보관함에서 제거합니다.
        private static void TryMirrorRemove(IInventory mirrorInventory, MaterialGrant grant)
        {
            if (mirrorInventory == null || mirrorInventory is HousingCraftMaterialDebugInventory)
                return;

            if (grant == null || grant.item == null || string.IsNullOrWhiteSpace(grant.item.itemID))
                return;

            if (grant.mirrorGrantedCount <= 0)
                return;

            string itemId = grant.item.itemID;
            int currentCount = mirrorInventory.GetItemCount(itemId);
            int removableCount = Mathf.Min(
                Mathf.Max(0, grant.mirrorGrantedCount),
                Mathf.Max(0, currentCount - Mathf.Max(0, grant.mirrorBaselineCount)));

            if (removableCount > 0 && !mirrorInventory.ConsumeItem(itemId, removableCount))
                Debug.LogWarning($"[HousingCraftMaterialDebugInventory] Visible inventory mirror remove failed. ItemId={itemId}, Amount={removableCount}");
        }

        private static Dictionary<string, MaterialGrant> CollectCraftMaterialRequirements()
        {
            Dictionary<string, MaterialGrant> materials = new();

            WorkbenchRecipeCatalog[] workbenchCatalogs = FindObjectsByType<WorkbenchRecipeCatalog>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < workbenchCatalogs.Length; i++)
            {
                if (workbenchCatalogs[i] != null)
                    AddRecipeMaterials(materials, workbenchCatalogs[i].GetAllRecipes());
            }

            MedicalCraftingController[] medicalControllers = FindObjectsByType<MedicalCraftingController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < medicalControllers.Length; i++)
            {
                if (medicalControllers[i] != null)
                    AddRecipeMaterials(materials, medicalControllers[i].GetAllRecipes());
            }

#if UNITY_EDITOR
            if (materials.Count == 0)
                AddRecipeMaterialsFromAssetDatabase(materials);
#endif

            return materials;
        }

        private static void AddRecipeMaterials(Dictionary<string, MaterialGrant> materials, IReadOnlyList<RecipeSO> recipes)
        {
            if (materials == null || recipes == null)
                return;

            for (int recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
            {
                RecipeSO recipe = recipes[recipeIndex];

                if (recipe == null || recipe.ingredients == null)
                    continue;

                for (int ingredientIndex = 0; ingredientIndex < recipe.ingredients.Count; ingredientIndex++)
                {
                    ItemRequirement ingredient = recipe.ingredients[ingredientIndex];

                    if (ingredient.item == null || string.IsNullOrWhiteSpace(ingredient.item.itemID))
                        continue;

                    string itemId = ingredient.item.itemID;

                    if (!materials.TryGetValue(itemId, out MaterialGrant grant))
                    {
                        grant = new MaterialGrant
                        {
                            item = ingredient.item
                        };

                        materials.Add(itemId, grant);
                    }

                    grant.grantedCount += Mathf.Max(1, ingredient.amount);
                }
            }
        }

        private static string GetFacilitySaveId(FacilityType facilityType)
        {
            return facilityType switch
            {
                FacilityType.Workbench => "Workbench",
                FacilityType.Medical => "Medical",
                FacilityType.Gym => "Gym",
                FacilityType.Stash => "Stash",
                FacilityType.Kitchen => "Kitchen",
                FacilityType.Bed => "Bed",
                FacilityType.CommStation => "CommStation",
                _ => facilityType.ToString()
            };
        }

#if UNITY_EDITOR
        private static void AddRecipeMaterialsFromAssetDatabase(Dictionary<string, MaterialGrant> materials)
        {
            AddRecipeMaterialsFromAssetDatabasePath(materials, "Assets/_Project/Data/Recipes/Workbench");
            AddRecipeMaterialsFromAssetDatabasePath(materials, "Assets/_Project/Data/Recipes/Medical");
        }

        private static void AddRecipeMaterialsFromAssetDatabasePath(Dictionary<string, MaterialGrant> materials, string path)
        {
            string[] guids = AssetDatabase.FindAssets("t:RecipeSO", new[] { path });

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                RecipeSO recipe = AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath);

                if (recipe == null)
                    continue;

                AddRecipeMaterials(materials, new[] { recipe });
            }
        }
#endif

        private struct InventoryEntry
        {
            public ItemDataSO item;
            public int count;
        }

        private sealed class MaterialGrant
        {
            public ItemDataSO item;
            public int baselineCount;
            public int grantedCount;
            // mirror 값은 로비 보관함처럼 화면에 보이는 인벤토리에 추가한 테스트 수량을 추적합니다.
            public int mirrorBaselineCount;
            public int mirrorGrantedCount;
        }
    }
}
#endif

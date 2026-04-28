using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Core
{
    /// <summary>
    /// [KHW 추가 스크립트]
    /// 역할: 아이템 ScriptableObject를 itemID 기준으로 빠르게 찾아주는 아이템 데이터베이스.
    /// 기존 ItemDataSO.cs는 수정하지 않고, 이 Pool에 기존 ItemDataSO 에셋들을 등록해서 사용한다.
    ///
    /// 코드 해석:
    /// - allItems: 프로젝트에 만들어둔 ItemDataSO 에셋 배열.
    /// - itemMap: itemID -> ItemDataSO 캐시 Dictionary.
    /// - TryGetItem(): 루팅 아이템이 NetworkVariable로 받은 itemID를 실제 ItemDataSO로 변환한다.
    /// - forceOneCellItems: 인벤토리 칸을 전부 1칸으로 쓰기 위해 gridSize를 (1,1)로 정규화한다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/KHW/Script Object Pool", fileName = "KHWScriptObjectPoolSO")]
    public class KHWScriptObjectPoolSO : ScriptableObject
    {
        [Header("[KHW] 아이템 데이터베이스")]
        [Tooltip("프로젝트에서 만든 ItemDataSO 에셋들을 모두 넣습니다. itemID로 검색됩니다.")]
        [SerializeField] private ItemDataSO[] allItems;

        [Header("[KHW] 그리드 인벤토리 규칙")]
        [Tooltip("켜두면 등록된 모든 아이템의 gridSize를 1x1로 맞춥니다. 이번 파트 조건: 모든 아이템은 한 칸.")]
        [SerializeField] private bool forceOneCellItems = true;

        [Tooltip("stackCount가 0 이하로 들어오는 것을 방지하기 위한 기본 수량입니다.")]
        [SerializeField] private int defaultAmount = 1;

        private readonly Dictionary<string, ItemDataSO> itemMap = new();

        public IReadOnlyList<ItemDataSO> AllItems => allItems;
        public int DefaultAmount => Mathf.Max(1, defaultAmount);
        public bool ForceOneCellItems => forceOneCellItems;

        private void OnEnable()
        {
            BuildMap();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // [KHW 추가 기능]
            // Inspector에서 Pool을 수정할 때도 itemID 캐시와 1칸 규칙을 즉시 반영한다.
            BuildMap();
        }
#endif

        public bool TryGetItem(string itemId, out ItemDataSO item)
        {
            // [KHW 추가 기능]
            // NetworkVariable에는 string/FixedString만 안전하게 담을 수 있으므로,
            // 루팅 시 itemID를 실제 ItemDataSO로 다시 찾기 위해 사용한다.
            if (string.IsNullOrWhiteSpace(itemId))
            {
                item = null;
                return false;
            }

            if (itemMap.Count == 0)
                BuildMap();

            return itemMap.TryGetValue(itemId, out item) && item != null;
        }

        public ItemDataSO GetItemOrNull(string itemId)
        {
            return TryGetItem(itemId, out ItemDataSO item) ? item : null;
        }

        public void NormalizeOneCell(ItemDataSO item)
        {
            // [KHW 추가 기능]
            // 기존 GridInventory.cs를 수정하지 않고 "모든 아이템은 한 칸" 조건을 맞추기 위해
            // ItemDataSO의 gridSize 값을 1x1로 정규화한다.
            if (!forceOneCellItems || item == null) return;
            item.gridSize = Vector2Int.one;
        }

        private void BuildMap()
        {
            itemMap.Clear();

            if (allItems == null) return;

            foreach (ItemDataSO item in allItems)
            {
                if (item == null) continue;

                if (forceOneCellItems)
                    item.gridSize = Vector2Int.one;

                if (string.IsNullOrWhiteSpace(item.itemID))
                {
                    Debug.LogWarning($"[KHWScriptObjectPoolSO] itemID가 비어있는 아이템이 있습니다: {item.name}", item);
                    continue;
                }

                if (itemMap.ContainsKey(item.itemID))
                {
                    Debug.LogError($"[KHWScriptObjectPoolSO] 중복 itemID 발견: {item.itemID}. 나중 아이템은 무시됩니다.", item);
                    continue;
                }

                itemMap.Add(item.itemID, item);
            }
        }
    }
}

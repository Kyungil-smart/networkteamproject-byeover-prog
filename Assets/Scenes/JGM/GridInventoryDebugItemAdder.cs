using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors._LSH_Temp
{
    /// <summary>
    /// GridInventory에 테스트 아이템을 직접 추가하는 임시 디버그 스크립트입니다.
    /// PlayerCarryWeightSystem의 실제 무게 계산 검증용입니다.
    /// 테스트 완료 후 삭제 예정입니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridInventory))]
    public class GridInventoryDebugItemAdder : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField]
        [Tooltip("테스트할 플레이어 GridInventory입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private GridInventory gridInventory;

        [Header("테스트 아이템")]
        [SerializeField]
        [Tooltip("GridInventory에 추가할 테스트 아이템입니다.")]
        private ItemDataSO testItem;

        [SerializeField]
        [Min(1)]
        [Tooltip("추가할 아이템 개수입니다.")]
        private int amount = 1;

        [Header("로그")]
        [SerializeField]
        [Tooltip("아이템 추가 결과를 Console에 출력합니다.")]
        private bool logResult = true;

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnValidate()
        {
            FindRequiredComponents();

            if (amount < 1)
                amount = 1;
        }

        private void FindRequiredComponents()
        {
            if (gridInventory == null)
                gridInventory = GetComponent<GridInventory>();
        }

        [ContextMenu("디버그 테스트 아이템 추가")]
        private void DebugAddTestItem()
        {
            if (gridInventory == null)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] GridInventory가 연결되어 있지 않습니다.", this);
                return;
            }

            if (testItem == null)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] Test Item이 비어 있습니다.", this);
                return;
            }

            if (!gridInventory.IsSpawned)
            {
                Debug.LogWarning(
                    "[GridInventoryDebugItemAdder] GridInventory가 아직 Network Spawn되지 않았습니다. Play Mode에서 Host 시작 후 실행하세요.",
                    this
                );
                return;
            }

            if (!gridInventory.IsServer)
            {
                Debug.LogWarning(
                    "[GridInventoryDebugItemAdder] GridInventory 아이템 추가는 서버에서만 가능합니다. Host 모드에서 실행하세요.",
                    this
                );
                return;
            }

            bool success = gridInventory.TryAddItem(testItem, amount);

            if (!logResult)
                return;

            if (success)
            {
                Debug.Log(
                    $"[GridInventoryDebugItemAdder] 테스트 아이템 추가 성공\n" +
                    $"아이템: {testItem.displayName} ({testItem.itemID})\n" +
                    $"수량: {amount}\n" +
                    $"단위 무게: {testItem.weightKg:0.##}kg\n" +
                    $"예상 증가 무게: {testItem.weightKg * amount:0.##}kg",
                    this
                );
            }
            else
            {
                Debug.LogWarning(
                    $"[GridInventoryDebugItemAdder] 테스트 아이템 추가 실패\n" +
                    $"아이템: {testItem.displayName} ({testItem.itemID})\n" +
                    $"원인 후보: 인벤토리 공간 부족, 서버 권한 아님, item 데이터 문제",
                    this
                );
            }
        }

        [ContextMenu("디버그 GridInventory 전체 비우기")]
        private void DebugClearGridInventory()
        {
            if (gridInventory == null)
            {
                Debug.LogWarning("[GridInventoryDebugItemAdder] GridInventory가 연결되어 있지 않습니다.", this);
                return;
            }

            if (!gridInventory.IsSpawned)
            {
                Debug.LogWarning(
                    "[GridInventoryDebugItemAdder] GridInventory가 아직 Network Spawn되지 않았습니다. Play Mode에서 Host 시작 후 실행하세요.",
                    this
                );
                return;
            }

            if (!gridInventory.IsServer)
            {
                Debug.LogWarning(
                    "[GridInventoryDebugItemAdder] GridInventory 정리는 서버에서만 가능합니다. Host 모드에서 실행하세요.",
                    this
                );
                return;
            }

            while (gridInventory.ServerGrid.Count > 0)
                gridInventory.ServerGrid.RemoveAt(gridInventory.ServerGrid.Count - 1);

            if (logResult)
                Debug.Log("[GridInventoryDebugItemAdder] GridInventory를 비웠습니다.", this);
        }
    }
}
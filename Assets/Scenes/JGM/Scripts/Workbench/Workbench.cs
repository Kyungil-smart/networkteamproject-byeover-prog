using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 작업대 시설 클래스이다.
    /// 시설 레벨은 FacilityBase가 관리하고,
    /// 제작 가능 레시피 제한은 WorkbenchCraftingController가 CurrentLevel을 읽어 처리한다.
    /// </summary>
    public class Workbench : FacilityBase
    {
        protected override void OnLevelChanged(int newLevel)
        {
            // 현재 단계에서는 작업대 레벨 상태만 유지한다.
            // 제작 가능 레시피 갱신은 WorkbenchCraftingController가 CurrentLevel을 읽어 처리한다.
        }

#if UNITY_EDITOR
        [ContextMenu("Debug Upgrade With Test Inventory")]
        private void DebugUpgradeWithTestInventory()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Workbench] 플레이 중에만 업그레이드 테스트를 실행할 수 있습니다.", this);
                return;
            }

            WorkbenchTestInventory testInventory = GetComponent<WorkbenchTestInventory>();

            if (testInventory == null)
            {
                Debug.LogWarning("[Workbench] 같은 오브젝트에서 WorkbenchTestInventory를 찾지 못했습니다.", this);
                return;
            }

            TryUpgradeWithInventory(testInventory);
        }
#endif
    }
}
using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// UI, 실제 Player 인벤토리, 파밍 아이템 시스템이 완성되기 전까지
    /// WorkbenchTestInventory를 이용해 보관함 업그레이드 흐름을 검증한다.
    /// </summary>
    public sealed class StashUpgradeTestController : MonoBehaviour
    {
        [Header("보관함 시설")]
        [SerializeField]
        [Tooltip("업그레이드할 보관함 시설입니다.")]
        private StashFacility stashFacility;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("업그레이드 재료를 검사하고 소모할 테스트 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("보관함")]
        [SerializeField]
        [Tooltip("현재 보관함 크기를 출력할 Stash 컴포넌트입니다.")]
        private Stash stash;

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
        }

        private void FindRequiredComponents()
        {
            if (stashFacility == null)
                stashFacility = GetComponent<StashFacility>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();

            if (stash == null)
                stash = GetComponent<Stash>();
        }

        [ContextMenu("보관함 테스트 업그레이드")]
        public void TestUpgradeStash()
        {
            FindRequiredComponents();

            if (stashFacility == null)
            {
                Debug.LogWarning("[StashUpgradeTestController] StashFacility가 연결되어 있지 않습니다.", this);
                return;
            }

            if (testInventory == null)
            {
                Debug.LogWarning("[StashUpgradeTestController] WorkbenchTestInventory가 연결되어 있지 않습니다.", this);
                return;
            }

            bool upgraded = stashFacility.TryUpgradeForTest(testInventory);

            if (!upgraded)
            {
                Debug.LogWarning("[StashUpgradeTestController] 보관함 업그레이드 실패", this);
                return;
            }

            Debug.Log($"[StashUpgradeTestController] 보관함 업그레이드 성공. 현재 레벨: Lv.{stashFacility.CurrentLevelValue}", this);

            PrintCurrentStashSize();
        }

        [ContextMenu("현재 보관함 크기 출력")]
        public void PrintCurrentStashSize()
        {
            FindRequiredComponents();

            if (stash == null)
            {
                Debug.LogWarning("[StashUpgradeTestController] Stash가 연결되어 있지 않습니다.", this);
                return;
            }

            Debug.Log($"[StashUpgradeTestController] 현재 보관함 크기: {stash.GridWidth} x {stash.GridHeight} / 총 {stash.TotalSlotCount}칸", this);
        }
    }
}
using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// Player, UI, 실제 GridInventory가 아직 없을 때 헬스장 업그레이드를 테스트하기 위한 임시 컨트롤러이다.
    /// WorkbenchTestInventory를 사용해 재료 검사, 재료 소모, 레벨 증가를 확인하고,
    /// 업그레이드 성공 직후 테스트용 플레이어 스탯에 헬스장 보너스를 바로 적용한다.
    /// </summary>
    public sealed class GymUpgradeTestController : MonoBehaviour
    {
        [Header("헬스장 시설")]
        [SerializeField]
        [Tooltip("업그레이드할 헬스장 시설 컴포넌트입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private GymFacility gymFacility;

        [Header("테스트 인벤토리")]
        [SerializeField]
        [Tooltip("업그레이드 재료를 검사하고 소모할 테스트 인벤토리입니다.")]
        private WorkbenchTestInventory testInventory;

        [Header("테스트 스탯 적용 대상")]
        [SerializeField]
        [Tooltip("헬스장 업그레이드 성공 후 보너스를 바로 적용할 테스트용 스탯 리시버입니다.")]
        private GymTestPlayerStatReceiver statReceiver;

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
            if (gymFacility == null)
                gymFacility = GetComponent<GymFacility>();

            if (testInventory == null)
                testInventory = GetComponent<WorkbenchTestInventory>();

            if (statReceiver == null)
                statReceiver = GetComponent<GymTestPlayerStatReceiver>();
        }

        [ContextMenu("헬스장 테스트 업그레이드")]
        public void TestUpgradeGym()
        {
            if (gymFacility == null)
            {
                Debug.LogWarning("[GymUpgradeTestController] GymFacility가 연결되어 있지 않습니다.", this);
                return;
            }

            if (testInventory == null)
            {
                Debug.LogWarning("[GymUpgradeTestController] WorkbenchTestInventory가 연결되어 있지 않습니다.", this);
                return;
            }

            bool upgraded = gymFacility.TryUpgradeForTest(testInventory);

            if (!upgraded)
            {
                Debug.LogWarning("[GymUpgradeTestController] 헬스장 업그레이드 실패", this);
                return;
            }

            Debug.Log($"[GymUpgradeTestController] 헬스장 업그레이드 성공. 현재 레벨: Lv.{gymFacility.CurrentLevelValue}", this);

            ApplyStatBonusAfterUpgrade();
        }

        private void ApplyStatBonusAfterUpgrade()
        {
            if (statReceiver == null)
            {
                Debug.LogWarning("[GymUpgradeTestController] GymTestPlayerStatReceiver가 없어 헬스장 보너스를 적용하지 못했습니다.", this);
                return;
            }

            statReceiver.ApplyGymBonus();
        }
    }
}
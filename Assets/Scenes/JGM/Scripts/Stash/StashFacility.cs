using UnityEngine;

namespace DeadZone.Systems
{
    /// <summary>
    /// 구현 원리 요약:
    /// 보관함 시설의 레벨 변경을 감지하고,
    /// 실제 보관함 크기 관리는 Stash 컴포넌트에 맡긴다.
    /// </summary>
    public sealed class StashFacility : FacilityBase
    {
        [Header("보관함 연결")]
        [SerializeField]
        [Tooltip("보관함 크기를 관리하는 Stash 컴포넌트입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private Stash stash;

        [Header("보관함 로그")]
        [SerializeField]
        [Tooltip("보관함 레벨 변경 로그를 출력할지 여부입니다.")]
        private bool logLevelChanged = true;

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
            if (stash == null)
                stash = GetComponent<Stash>();
        }

        protected override void OnLevelChanged(int newLevel)
        {
            FindRequiredComponents();

            if (stash == null)
            {
                Debug.LogWarning("[StashFacility] Stash 컴포넌트를 찾지 못했습니다.", this);
                return;
            }

            stash.ApplyFacilityLevel(newLevel);

            if (logLevelChanged)
            {
                Debug.Log($"[StashFacility] 보관함 레벨 변경: Lv.{newLevel} / 크기: {stash.GridWidth} x {stash.GridHeight}", this);
            }
        }

        public bool TryUpgradeForTest(IInventory inventory)
        {
            return TryUpgradeWithInventory(inventory);
        }
    }
}
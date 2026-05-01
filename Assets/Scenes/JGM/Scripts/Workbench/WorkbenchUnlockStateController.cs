using UnityEngine;

using DeadZone.Core;
using DeadZone.Systems;

namespace DeadZone.Systems.Housing
{
    /// <summary>
    /// 작업대 레벨에 따라 현재 제작 가능한 등급 설명을 계산합니다.
    /// 실제 제작 가능 여부 검사는 WorkbenchRecipeCatalog가 담당하고, 이 스크립트는 상태 표시와 이벤트 발행만 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Workbench))]
    public class WorkbenchUnlockStateController : MonoBehaviour
    {
        [Header("작업대")]
        [SerializeField]
        [Tooltip("제작 해금 상태를 계산할 작업대입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
        private Workbench workbench;

        [Header("로그")]
        [SerializeField]
        [Tooltip("작업대 제작 해금 상태가 바뀔 때 Console에 출력합니다.")]
        private bool logUnlockChanged = true;

        private bool subscribed;

        public int CurrentLevel => workbench != null ? workbench.CurrentLevel.Value : 0;
        public string CurrentGradeLabel => GetGradeLabel(CurrentLevel);

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();
        }

        private void OnEnable()
        {
            FindRequiredComponents();
            SubscribeLevelChanged();
            ApplyLevel(CurrentLevel);
        }

        private void OnDisable()
        {
            UnsubscribeLevelChanged();
        }

        private void OnValidate()
        {
            FindRequiredComponents();
        }

        private void FindRequiredComponents()
        {
            if (workbench == null)
                workbench = GetComponent<Workbench>();
        }

        private void SubscribeLevelChanged()
        {
            if (workbench == null || subscribed)
                return;

            workbench.CurrentLevel.OnValueChanged += HandleLevelChanged;
            subscribed = true;
        }

        private void UnsubscribeLevelChanged()
        {
            if (workbench == null || !subscribed)
                return;

            workbench.CurrentLevel.OnValueChanged -= HandleLevelChanged;
            subscribed = false;
        }

        private void HandleLevelChanged(int oldLevel, int newLevel)
        {
            ApplyLevel(newLevel);
        }

        public void ApplyLevel(int level)
        {
            int safeLevel = Mathf.Clamp(level, 1, 4);
            string gradeLabel = GetGradeLabel(safeLevel);

            EventBus.Publish(new WorkbenchCraftingUnlockChangedEvent
            {
                level = safeLevel,
                unlockedGradeLabel = gradeLabel,
                maxRequiredFacilityLevel = safeLevel
            });

            if (logUnlockChanged)
            {
                Debug.Log($"[WorkbenchUnlockStateController] 작업대 Lv.{safeLevel} 효과 적용\n제작 가능 등급: {gradeLabel}\n귀중품 제작: 불가", this);
            }
        }

        public static string GetGradeLabel(int level)
        {
            switch (Mathf.Clamp(level, 1, 4))
            {
                case 1:
                    return "일반 등급";
                case 2:
                    return "희귀 등급까지";
                case 3:
                    return "레어 등급까지";
                case 4:
                    return "F2 / 드라고 소총 / 6 클래스 아머";
                default:
                    return "일반 등급";
            }
        }
    }
}

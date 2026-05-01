using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
    /// <summary>
    /// 은신처 카메라 시설 선택 라우터입니다.
    /// UI가 없을 때는 Inspector 테스트용으로 사용하고,
    /// 추후 UI 버튼이나 상호작용 입력에서도 같은 메서드를 호출할 수 있습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideoutCameraFacilitySelector : MonoBehaviour
    {
        public enum FacilityView
        {
            None,
            Workbench,
            Stash,
            Kitchen,
            Bed,
            Gym,
            CommStation,
            Medical
        }

        [Header("카메라 컨트롤러")]
        [SerializeField]
        [Tooltip("은신처 카메라 이동을 담당하는 컨트롤러입니다.")]
        private HideoutCameraController cameraController;

        [Header("테스트 선택")]
        [SerializeField]
        [Tooltip("Play Mode 중 이 값을 바꾸면 해당 시설 시점으로 이동합니다. None이면 기본 시점으로 복귀합니다.")]
        private FacilityView selectedFacility = FacilityView.None;

        [SerializeField]
        [Tooltip("Inspector에서 선택값이 바뀌면 자동으로 카메라를 이동합니다.")]
        private bool moveWhenSelectionChanged = true;

        [SerializeField]
        [Tooltip("None을 선택했을 때 기본 시점으로 돌아갈지 여부입니다.")]
        private bool returnToDefaultWhenNone = true;

        [Header("시설 카메라 타겟")]
        [SerializeField]
        [Tooltip("작업대 카메라 타겟입니다.")]
        private HideoutCameraTarget workbenchTarget;

        [SerializeField]
        [Tooltip("보관함 카메라 타겟입니다.")]
        private HideoutCameraTarget stashTarget;

        [SerializeField]
        [Tooltip("주방 카메라 타겟입니다.")]
        private HideoutCameraTarget kitchenTarget;

        [SerializeField]
        [Tooltip("침대 카메라 타겟입니다.")]
        private HideoutCameraTarget bedTarget;

        [SerializeField]
        [Tooltip("헬스장 카메라 타겟입니다.")]
        private HideoutCameraTarget gymTarget;

        [SerializeField]
        [Tooltip("통신장비 카메라 타겟입니다.")]
        private HideoutCameraTarget commStationTarget;

        [SerializeField]
        [Tooltip("의료시설 카메라 타겟입니다.")]
        private HideoutCameraTarget medicalTarget;

        [Header("디버그")]
        [SerializeField]
        [Tooltip("콘솔 로그 출력 여부입니다.")]
        private bool showDebugLog = true;

        private FacilityView previousSelectedFacility;

        public FacilityView SelectedFacility => selectedFacility;

        private void Awake()
        {
            if (cameraController == null)
            {
                cameraController = GetComponent<HideoutCameraController>();
            }

            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<HideoutCameraController>();
            }
        }

        private void Start()
        {
            previousSelectedFacility = selectedFacility;
        }

        private void Update()
        {
            if (!moveWhenSelectionChanged)
            {
                return;
            }

            if (cameraController != null && cameraController.IsMoving)
            {
                return;
            }

            if (previousSelectedFacility == selectedFacility)
            {
                return;
            }

            ApplySelectedFacility();
        }

        /// <summary>
        /// 외부에서 시설 enum으로 카메라 이동을 요청할 때 사용합니다.
        /// 추후 UI, 상호작용, 시설 클릭 입력에서 호출할 수 있습니다.
        /// </summary>
        public void SelectFacility(FacilityView facilityView)
        {
            selectedFacility = facilityView;
            ApplySelectedFacility();
        }

        /// <summary>
        /// 외부에서 시설 ID 문자열로 카메라 이동을 요청할 때 사용합니다.
        /// 예: Workbench, Stash, Kitchen, Medical
        /// </summary>
        public void SelectFacilityById(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                DebugLog("시설 ID가 비어 있습니다.");
                return;
            }

            HideoutCameraTarget target = FindTargetById(targetId);

            if (target == null)
            {
                DebugLog($"{targetId}에 해당하는 시설 카메라 타겟을 찾지 못했습니다.");
                return;
            }

            selectedFacility = GetFacilityViewByTarget(target);
            previousSelectedFacility = selectedFacility;

            DebugLog($"{target.DisplayName} 시점으로 이동합니다. ID: {targetId}");
            MoveToTarget(target);
        }

        /// <summary>
        /// 외부에서 HideoutCameraTarget을 직접 넘겨 카메라 이동을 요청할 때 사용합니다.
        /// UI 버튼이나 시설 오브젝트가 타겟을 직접 알고 있을 때 사용하기 좋습니다.
        /// </summary>
        public void SelectTarget(HideoutCameraTarget target)
        {
            if (target == null)
            {
                DebugLog("이동할 시설 카메라 타겟이 비어 있습니다.");
                return;
            }

            selectedFacility = GetFacilityViewByTarget(target);
            previousSelectedFacility = selectedFacility;

            DebugLog($"{target.DisplayName} 시점으로 이동합니다.");
            MoveToTarget(target);
        }

        /// <summary>
        /// 기본 은신처 카메라 시점으로 복귀합니다.
        /// 추후 뒤로가기 버튼이 이 메서드를 호출하면 됩니다.
        /// </summary>
        public void ReturnToDefaultView()
        {
            if (cameraController == null)
            {
                DebugLog("HideoutCameraController가 연결되지 않았습니다.");
                return;
            }

            selectedFacility = FacilityView.None;
            previousSelectedFacility = selectedFacility;

            DebugLog("기본 시점으로 이동합니다.");
            cameraController.ReturnToDefaultView();
        }

        [ContextMenu("선택한 시설 시점으로 이동")]
        public void ApplySelectedFacility()
        {
            if (cameraController == null)
            {
                DebugLog("HideoutCameraController가 연결되지 않았습니다.");
                return;
            }

            if (selectedFacility == FacilityView.None)
            {
                previousSelectedFacility = selectedFacility;

                if (returnToDefaultWhenNone)
                {
                    DebugLog("기본 시점으로 복귀합니다.");
                    cameraController.ReturnToDefaultView();
                }

                return;
            }

            HideoutCameraTarget target = GetSelectedTarget();

            if (target == null)
            {
                DebugLog($"{selectedFacility}에 연결된 카메라 타겟이 없습니다.");
                return;
            }

            previousSelectedFacility = selectedFacility;

            DebugLog($"{target.DisplayName} 시점으로 이동합니다.");
            MoveToTarget(target);
        }

        public void SelectWorkbenchView()
        {
            SelectFacility(FacilityView.Workbench);
        }

        public void SelectStashView()
        {
            SelectFacility(FacilityView.Stash);
        }

        public void SelectKitchenView()
        {
            SelectFacility(FacilityView.Kitchen);
        }

        public void SelectBedView()
        {
            SelectFacility(FacilityView.Bed);
        }

        public void SelectGymView()
        {
            SelectFacility(FacilityView.Gym);
        }

        public void SelectCommStationView()
        {
            SelectFacility(FacilityView.CommStation);
        }

        public void SelectMedicalView()
        {
            SelectFacility(FacilityView.Medical);
        }

        [ContextMenu("작업대 시점 테스트")]
        private void TestWorkbenchView()
        {
            SelectWorkbenchView();
        }

        [ContextMenu("보관함 시점 테스트")]
        private void TestStashView()
        {
            SelectStashView();
        }

        [ContextMenu("주방 시점 테스트")]
        private void TestKitchenView()
        {
            SelectKitchenView();
        }

        [ContextMenu("침대 시점 테스트")]
        private void TestBedView()
        {
            SelectBedView();
        }

        [ContextMenu("헬스장 시점 테스트")]
        private void TestGymView()
        {
            SelectGymView();
        }

        [ContextMenu("통신장비 시점 테스트")]
        private void TestCommStationView()
        {
            SelectCommStationView();
        }

        [ContextMenu("의료시설 시점 테스트")]
        private void TestMedicalView()
        {
            SelectMedicalView();
        }

        [ContextMenu("기본 시점으로 이동")]
        private void TestReturnToDefaultView()
        {
            ReturnToDefaultView();
        }

        private void MoveToTarget(HideoutCameraTarget target)
        {
            if (cameraController == null)
            {
                DebugLog("HideoutCameraController가 연결되지 않았습니다.");
                return;
            }

            cameraController.MoveToFacility(target);
        }

        private HideoutCameraTarget GetSelectedTarget()
        {
            switch (selectedFacility)
            {
                case FacilityView.Workbench:
                    return workbenchTarget;

                case FacilityView.Stash:
                    return stashTarget;

                case FacilityView.Kitchen:
                    return kitchenTarget;

                case FacilityView.Bed:
                    return bedTarget;

                case FacilityView.Gym:
                    return gymTarget;

                case FacilityView.CommStation:
                    return commStationTarget;

                case FacilityView.Medical:
                    return medicalTarget;

                case FacilityView.None:
                default:
                    return null;
            }
        }

        private HideoutCameraTarget FindTargetById(string targetId)
        {
            if (IsSameTargetId(workbenchTarget, targetId))
            {
                return workbenchTarget;
            }

            if (IsSameTargetId(stashTarget, targetId))
            {
                return stashTarget;
            }

            if (IsSameTargetId(kitchenTarget, targetId))
            {
                return kitchenTarget;
            }

            if (IsSameTargetId(bedTarget, targetId))
            {
                return bedTarget;
            }

            if (IsSameTargetId(gymTarget, targetId))
            {
                return gymTarget;
            }

            if (IsSameTargetId(commStationTarget, targetId))
            {
                return commStationTarget;
            }

            if (IsSameTargetId(medicalTarget, targetId))
            {
                return medicalTarget;
            }

            return null;
        }

        private bool IsSameTargetId(HideoutCameraTarget target, string targetId)
        {
            if (target == null)
            {
                return false;
            }

            return string.Equals(target.TargetId, targetId, System.StringComparison.OrdinalIgnoreCase);
        }

        private FacilityView GetFacilityViewByTarget(HideoutCameraTarget target)
        {
            if (target == workbenchTarget)
            {
                return FacilityView.Workbench;
            }

            if (target == stashTarget)
            {
                return FacilityView.Stash;
            }

            if (target == kitchenTarget)
            {
                return FacilityView.Kitchen;
            }

            if (target == bedTarget)
            {
                return FacilityView.Bed;
            }

            if (target == gymTarget)
            {
                return FacilityView.Gym;
            }

            if (target == commStationTarget)
            {
                return FacilityView.CommStation;
            }

            if (target == medicalTarget)
            {
                return FacilityView.Medical;
            }

            return FacilityView.None;
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
            {
                return;
            }

            Debug.Log($"[HideoutCameraFacilitySelector] {message}", this);
        }
    }
}
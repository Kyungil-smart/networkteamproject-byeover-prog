using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
    // 은신처 시설 UI의 전체 흐름을 관리
    // 시설 선택, 카메라 이동, 시설 열기 버튼 표시, 업그레이드 창 열기를 담당
    [DisallowMultipleComponent]
    public sealed class HideoutFacilityUIController : MonoBehaviour
    {
        [Header("카메라 선택")]
        [SerializeField]
        [Tooltip("시설 카메라 이동을 담당하는 선택자입니다.")]
        private HideoutCameraFacilitySelector cameraFacilitySelector;

        [Header("UI 루트")]
        [SerializeField]
        [Tooltip("시설을 선택했을 때 보이는 시설열기 버튼 루트입니다.")]
        private GameObject openFacilityButtonRoot;

        [SerializeField]
        [Tooltip("새 시설 업그레이드 창입니다. FacilityContentRoot 대신 사용합니다.")]
        private FacilityUpgradeWindowUI facilityUpgradeWindowUI;

        [Header("기존 UI 루트")]
        [SerializeField]
        [Tooltip("기존 FacilityContentRoot입니다. 새 업그레이드 UI에서는 사용하지 않습니다.")]
        private GameObject facilityContentRoot;

        [SerializeField]
        [Tooltip("기존 좌측 시설 패널입니다. 새 업그레이드 UI에서는 사용하지 않습니다.")]
        private GameObject leftFacilityPanelRoot;

        [SerializeField]
        [Tooltip("기존 우측 인벤토리 패널입니다. 새 업그레이드 UI에서는 사용하지 않습니다.")]
        private GameObject rightInventoryPanelRoot;

        [Header("동작 옵션")]
        [SerializeField]
        [Tooltip("다른 시설을 선택할 때 열려 있던 업그레이드 창을 닫습니다.")]
        private bool closeContentWhenSelectFacility = true;

        [SerializeField]
        [Tooltip("콘솔 로그를 출력합니다.")]
        private bool showDebugLog = true;

        private HideoutCameraFacilitySelector.FacilityView selectedFacility =
            HideoutCameraFacilitySelector.FacilityView.None;

        public HideoutCameraFacilitySelector.FacilityView SelectedFacility => selectedFacility;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            CloseContentOnly();
            SetOpenFacilityButtonVisible(false);
        }

        private void ResolveReferences()
        {
            if (cameraFacilitySelector == null)
                cameraFacilitySelector = FindFirstObjectByType<HideoutCameraFacilitySelector>();

            if (facilityUpgradeWindowUI == null)
                facilityUpgradeWindowUI = FindFirstObjectByType<FacilityUpgradeWindowUI>(FindObjectsInactive.Include);
        }

        public void SelectFacility(HideoutCameraFacilitySelector.FacilityView facilityView)
        {
            if (facilityView == HideoutCameraFacilitySelector.FacilityView.None)
            {
                CloseFacilityView();
                return;
            }

            selectedFacility = facilityView;

            if (cameraFacilitySelector == null)
            {
                Debug.LogWarning("[HideoutFacilityUIController] HideoutCameraFacilitySelector가 연결되지 않았습니다.", this);
            }
            else
            {
                cameraFacilitySelector.SelectFacility(facilityView);
            }

            if (closeContentWhenSelectFacility)
                CloseContentOnly();

            SetOpenFacilityButtonVisible(true);

            DebugLog($"{facilityView} 시설을 선택했습니다.");
        }

        public void OpenSelectedFacility()
        {
            if (selectedFacility == HideoutCameraFacilitySelector.FacilityView.None)
            {
                Debug.LogWarning("[HideoutFacilityUIController] 선택된 시설이 없습니다.", this);
                return;
            }

            if (facilityUpgradeWindowUI == null)
            {
                Debug.LogWarning("[HideoutFacilityUIController] FacilityUpgradeWindowUI가 연결되지 않았습니다.", this);
                return;
            }

            HideLegacyContentRoot();
            facilityUpgradeWindowUI.Open(selectedFacility);

            SetOpenFacilityButtonVisible(false);

            DebugLog($"{selectedFacility} 시설 업그레이드 창을 열었습니다.");
        }

        public void CloseFacilityView()
        {
            selectedFacility = HideoutCameraFacilitySelector.FacilityView.None;

            CloseContentOnly();
            SetOpenFacilityButtonVisible(false);

            if (cameraFacilitySelector != null)
                cameraFacilitySelector.ReturnToDefaultView();

            DebugLog("시설 UI를 닫고 기본 시점으로 돌아갑니다.");
        }

        public void CloseContentOnly()
        {
            if (facilityUpgradeWindowUI != null)
                facilityUpgradeWindowUI.Close();

            HideLegacyContentRoot();
        }

        public void SelectWorkbench()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Workbench);
        }

        public void SelectStash()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Stash);
        }

        public void SelectKitchen()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Kitchen);
        }

        public void SelectBed()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Bed);
        }

        public void SelectGym()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Gym);
        }

        public void SelectCommStation()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.CommStation);
        }

        public void SelectMedical()
        {
            SelectFacility(HideoutCameraFacilitySelector.FacilityView.Medical);
        }

        private void SetOpenFacilityButtonVisible(bool visible)
        {
            if (openFacilityButtonRoot != null)
                openFacilityButtonRoot.SetActive(visible);
        }

        private void HideLegacyContentRoot()
        {
            HideIfNotUpgradeWindow(facilityContentRoot);
            HideIfNotUpgradeWindow(leftFacilityPanelRoot);
            HideIfNotUpgradeWindow(rightInventoryPanelRoot);
        }

        private void HideIfNotUpgradeWindow(GameObject target)
        {
            if (target == null)
                return;

            if (facilityUpgradeWindowUI != null && target == facilityUpgradeWindowUI.WindowRoot)
                return;

            target.SetActive(false);
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[HideoutFacilityUIController] {message}", this);
        }
    }
}
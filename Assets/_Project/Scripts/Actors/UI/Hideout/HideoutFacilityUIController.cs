using UnityEngine;

using DeadZone.Actors.UI;

namespace DeadZone.Actors.UI.Hideout
{
    // 은신처 시설 UI의 전체 흐름을 관리
    // 시설 버튼 선택, 카메라 이동 요청, 시설 열기, 뒤로가기를 담당
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
        [Tooltip("좌측 시설 패널과 우측 인벤토리 패널을 묶는 전체 루트입니다.")]
        private GameObject facilityContentRoot;

        [SerializeField]
        [Tooltip("화면 중앙 좌측의 시설 정보 패널입니다.")]
        private GameObject leftFacilityPanelRoot;

        [SerializeField]
        [Tooltip("화면 중앙 우측의 플레이어 인벤토리 패널입니다.")]
        private GameObject rightInventoryPanelRoot;

        [Header("인벤토리")]
        [SerializeField]
        [Tooltip("우측 플레이어 인벤토리 UI입니다. 아직 없으면 비워둬도 됩니다.")]
        private InventoryUI inventoryUI;

        [Header("동작 옵션")]
        [SerializeField]
        [Tooltip("다른 시설을 선택할 때 열려 있던 좌우 패널을 자동으로 닫습니다.")]
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

            if (inventoryUI == null)
                inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        }

        /// 상단 시설 버튼에서 호출
        /// 시설을 선택하고 카메라를 해당 시설 시점으로 이동
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

        // 시설열기 버튼에서 호출
        // 선택된 시설의 좌측 패널과 우측 인벤토리 패널
        public void OpenSelectedFacility()
        {
            if (selectedFacility == HideoutCameraFacilitySelector.FacilityView.None)
            {
                Debug.LogWarning("[HideoutFacilityUIController] 선택된 시설이 없습니다.", this);
                return;
            }

            SetContentVisible(true);

            if (inventoryUI != null)
                inventoryUI.Open();
            else if (rightInventoryPanelRoot != null)
                rightInventoryPanelRoot.SetActive(true);

            DebugLog($"{selectedFacility} 시설 UI를 열었습니다.");
        }

        /// 뒤로가기 버튼에서 호출
        /// 시설 UI를 닫고 기본 은신처 카메라 시점으로 복귀
        public void CloseFacilityView()
        {
            selectedFacility = HideoutCameraFacilitySelector.FacilityView.None;

            CloseContentOnly();
            SetOpenFacilityButtonVisible(false);

            if (cameraFacilitySelector != null)
                cameraFacilitySelector.ReturnToDefaultView();

            DebugLog("시설 UI를 닫고 기본 시점으로 돌아갑니다.");
        }

        /// <summary>
        /// 좌우 패널만 닫습니다. 카메라 시점은 변경하지 않습니다.
        /// </summary>
        public void CloseContentOnly()
        {
            if (inventoryUI != null)
                inventoryUI.Close();

            SetContentVisible(false);
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

        private void SetContentVisible(bool visible)
        {
            if (facilityContentRoot != null)
                facilityContentRoot.SetActive(visible);

            if (leftFacilityPanelRoot != null)
                leftFacilityPanelRoot.SetActive(visible);

            if (rightInventoryPanelRoot != null)
                rightInventoryPanelRoot.SetActive(visible);
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
                return;

            Debug.Log($"[HideoutFacilityUIController] {message}", this);
        }
    }
}
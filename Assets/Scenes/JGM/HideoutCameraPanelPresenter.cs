using System.Collections.Generic;

using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
    /// <summary>
    /// 은신처 카메라 시점에 따라 UI 패널 표시를 관리합니다.
    /// 현재 UI가 없어도 사용할 수 있으며, 추후 UI 패널을 연결하면 자동으로 연동됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideoutCameraPanelPresenter : MonoBehaviour
    {
        [Header("카메라 컨트롤러")]
        [SerializeField]
        [Tooltip("은신처 카메라 이동을 담당하는 컨트롤러입니다.")]
        private HideoutCameraController cameraController;

        [Header("기본 UI")]
        [SerializeField]
        [Tooltip("기본 은신처 화면에서 보여줄 패널입니다. 아직 UI가 없으면 비워둬도 됩니다.")]
        private GameObject defaultPanel;

        [SerializeField]
        [Tooltip("시설 시점에서 보여줄 뒤로가기 버튼 오브젝트입니다. 아직 UI가 없으면 비워둬도 됩니다.")]
        private GameObject backButtonObject;

        [Header("시설 패널")]
        [SerializeField]
        [Tooltip("시설별 패널 목록입니다. 일괄 비활성화 용도입니다. 아직 UI가 없으면 비워둬도 됩니다.")]
        private List<GameObject> facilityPanels = new List<GameObject>();

        [SerializeField]
        [Tooltip("HideoutCameraTarget에 연결된 Linked Panel을 자동으로 표시합니다.")]
        private bool useTargetLinkedPanel = true;

        [SerializeField]
        [Tooltip("시설 시점으로 이동하면 기본 패널을 숨깁니다.")]
        private bool hideDefaultPanelOnFacilityView = true;

        [SerializeField]
        [Tooltip("시작 시 시설 패널들을 모두 숨깁니다.")]
        private bool hideFacilityPanelsOnStart = true;

        [Header("디버그")]
        [SerializeField]
        [Tooltip("콘솔 로그 출력 여부입니다.")]
        private bool showDebugLog = true;

        private GameObject currentPanel;

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

        private void OnEnable()
        {
            if (cameraController == null)
            {
                return;
            }

            cameraController.OnViewChanged += HandleViewChanged;
        }

        private void OnDisable()
        {
            if (cameraController == null)
            {
                return;
            }

            cameraController.OnViewChanged -= HandleViewChanged;
        }

        private void Start()
        {
            if (hideFacilityPanelsOnStart)
            {
                HideAllFacilityPanels();
            }

            ApplyDefaultPanelState();
        }

        private void HandleViewChanged(HideoutCameraTarget target)
        {
            if (target == null)
            {
                ShowDefaultViewPanels();
                return;
            }

            ShowFacilityViewPanels(target);
        }

        private void ShowDefaultViewPanels()
        {
            HideCurrentPanel();
            HideAllFacilityPanels();

            SetActiveSafe(defaultPanel, true);
            SetActiveSafe(backButtonObject, false);

            currentPanel = null;

            DebugLog("기본 시점 UI 상태로 전환했습니다.");
        }

        private void ShowFacilityViewPanels(HideoutCameraTarget target)
        {
            HideCurrentPanel();
            HideAllFacilityPanels();

            if (hideDefaultPanelOnFacilityView)
            {
                SetActiveSafe(defaultPanel, false);
            }

            SetActiveSafe(backButtonObject, true);

            GameObject panelToShow = null;

            if (useTargetLinkedPanel && target.LinkedPanel != null)
            {
                panelToShow = target.LinkedPanel;
            }

            if (panelToShow != null)
            {
                SetActiveSafe(panelToShow, true);
                currentPanel = panelToShow;

                DebugLog($"{target.DisplayName} 패널을 표시했습니다.");
            }
            else
            {
                currentPanel = null;

                DebugLog($"{target.DisplayName}에 연결된 UI 패널이 없습니다. 카메라 이동만 처리됩니다.");
            }
        }

        private void ApplyDefaultPanelState()
        {
            SetActiveSafe(defaultPanel, true);
            SetActiveSafe(backButtonObject, false);
        }

        private void HideCurrentPanel()
        {
            if (currentPanel == null)
            {
                return;
            }

            SetActiveSafe(currentPanel, false);
            currentPanel = null;
        }

        private void HideAllFacilityPanels()
        {
            for (int i = 0; i < facilityPanels.Count; i++)
            {
                SetActiveSafe(facilityPanels[i], false);
            }
        }

        private void SetActiveSafe(GameObject targetObject, bool active)
        {
            if (targetObject == null)
            {
                return;
            }

            if (targetObject.activeSelf == active)
            {
                return;
            }

            targetObject.SetActive(active);
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
            {
                return;
            }

            Debug.Log($"[HideoutCameraPanelPresenter] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("기본 UI 상태로 전환")]
        private void Editor_ShowDefaultViewPanels()
        {
            ShowDefaultViewPanels();
        }

        [ContextMenu("시설 패널 모두 숨기기")]
        private void Editor_HideAllFacilityPanels()
        {
            HideAllFacilityPanels();
        }
#endif
    }
}
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI.Hideout
{
    // 상단 시설 버튼 하나를 담당
    // 버튼마다 선택할 시설 타입만 다르게 설정
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class HideoutFacilityButton : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField]
        [Tooltip("은신처 시설 UI 전체 컨트롤러입니다.")]
        private HideoutFacilityUIController uiController;

        [Header("시설")]
        [SerializeField]
        [Tooltip("이 버튼이 선택할 시설입니다.")]
        private HideoutCameraFacilitySelector.FacilityView facilityView =
            HideoutCameraFacilitySelector.FacilityView.None;

        private Button button;

        private void Reset()
        {
            button = GetComponent<Button>();
        }

        private void Awake()
        {
            button = GetComponent<Button>();

            if (uiController == null)
                uiController = FindFirstObjectByType<HideoutFacilityUIController>();
        }

        private void OnEnable()
        {
            if (button == null)
                button = GetComponent<Button>();

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        private void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (uiController == null)
            {
                Debug.LogWarning("[HideoutFacilityButton] HideoutFacilityUIController가 연결되지 않았습니다.", this);
                return;
            }

            uiController.SelectFacility(facilityView);
        }
    }
}
using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
    /// <summary>
    /// 은신처 시설별 카메라 이동 지점입니다.
    /// 카메라가 어디로 이동할지, 어떤 시설 UI와 연결될지를 보관합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideoutCameraTarget : MonoBehaviour
    {
        [Header("시설 정보")]
        [SerializeField]
        [Tooltip("시설을 구분하기 위한 ID입니다. 예: Workbench, Stash, Kitchen, Medical")]
        private string targetId;

        [SerializeField]
        [Tooltip("UI나 로그에 표시할 시설 이름입니다.")]
        private string displayName;

        [Header("카메라 위치")]
        [SerializeField]
        [Tooltip("카메라가 이동할 위치입니다. 비워두면 이 오브젝트의 Transform을 사용합니다.")]
        private Transform cameraPoint;

        [Header("추후 UI 연동")]
        [SerializeField]
        [Tooltip("시설 선택 시 열릴 UI 패널입니다. 아직 UI가 없으면 비워둬도 됩니다.")]
        private GameObject linkedPanel;

        [Header("선택 가능 여부")]
        [SerializeField]
        [Tooltip("false면 버튼에서 선택 요청이 들어와도 이동하지 않습니다.")]
        private bool canSelect = true;

        public string TargetId => targetId;
        public string DisplayName => displayName;
        public Transform CameraPoint => cameraPoint != null ? cameraPoint : transform;
        public GameObject LinkedPanel => linkedPanel;
        public bool CanSelect => canSelect;

        private void OnValidate()
        {
            if (cameraPoint == null)
            {
                cameraPoint = transform;
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                targetId = gameObject.name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = gameObject.name;
            }
        }

        public void SetSelectable(bool value)
        {
            canSelect = value;
        }
    }
}
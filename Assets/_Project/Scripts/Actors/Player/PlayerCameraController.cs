using UnityEngine;
using Unity.Netcode;

namespace DeadZone.Actors
{
    public class PlayerCameraController : NetworkBehaviour
    {
        [Header("====카메라 참조====")]
        [Tooltip("Owner일 때 활성화할 플레이어 전용 카메라 루트")]
        [SerializeField] private GameObject cameraRoot;
        
        [Tooltip("마우스 조준 Raycast와 화면 렌더링에 사용할 플레이어 전용 카메라" +
                 "\nOwner일 때만 활성화되고, PlayerInputController에 입력 기준 카메라로 전달")]
        [SerializeField] private Camera playerCamera;
        
        [Tooltip("Owner 플레이어에서만 활성화할 오디오리스너" +
                 "\n비Owner 플레이어의 AudioListener는 중복 청취를 막기 위해 반드시 꺼져 있어야 합니다.")]
        [SerializeField] private AudioListener audioListener;
        
        [Header("====쿼터뷰 추적 설정====")]
        [Tooltip("카메라가 따라갈 기준 Transform 비워두면 Player Root를 기준으로 사용")]
        [SerializeField] private Transform followTarget;
        
        [Tooltip("카메라가 따라갈 대상 위치에 더할 월드 좌표 오프셋 Player가 회전해도 같이 회전하지 않음")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 12f, -8f);
        
        [Tooltip("쿼터뷰 시점을 유지하기 위한 카메라 월드 회전값" +
                 "\nPlayer 회전에 끌리지 않도록 LateUpdate에서 이 회전을 다시 적용")]
        [SerializeField] private Vector3 worldEulerAngles = new Vector3(60f, 0f, 0f);
        
        [Header("====플레이어 연결 대상====")]
        [Tooltip("Owner 카메라를 주입받을 입력 컨트롤러" +
                 "\n비워두면 같은 Player Root에서 자동으로 찾습니다.")]
        [SerializeField] private PlayerInputController inputController;
        
        [Tooltip("카메라 기준 이동 Transform을 주입받을 이동 컨트롤러" +
                 "\n비워두면 같은 Player Root에서 자동으로 찾습니다.")]
        [SerializeField] private FPSController fpsController;

        private Transform cameraTransform;
        private Quaternion fixedWorldRotation;
        private bool isLocalOwnerCamera;

        private void Awake()
        {
            cameraTransform = playerCamera != null ? playerCamera.transform : null;
            fixedWorldRotation = Quaternion.Euler(worldEulerAngles);

            if (followTarget == null) followTarget = transform;
            
            if (inputController == null) 
                inputController = GetComponent<PlayerInputController>();
            
            if (fpsController == null)
                fpsController = GetComponent<FPSController>();
            
            
            // 중복 Camera / AudioListener 경고를 막기 위해 기본 상태는 비활성화
            SetCameraActive(false);
        }

        private void LateUpdate()
        {
            if (!isLocalOwnerCamera) return;
            ApplyCameraTransform();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            isLocalOwnerCamera = IsOwner;
            SetCameraActive(isLocalOwnerCamera);
            
            if (!isLocalOwnerCamera) return;
            
            if (inputController != null && playerCamera != null)
                inputController.SetInputCamera(playerCamera);

            if (fpsController != null && cameraTransform != null)
            {
                // Owner 카메라의 월드 회전을 고정한 뒤 이동 기준으로 사용
                fpsController.SetMovementReference(cameraTransform);
            }
            
            ApplyCameraTransform();
        }

        public override void OnNetworkDespawn()
        {
            isLocalOwnerCamera = false;
            SetCameraActive(false);
            
            base.OnNetworkDespawn();
        }
            
        private void ApplyCameraTransform()
        {
            if (cameraTransform == null || followTarget == null) return;

            cameraTransform.position = followTarget.position + worldOffset;
            cameraTransform.rotation = fixedWorldRotation;
        }

        private void SetCameraActive(bool active)
        {
            if (cameraRoot != null) cameraRoot.SetActive(active);
            if (playerCamera != null) playerCamera.enabled = active;
            if (audioListener != null) audioListener.enabled = active;
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            fixedWorldRotation = Quaternion.Euler(worldEulerAngles);            
        }
#endif
    }    
}



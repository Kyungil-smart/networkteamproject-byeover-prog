using Unity.Netcode;
using UnityEngine;

namespace DeadZone.Actors
{
    public class PlayerCameraController : NetworkBehaviour
    {
        [Header("====카메라 참조====")]
        [Tooltip("Owner 플레이어에서만 활성화할 카메라 루트 오브젝트" +
                 "\n보통 PlayerPrefab 하위의 CameraHolder/PlayerCamera를 사용" +
                 "\n비워두면 런타임에 CameraHolder/PlayerCamera 경로를 찾아 자동 할당")]
        [SerializeField] private GameObject cameraRoot;

        [Tooltip("화면 렌더링과 마우스 조준 Raycast 기준으로 사용할 플레이어 전용 카메라" +
                 "\nOwner일 때만 enabled=true가 되어야 합니다.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Owner 플레이어에서만 활성화할 AudioListener" +
                 "\n여러 플레이어 카메라가 동시에 켜지면 AudioListener 중복 경고가 발생하므로 Owner만 켭니다.")]
        [SerializeField] private AudioListener audioListener;

        [Header("====쿼터뷰 추적 설정====")]
        [Tooltip("카메라가 따라갈 대상" +
                 "\n비워두면 이 PlayerCameraController가 붙은 Player Root Transform을 사용")]
        [SerializeField] private Transform followTarget;

        [Tooltip("추적 대상 위치를 기준으로 카메라를 배치할 월드 오프셋" +
                 "\n기본값 (0, 12, -8)은 플레이어 위쪽에서 뒤로 떨어진 쿼터뷰 시작값")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 12f, -8f);

        [Tooltip("카메라가 바라볼 목표 지점의 보정값" +
                 "\n기본값 (0, 1, 0)은 플레이어 발밑이 아니라 상체 근처를 바라보게 하기 위한 값")]
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);

        [Header("====플레이어 컨트롤러 연결====")]
        [Tooltip("마우스 조준 Raycast에 사용할 카메라를 전달받는 입력 컨트롤러" +
                 "\n비워두면 같은 Player Root에서 PlayerInputController를 자동 탐색")]
        [SerializeField] private PlayerInputController inputController;

        [Tooltip("이동 기준 방향을 카메라 기준으로 맞추기 위한 이동 컨트롤러" +
                 "\n비워두면 같은 Player Root에서 FPSController를 자동 탐색")]
        [SerializeField] private FPSController fpsController;
        
        [Header("====디버그=====")]
        [Tooltip("카메라 활성화 상태, Owner 여부, AudioListener 상태를 Console에 출력" +
                 "\nNo cameras rendering 또는 Owner 카메라 비활성 문제를 추적할 때만 켭니다.")]
        [SerializeField] private bool enableDebugLogs;

        private Transform cameraTransform;
        private bool isLocalOwnerCamera;

        private void Awake()
        {
            ResolveRuntimeCameraReferences();

            if (followTarget == null)
                followTarget = transform;

            if (inputController == null)
                inputController = GetComponent<PlayerInputController>();

            if (fpsController == null)
                fpsController = GetComponent<FPSController>();

            SetCameraActive(false, "Awake");
            LogCameraState("Awake");
        }

        /// <summary>
        /// 네트워크 스폰 이후 Owner 여부를 기준으로 로컬 전용 카메라를 활성화하고,
        /// 입력 / 이동 컨트롤러에 카메라 기준 참조를 전달한다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            isLocalOwnerCamera = IsOwner;
            SetCameraActive(isLocalOwnerCamera, "OnNetworkSpawn");

            if (!isLocalOwnerCamera)
                return;

            EnsureOwnerCameraActive();

            if (inputController != null && playerCamera != null)
                inputController.SetInputCamera(playerCamera);

            if (fpsController != null && cameraTransform != null)
                fpsController.SetMovementReference(cameraTransform);

            ApplyCameraTransform();
            LogCameraState("OnNetworkSpawn");
        }

        /// <summary>
        /// 네트워크 디스폰 시 로컬 Owner 카메라 상태를 정리한다.
        /// 단, Play 중 카메라가 예기치 않게 꺼진다면 이 메서드의 비활성화 호출을 먼저 의심
        /// </summary>
        public override void OnNetworkDespawn()
        {
            isLocalOwnerCamera = false;
            SetCameraActive(false, "OnNetworkDespawn");

            base.OnNetworkDespawn();
        }

        private void LateUpdate()
        {
            if (!isLocalOwnerCamera)
                return;

            EnsureOwnerCameraActive();
            ApplyCameraTransform();
        }

        private void OnDestroy()
        {
            SetCameraActive(false, "OnDestroy");
        }

        /// <summary>
        /// PlayerPrefab 내부의 기본 카메라 경로를 찾아 런타임 참조를 자동 할당한다.
        /// </summary>
        private void ResolveRuntimeCameraReferences()
        {
            Transform runtimeCameraTransform = transform.Find("CameraHolder/PlayerCamera");

            if (runtimeCameraTransform != null)
            {
                cameraRoot = runtimeCameraTransform.gameObject;
                playerCamera = runtimeCameraTransform.GetComponent<Camera>();
                audioListener = runtimeCameraTransform.GetComponent<AudioListener>();
            }

            cameraTransform = playerCamera != null ? playerCamera.transform : null;
        }

        /// <summary>
        /// Camera Root는 유지한 채 Camera / AudioListener 컴포넌트의 enabled 상태만 제어한다.
        /// </summary>
        private void SetCameraActive(bool active, string reason)
        {
            if (cameraRoot != null)
                cameraRoot.SetActive(true);

            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(true);
                playerCamera.enabled = active;
            }

            if (audioListener != null)
                audioListener.enabled = active;

            LogCameraState($"SetCameraActive({active}) / {reason}");
        }

        /// <summary>
        /// Owner 카메라가 런타임 중 비활성화되었을 때 다시 활성화한다.
        /// 테스트 단계에서 No cameras rendering 문제를 방지하기 위한 안전장치다.
        /// </summary>
        private void EnsureOwnerCameraActive()
        {
            if (cameraRoot != null && !cameraRoot.activeSelf)
                cameraRoot.SetActive(true);

            if (playerCamera != null)
            {
                if (!playerCamera.gameObject.activeSelf)
                    playerCamera.gameObject.SetActive(true);

                if (!playerCamera.enabled)
                    playerCamera.enabled = true;
            }

            if (audioListener != null && !audioListener.enabled)
                audioListener.enabled = true;
        }

        /// <summary>
        /// 추적 대상 위치와 오프셋을 기준으로 쿼터뷰 카메라의 월드 위치와 회전을 계산한다.
        /// </summary>
        private void ApplyCameraTransform()
        {
            if (cameraTransform == null || followTarget == null)
                return;

            Vector3 cameraPosition = followTarget.position + worldOffset;
            Vector3 lookTarget = followTarget.position + lookAtOffset;
            Vector3 lookDirection = lookTarget - cameraPosition;

            if (lookDirection.sqrMagnitude <= 0.0001f)
                return;

            cameraTransform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(lookDirection, Vector3.up));
        }

        /// <summary>
        /// Owner 여부, Camera enabled, AudioListener enabled 등 카메라 활성 상태를 추적한다.
        /// </summary>
        private void LogCameraState(string reason)
        {
            if (!enableDebugLogs)
                return;

            Debug.Log(
                $"[PlayerCameraController] {reason} | " +
                $"Object={name}, " +
                $"IsOwner={IsOwner}, " +
                $"CameraRoot={(cameraRoot != null ? cameraRoot.name : "NULL")}, " +
                $"CameraEnabled={(playerCamera != null ? playerCamera.enabled.ToString() : "NULL")}, " +
                $"CameraActiveInHierarchy={(playerCamera != null ? playerCamera.gameObject.activeInHierarchy.ToString() : "NULL")}, " +
                $"CameraScene={(playerCamera != null ? playerCamera.gameObject.scene.name : "NULL")}, " +
                $"ListenerEnabled={(audioListener != null ? audioListener.enabled.ToString() : "NULL")}, " +
                $"CameraMain={(Camera.main != null ? Camera.main.name : "NULL")}",
                this);
        }
    }
}

using System;
using System.Collections;

using UnityEngine;

namespace DeadZone.Actors.UI.Hideout
{
    /// <summary>
    /// 은신처 카메라 시점 전환을 담당합니다.
    /// 기본 시점, 시설 시점 이동, 기본 시점 복귀를 한 곳에서 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideoutCameraController : MonoBehaviour
    {
        [Header("카메라")]
        [SerializeField]
        [Tooltip("움직일 카메라입니다. 비워두면 Camera.main을 자동으로 찾습니다.")]
        private Camera controlledCamera;

        [SerializeField]
        [Tooltip("은신처 기본 카메라 위치입니다.")]
        private Transform defaultViewPoint;

        [Header("이동 설정")]
        [SerializeField]
        [Tooltip("카메라 이동에 걸리는 시간입니다.")]
        private float moveDuration = 0.6f;

        [SerializeField]
        [Tooltip("게임 시작 시 기본 시점으로 즉시 배치할지 여부입니다.")]
        private bool applyDefaultViewOnStart = true;

        [SerializeField]
        [Tooltip("이동 중 다른 시설 선택을 막을지 여부입니다.")]
        private bool blockInputWhileMoving = true;

        [Header("디버그")]
        [SerializeField]
        [Tooltip("콘솔 로그 출력 여부입니다.")]
        private bool showDebugLog = true;

        private Coroutine moveRoutine;
        private HideoutCameraTarget currentTarget;
        private bool isMoving;

        public event Action<HideoutCameraTarget> OnViewChanged;
        public event Action<HideoutCameraTarget> OnCameraMoveStarted;
        public event Action<HideoutCameraTarget> OnCameraMoveFinished;

        public bool IsMoving => isMoving;
        public HideoutCameraTarget CurrentTarget => currentTarget;

        private Transform CameraTransform
        {
            get
            {
                if (controlledCamera == null)
                {
                    controlledCamera = Camera.main;
                }

                return controlledCamera != null ? controlledCamera.transform : null;
            }
        }

        private void Awake()
        {
            if (controlledCamera == null)
            {
                controlledCamera = Camera.main;
            }
        }

        private void Start()
        {
            if (applyDefaultViewOnStart)
            {
                ApplyDefaultViewImmediate();
            }
        }

        public void MoveToFacility(HideoutCameraTarget target)
        {
            if (target == null)
            {
                DebugLog("시설 카메라 타겟이 비어 있습니다.");
                return;
            }

            if (!target.CanSelect)
            {
                DebugLog($"{target.DisplayName} 시설은 현재 선택할 수 없습니다.");
                return;
            }

            if (blockInputWhileMoving && isMoving)
            {
                DebugLog("카메라 이동 중이라 다른 시설로 이동할 수 없습니다.");
                return;
            }

            MoveToPoint(target.CameraPoint, target);
        }

        public void ReturnToDefaultView()
        {
            if (defaultViewPoint == null)
            {
                DebugLog("기본 카메라 시점이 연결되지 않았습니다.");
                return;
            }

            MoveToPoint(defaultViewPoint, null);
        }

        public void ApplyDefaultViewImmediate()
        {
            Transform cameraTransform = CameraTransform;

            if (cameraTransform == null)
            {
                DebugLog("이동할 카메라를 찾을 수 없습니다.");
                return;
            }

            if (defaultViewPoint == null)
            {
                DebugLog("기본 카메라 시점이 연결되지 않았습니다.");
                return;
            }

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            isMoving = false;
            currentTarget = null;

            cameraTransform.SetPositionAndRotation(defaultViewPoint.position, defaultViewPoint.rotation);
            OnViewChanged?.Invoke(null);

            DebugLog("기본 카메라 시점으로 즉시 배치했습니다.");
        }

        private void MoveToPoint(Transform targetPoint, HideoutCameraTarget nextTarget)
        {
            Transform cameraTransform = CameraTransform;

            if (cameraTransform == null)
            {
                DebugLog("이동할 카메라를 찾을 수 없습니다.");
                return;
            }

            if (targetPoint == null)
            {
                DebugLog("카메라 이동 지점이 비어 있습니다.");
                return;
            }

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
            }

            moveRoutine = StartCoroutine(MoveRoutine(cameraTransform, targetPoint, nextTarget));
        }

        private IEnumerator MoveRoutine(Transform cameraTransform, Transform targetPoint, HideoutCameraTarget nextTarget)
        {
            isMoving = true;
            OnCameraMoveStarted?.Invoke(nextTarget);

            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;

            Vector3 endPosition = targetPoint.position;
            Quaternion endRotation = targetPoint.rotation;

            float elapsedTime = 0f;
            float safeDuration = Mathf.Max(0.01f, moveDuration);

            DebugLog(nextTarget == null
                ? "기본 카메라 시점으로 이동합니다."
                : $"{nextTarget.DisplayName} 시설 카메라 시점으로 이동합니다.");

            while (elapsedTime < safeDuration)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / safeDuration);

                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                cameraTransform.position = Vector3.Lerp(startPosition, endPosition, smoothT);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, endRotation, smoothT);

                yield return null;
            }

            cameraTransform.SetPositionAndRotation(endPosition, endRotation);

            currentTarget = nextTarget;
            isMoving = false;
            moveRoutine = null;

            OnViewChanged?.Invoke(currentTarget);
            OnCameraMoveFinished?.Invoke(currentTarget);

            DebugLog(currentTarget == null
                ? "기본 카메라 시점 이동이 완료되었습니다."
                : $"{currentTarget.DisplayName} 시설 카메라 시점 이동이 완료되었습니다.");
        }

        private void DebugLog(string message)
        {
            if (!showDebugLog)
            {
                return;
            }

            Debug.Log($"[HideoutCameraController] {message}", this);
        }

#if UNITY_EDITOR
        [ContextMenu("기본 시점으로 즉시 이동")]
        private void Editor_ApplyDefaultViewImmediate()
        {
            ApplyDefaultViewImmediate();
        }

        [ContextMenu("기본 시점으로 부드럽게 이동")]
        private void Editor_ReturnToDefaultView()
        {
            ReturnToDefaultView();
        }
#endif
    }
}
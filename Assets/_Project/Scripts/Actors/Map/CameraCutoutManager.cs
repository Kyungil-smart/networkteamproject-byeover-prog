using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 카메라 컷아웃 셰이더에 플레이어/마우스 기준 컷아웃 값을 전달하고,
    /// 플레이어와 카메라 사이의 시야를 가리는 오브젝트에만 컷아웃 적용 여부를 설정한다.
    /// </summary>
    public class CameraCutoutManager : MonoBehaviour
    {
        private static readonly int CutoutPlayerCenterId = Shader.PropertyToID("_CutoutPlayerCenter");
        private static readonly int CutoutLookCenterId = Shader.PropertyToID("_CutoutLookCenter");
        private static readonly int CutoutPlayerRadiusId = Shader.PropertyToID("_CutoutPlayerRadius");
        private static readonly int CutoutLookRadiusId = Shader.PropertyToID("_CutoutLookRadius");
        private static readonly int CutoutMinYId = Shader.PropertyToID("_CutoutMinY");
        private static readonly int CutoutFeatherId = Shader.PropertyToID("_CutoutFeather");
        private static readonly int CutoutHeightFeatherId = Shader.PropertyToID("_CutoutHeightFeather");
        private static readonly int CutoutUseFullObjectHeightId = Shader.PropertyToID("_CutoutUseFullObjectHeight");
        private static readonly int UseCameraCutoutId = Shader.PropertyToID("_UseCameraCutout");

        [Header("====추적 대상====")]
        [Tooltip("컷아웃 중심 계산 기준이 되는 로컬 Owner 플레이어 루트\n비워두면 OwnerPlayerRootRegisteredEvent로 자동 설정")]
        [SerializeField] private Transform followTarget;

        [Tooltip("마우스 화면 좌표를 월드 좌표로 변환할 때 사용할 카메라\n비워두면 Camera.main을 사용")]
        [SerializeField] private Camera inputCamera;

        [Header("====마우스 월드 위치 기준====")]
        [Tooltip("마우스 Raycast가 맞출 지면 레이어")]
        [SerializeField] private LayerMask aimMask;

        [Tooltip("마우스 Raycast 최대 거리")]
        [SerializeField, Min(1f)] private float aimRayDistance = 200f;

        [Header("====컷아웃 범위====")]
        [Tooltip("플레이어 기준 이 높이보다 위쪽 픽셀만 컷아웃한다")]
        [SerializeField, Min(0f)] private float cutoutHeightOffset = 1f;

        [Tooltip("플레이어 주변 컷아웃 반경")]
        [SerializeField, Min(0f)] private float playerCutoutRadius = 3f;

        [Tooltip("마우스 월드 위치 주변 컷아웃 반경")]
        [SerializeField, Min(0f)] private float lookCutoutRadius = 2.5f;

        [Tooltip("컷아웃 경계에만 적용할 디더 완충 폭")]
        [SerializeField, Min(0f)] private float cutoutFeather = 0.35f;

        [Tooltip("높이 기준 컷아웃 경계에만 적용할 디더 완충 폭")]
        [SerializeField, Min(0f)] private float heightCutoutFeather = 0.3f;

        [Tooltip("참이면 플레이어/마우스 주변 범위가 아니라 오브젝트 전체를 높이 기준으로 컷아웃한다")]
        [SerializeField] private bool useFullObjectHeightCutout = false;

        [Tooltip("전체 높이 컷아웃 모드로 전환되거나 복귀하는 데 걸리는 시간")]
        [SerializeField, Min(0.01f)] private float fullObjectCutoutTransitionTime = 0.25f;

        [Tooltip("전체 높이 컷아웃으로 전환되기 전까지 확장될 원형 컷아웃 반경")]
        [SerializeField, Min(0f)] private float fullObjectCutoutExpandedRadius = 8f;

        [Header("====가림 오브젝트 검출====")]
        [Tooltip("참이면 카메라와 플레이어 사이를 검사해 맞은 오브젝트에만 컷아웃을 적용한다")]
        [SerializeField] private bool useRaycastOccluderTest = true;

        [Tooltip("카메라와 플레이어 사이에서 시야를 가릴 수 있는 오브젝트 레이어")]
        [SerializeField] private LayerMask occluderMask = ~0;

        [Tooltip("가림 오브젝트 검출 Raycast가 향할 플레이어 머리 위 높이")]
        [SerializeField, Min(0f)] private float occluderTargetHeightOffset = 1.5f;

        [Tooltip("Raycast 대신 SphereCast를 사용할 반경. 0이면 Raycast처럼 동작한다")]
        [SerializeField, Min(0f)] private float occluderCastRadius = 0.3f;

        [Tooltip("한 프레임에 수집할 수 있는 최대 가림 오브젝트 수")]
        [SerializeField, Min(1)] private int maxOccluderHits = 16;

        private readonly HashSet<CameraCutoutTarget> targets = new();
        private readonly HashSet<Renderer> registeredRenderers = new();
        private readonly HashSet<Renderer> activeOccluders = new();
        private readonly HashSet<Renderer> previousOccluders = new();
        private MaterialPropertyBlock propertyBlock;

        private RaycastHit[] occluderHits;
        private Vector3 currentCutoutPlayerCenter;
        private Vector3 currentCutoutLookCenter;
        private float currentPlayerCutoutRadius;
        private float currentLookCutoutRadius;
        private float currentCutoutFeather;
        private float currentHeightCutoutFeather;
        private float currentFullObjectHeightBlend;
        private float fullObjectCutoutProgress;

        private void Awake()
        {
            ResolveInputCameraIfNeeded();
            propertyBlock = new MaterialPropertyBlock();
            occluderHits = new RaycastHit[maxOccluderHits];
        }

        private void OnEnable()
        {
            EventBus.Subscribe<CameraCutoutTargetRegisteredEvent>(OnTargetRegistered);
            EventBus.Subscribe<CameraCutoutTargetUnregisteredEvent>(OnTargetUnregistered);
            EventBus.Subscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Subscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);
            EventBus.Subscribe<OwnerPlayerCameraRegisteredEvent>(OnOwnerPlayerCameraRegistered);
            EventBus.Subscribe<OwnerPlayerCameraUnregisteredEvent>(OnOwnerPlayerCameraUnregistered);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CameraCutoutTargetRegisteredEvent>(OnTargetRegistered);
            EventBus.Unsubscribe<CameraCutoutTargetUnregisteredEvent>(OnTargetUnregistered);
            EventBus.Unsubscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Unsubscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);
            EventBus.Unsubscribe<OwnerPlayerCameraRegisteredEvent>(OnOwnerPlayerCameraRegistered);
            EventBus.Unsubscribe<OwnerPlayerCameraUnregisteredEvent>(OnOwnerPlayerCameraUnregistered);

            ClearActiveOccluders();
        }

        private void LateUpdate()
        {
            UpdateCutoutShaderState();

            if (useRaycastOccluderTest)
            {
                UpdateRaycastOccluders();
            }
            else
            {
                ClearActiveOccluders();
            }
        }

        /// <summary>
        /// 로컬 Owner 플레이어 루트 Transform을 컷아웃 기준점으로 설정한다.
        /// </summary>
        private void OnOwnerPlayerRootRegistered(OwnerPlayerRootRegisteredEvent e)
        {
            followTarget = e.playerRoot;
        }

        /// <summary>
        /// 현재 사용 중인 로컬 Owner 플레이어 루트가 사라졌을 때 컷아웃 기준점을 정리한다.
        /// </summary>
        private void OnOwnerPlayerRootUnregistered(OwnerPlayerRootUnregisteredEvent e)
        {
            if (followTarget == e.playerRoot)
            {
                followTarget = null;
                ClearActiveOccluders();
            }
        }

        /// <summary>
        /// 로컬 Owner 플레이어 카메라를 마우스 Raycast와 가림 오브젝트 검사 기준 카메라로 설정한다.
        /// </summary>
        private void OnOwnerPlayerCameraRegistered(OwnerPlayerCameraRegisteredEvent e)
        {
            inputCamera = e.playerCamera;
        }

        /// <summary>
        /// 현재 사용 중인 로컬 Owner 플레이어 카메라가 해제될 때 카메라 참조와 컷아웃 상태를 정리한다.
        /// </summary>
        private void OnOwnerPlayerCameraUnregistered(OwnerPlayerCameraUnregisteredEvent e)
        {
            if (inputCamera == e.playerCamera)
            {
                inputCamera = null;
                ClearActiveOccluders();
            }
        }

        /// <summary>
        /// 컷아웃 적용 후보 오브젝트가 활성화되면 해당 Renderer들을 등록한다.
        /// </summary>
        private void OnTargetRegistered(CameraCutoutTargetRegisteredEvent e)
        {
            if (e.target == null || !targets.Add(e.target))
                return;

            RegisterTargetRenderers(e.target);
        }

        /// <summary>
        /// 컷아웃 적용 후보 오브젝트가 비활성화되면 해당 Renderer들을 등록 목록에서 제거한다.
        /// </summary>
        private void OnTargetUnregistered(CameraCutoutTargetUnregisteredEvent e)
        {
            if (e.target == null || !targets.Remove(e.target))
                return;

            UnregisterTargetRenderers(e.target);
        }

        /// <summary>
        /// 현재 플레이어와 마우스 월드 위치를 기준으로 Renderer별 MaterialPropertyBlock에 넣을 컷아웃 값을 갱신한다.
        /// </summary>
        private void UpdateCutoutShaderState()
        {
            if (followTarget == null)
                return;

            UpdateFullObjectCutoutProgress();

            Vector3 playerCenter = followTarget.position;
            Vector3 lookCenter = playerCenter;

            if (TryResolveMouseWorldPosition(out Vector3 mouseWorldPosition))
                lookCenter = mouseWorldPosition;

            currentCutoutPlayerCenter = playerCenter;
            currentCutoutLookCenter = lookCenter;
            currentPlayerCutoutRadius = Mathf.Lerp(playerCutoutRadius, fullObjectCutoutExpandedRadius, fullObjectCutoutProgress);
            currentLookCutoutRadius = Mathf.Lerp(lookCutoutRadius, fullObjectCutoutExpandedRadius, fullObjectCutoutProgress);
            currentCutoutFeather = Mathf.Min(cutoutFeather, currentPlayerCutoutRadius, currentLookCutoutRadius);
            currentHeightCutoutFeather = Mathf.Min(heightCutoutFeather, cutoutHeightOffset);
            currentFullObjectHeightBlend = useFullObjectHeightCutout && Mathf.Approximately(fullObjectCutoutProgress, 1f) ? 1f : 0f;
        }

        /// <summary>
        /// 전체 높이 컷아웃 토글 상태에 맞춰 원형 확장과 전체 전환에 사용할 진행도를 갱신한다.
        /// </summary>
        private void UpdateFullObjectCutoutProgress()
        {
            float targetProgress = useFullObjectHeightCutout ? 1f : 0f;
            float maxDelta = Time.deltaTime / fullObjectCutoutTransitionTime;

            fullObjectCutoutProgress = Mathf.MoveTowards(
                fullObjectCutoutProgress,
                targetProgress,
                maxDelta);
        }

        /// <summary>
        /// 현재 마우스 화면 위치를 지면 Raycast로 월드 좌표에 투영한다.
        /// </summary>
        private bool TryResolveMouseWorldPosition(out Vector3 mouseWorldPosition)
        {
            mouseWorldPosition = default;

            ResolveInputCameraIfNeeded();
            if (inputCamera == null)
                return false;

            if (Mouse.current == null)
                return false;

            Vector2 mouseScreenPosition = Mouse.current.position.ReadValue();
            Ray ray = inputCamera.ScreenPointToRay(mouseScreenPosition);

            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    aimRayDistance,
                    aimMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            mouseWorldPosition = hit.point;
            return true;
        }

        /// <summary>
        /// 플레이어 머리 위 지점에서 카메라 위치 방향으로 시야선을 검사하고,
        /// 해당 시야선에 걸린 CameraCutoutTarget의 Renderer에만 컷아웃 적용 플래그를 켠다.
        /// </summary>
        private void UpdateRaycastOccluders()
        {
            ResolveInputCameraIfNeeded();

            if (inputCamera == null || followTarget == null)
                return;

            previousOccluders.Clear();

            foreach (Renderer renderer in activeOccluders)
            {
                if (renderer != null)
                    previousOccluders.Add(renderer);
            }

            activeOccluders.Clear();

            Vector3 origin = GetOccluderCastTargetPosition();
            Vector3 target = inputCamera.transform.position;
            Vector3 direction = target - origin;
            float distance = direction.magnitude;

            if (distance <= 0.001f)
                return;

            direction /= distance;

            int hitCount = CastOccluders(origin, direction, distance);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = occluderHits[i];
                CameraCutoutTarget cutoutTarget = hit.collider.GetComponentInParent<CameraCutoutTarget>();
                if (cutoutTarget == null || !targets.Contains(cutoutTarget))
                    continue;

                SetTargetCutout(cutoutTarget, true);
            }

            foreach (Renderer renderer in previousOccluders)
            {
                SetRendererCutout(renderer, false);
            }
        }

        private Vector3 GetOccluderCastTargetPosition()
        {
            return followTarget.position + Vector3.up * occluderTargetHeightOffset;
        }

        private int CastOccluders(Vector3 origin, Vector3 direction, float distance)
        {
            if (occluderCastRadius > 0f)
            {
                return Physics.SphereCastNonAlloc(
                    origin,
                    occluderCastRadius,
                    direction,
                    occluderHits,
                    distance,
                    occluderMask,
                    QueryTriggerInteraction.Ignore);
            }

            return Physics.RaycastNonAlloc(
                origin,
                direction,
                occluderHits,
                distance,
                occluderMask,
                QueryTriggerInteraction.Ignore);
        }

        private void SetTargetCutout(CameraCutoutTarget target, bool active)
        {
            Renderer[] renderers = target.Renderers;
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !registeredRenderers.Contains(renderer))
                    continue;

                if (active)
                {
                    activeOccluders.Add(renderer);
                    previousOccluders.Remove(renderer);
                }
                else
                {
                    activeOccluders.Remove(renderer);
                    previousOccluders.Remove(renderer);
                }

                SetRendererCutout(renderer, active);
            }
        }

        private void RegisterTargetRenderers(CameraCutoutTarget target)
        {
            Renderer[] renderers = target.Renderers;
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                registeredRenderers.Add(renderer);
                SetRendererCutout(renderer, false);
            }
        }

        private void UnregisterTargetRenderers(CameraCutoutTarget target)
        {
            Renderer[] renderers = target.Renderers;
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                SetRendererCutout(renderer, false);
                registeredRenderers.Remove(renderer);
                activeOccluders.Remove(renderer);
                previousOccluders.Remove(renderer);
            }
        }

        /// <summary>
        /// 현재 컷아웃 적용 중인 Renderer들을 모두 원래 상태로 돌린다.
        /// </summary>
        private void ClearActiveOccluders()
        {
            foreach (Renderer renderer in activeOccluders)
            {
                SetRendererCutout(renderer, false);
            }

            activeOccluders.Clear();
            previousOccluders.Clear();
        }

        /// <summary>
        /// Renderer별 MaterialPropertyBlock에 컷아웃 중심, 반경, 적용 여부를 기록한다.
        /// </summary>
        private void SetRendererCutout(Renderer renderer, bool active)
        {
            if (renderer == null)
                return;

            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);

            // Shader Graph의 Blackboard 프로퍼티는 머티리얼 프로퍼티로 생성되므로,
            // 전역 셰이더 값만으로는 머티리얼 값이 우선되어 반영되지 않을 수 있다.
            propertyBlock.SetVector(CutoutPlayerCenterId, currentCutoutPlayerCenter);
            propertyBlock.SetVector(CutoutLookCenterId, currentCutoutLookCenter);
            propertyBlock.SetFloat(CutoutPlayerRadiusId, currentPlayerCutoutRadius);
            propertyBlock.SetFloat(CutoutLookRadiusId, currentLookCutoutRadius);
            propertyBlock.SetFloat(CutoutMinYId, cutoutHeightOffset);
            propertyBlock.SetFloat(CutoutFeatherId, currentCutoutFeather);
            propertyBlock.SetFloat(CutoutHeightFeatherId, currentHeightCutoutFeather);
            propertyBlock.SetFloat(CutoutUseFullObjectHeightId, currentFullObjectHeightBlend);
            propertyBlock.SetFloat(UseCameraCutoutId, active ? 1f : 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private void ResolveInputCameraIfNeeded()
        {
            if (inputCamera != null)
                return;

            inputCamera = Camera.main;
        }
    }
}

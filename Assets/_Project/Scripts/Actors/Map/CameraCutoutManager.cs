using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 카메라 컷아웃 셰이더에 전역 컷아웃 위치를 전달하고,
    /// 테스트용 레이캐스트 방식으로 카메라와 플레이어 사이를 가리는 오브젝트에 컷아웃 적용 여부를 설정한다.
    /// </summary>
    public class CameraCutoutManager : MonoBehaviour
    {
        private static readonly int CutoutPlayerCenterId = Shader.PropertyToID("_CutoutPlayerCenter");
        private static readonly int CutoutLookCenterId = Shader.PropertyToID("_CutoutLookCenter");
        private static readonly int CutoutPlayerRadiusId = Shader.PropertyToID("_CutoutPlayerRadius");
        private static readonly int CutoutLookRadiusId = Shader.PropertyToID("_CutoutLookRadius");
        private static readonly int CutoutMinYId = Shader.PropertyToID("_CutoutMinY");
        private static readonly int UseCameraCutoutId = Shader.PropertyToID("_UseCameraCutout");
        private static readonly int PlayerInsideId = Shader.PropertyToID("_PlayerInside");

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

        [Header("====레이캐스트 테스트====")]
        [Tooltip("참이면 카메라와 플레이어 사이를 검사해 맞은 오브젝트에만 컷아웃을 적용한다")]
        [SerializeField] private bool useRaycastOccluderTest = true;

        [Tooltip("카메라와 플레이어 사이에서 시야를 가릴 수 있는 오브젝트 레이어")]
        [SerializeField] private LayerMask occluderMask = ~0;

        [Tooltip("Raycast 대신 SphereCast를 사용할 반경. 0이면 Raycast처럼 동작한다")]
        [SerializeField, Min(0f)] private float occluderCastRadius = 0.3f;

        [Tooltip("한 프레임에 수집할 수 있는 최대 가림 오브젝트 수")]
        [SerializeField, Min(1)] private int maxOccluderHits = 16;

        private readonly HashSet<CameraCutoutTarget> targets = new();
        private readonly HashSet<Renderer> registeredRenderers = new();
        private readonly HashSet<Renderer> activeOccluders = new();
        private readonly HashSet<Renderer> previousOccluders = new();
        private readonly MaterialPropertyBlock propertyBlock = new();

        private RaycastHit[] occluderHits;

        private void Awake()
        {
            ResolveInputCameraIfNeeded();
            occluderHits = new RaycastHit[maxOccluderHits];
        }

        private void OnEnable()
        {
            EventBus.Subscribe<CameraCutoutTargetRegisteredEvent>(OnTargetRegistered);
            EventBus.Subscribe<CameraCutoutTargetUnregisteredEvent>(OnTargetUnregistered);
            EventBus.Subscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Subscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CameraCutoutTargetRegisteredEvent>(OnTargetRegistered);
            EventBus.Unsubscribe<CameraCutoutTargetUnregisteredEvent>(OnTargetUnregistered);
            EventBus.Unsubscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Unsubscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);

            ClearActiveOccluders();
        }

        private void LateUpdate()
        {
            UpdateShaderGlobals();

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
        /// 셰이더가 플레이어와 마우스 월드 위치 기준 컷아웃을 계산할 수 있도록 전역 프로퍼티를 갱신한다.
        /// </summary>
        private void UpdateShaderGlobals()
        {
            if (followTarget == null)
                return;

            Vector3 playerCenter = followTarget.position;
            Vector3 lookCenter = playerCenter;

            if (TryResolveMouseWorldPosition(out Vector3 mouseWorldPosition))
                lookCenter = mouseWorldPosition;

            Shader.SetGlobalVector(CutoutPlayerCenterId, playerCenter);
            Shader.SetGlobalVector(CutoutLookCenterId, lookCenter);
            Shader.SetGlobalFloat(CutoutPlayerRadiusId, playerCutoutRadius);
            Shader.SetGlobalFloat(CutoutLookRadiusId, lookCutoutRadius);

            // Generic_Basic 그래프는 CutoutPlayerCenter.y + CutoutMinY로 높이 기준을 계산한다.
            Shader.SetGlobalFloat(CutoutMinYId, cutoutHeightOffset);
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

            Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);

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
        /// 카메라와 플레이어 사이에 있는 등록된 Renderer를 찾고, 해당 Renderer에만 컷아웃 적용 플래그를 켠다.
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

            Vector3 origin = inputCamera.transform.position;
            Vector3 target = followTarget.position + Vector3.up * cutoutHeightOffset;
            Vector3 direction = target - origin;
            float distance = direction.magnitude;

            if (distance <= 0.001f)
                return;

            direction /= distance;

            int hitCount = CastOccluders(origin, direction, distance);

            for (int i = 0; i < hitCount; i++)
            {
                Renderer renderer = occluderHits[i].collider.GetComponentInParent<Renderer>();
                if (renderer == null || !registeredRenderers.Contains(renderer))
                    continue;

                activeOccluders.Add(renderer);
                SetRendererCutout(renderer, true);
                previousOccluders.Remove(renderer);
            }

            foreach (Renderer renderer in previousOccluders)
            {
                SetRendererCutout(renderer, false);
            }
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
        /// Renderer별 MaterialPropertyBlock에 컷아웃 적용 여부와 내부 진입 플래그를 기록한다.
        /// </summary>
        private void SetRendererCutout(Renderer renderer, bool active)
        {
            if (renderer == null)
                return;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(UseCameraCutoutId, active ? 1f : 0f);

            // Generic_Basic에서 _PlayerInside는 마우스 위치 기반 컷아웃 반경 활성화에 사용된다.
            propertyBlock.SetFloat(PlayerInsideId, active ? 1f : 0f);
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

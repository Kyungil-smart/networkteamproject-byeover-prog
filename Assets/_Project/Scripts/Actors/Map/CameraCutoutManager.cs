using System.Collections.Generic;
using UnityEngine;
using DeadZone.Core;

namespace DeadZone.Actors
{
    public class CameraCutoutManager : MonoBehaviour
    {
        private static readonly int CutoutPlayerCenterId = Shader.PropertyToID("_CutoutPlayerCenter");
        private static readonly int CutoutLookCenterId = Shader.PropertyToID("_CutoutLookCenter");
        private static readonly int CutoutPlayerRadiusId = Shader.PropertyToID("_CutoutPlayerRadius");
        private static readonly int CutoutLookRadiusId = Shader.PropertyToID("_CutoutLookRadius");
        private static readonly int CutoutMinYId = Shader.PropertyToID("_CutoutMinY");
        private static readonly int PlayerInsideId = Shader.PropertyToID("_PlayerInside");
        private static readonly int UseCameraCutoutId = Shader.PropertyToID("_UseCameraCutout");

        [Header("====추적 대상====")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private FPSController fpsController;
        [SerializeField] private Camera targetCamera;

        [Header("====컷아웃 범위====")]
        [SerializeField, Min(0f)] private float cutoutHeightOffset = 1f;
        [SerializeField, Min(0f)] private float playerRadius = 3f;
        [SerializeField, Min(0f)] private float lookDistance = 4f;
        [SerializeField, Min(0f)] private float lookRadius = 2.5f;

        [Header("====레이캐스트 테스트====")]
        [Tooltip("참이면 카메라와 플레이어 사이를 Raycast로 검사해 맞은 오브젝트에만 컷아웃을 적용한다.")]
        [SerializeField] private bool useRaycastTest = true;

        [Tooltip("카메라와 플레이어 사이에서 시야를 가릴 수 있는 오브젝트 레이어")]
        [SerializeField] private LayerMask occluderMask = ~0;

        [Tooltip("Raycast 대신 SphereCast를 사용할 반경. 0이면 Raycast처럼 동작한다.")]
        [SerializeField, Min(0f)] private float occluderCastRadius = 0.3f;

        [Tooltip("한 프레임에 수집할 수 있는 최대 가림 오브젝트 수")]
        [SerializeField, Min(1)] private int maxOccluderHits = 16;

        private readonly HashSet<Renderer> activeOccluders = new();
        private readonly HashSet<Renderer> previousOccluders = new();
        private readonly MaterialPropertyBlock propertyBlock = new();
        private RaycastHit[] occluderHits;
        private Vector3 lastValidLookDirection = Vector3.forward;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            occluderHits = new RaycastHit[maxOccluderHits];
        }

        private void LateUpdate()
        {
            UpdateShaderGlobals();

            if (useRaycastTest)
            {
                UpdateRaycastOccluders();
            }
            else
            {
                ClearActiveOccluders();
            }
        }

        /// <summary>
        /// 셰이더가 컷아웃 위치와 반경을 계산할 수 있도록 전역 프로퍼티를 갱신한다.
        /// </summary>
        private void UpdateShaderGlobals()
        {
            if (followTarget == null)
                return;

            Vector3 lookDirection = ResolveLookDirection();

            Vector3 playerCenter = followTarget.position;
            Vector3 lookCenter = followTarget.position + lookDirection * lookDistance;

            Shader.SetGlobalVector(CutoutPlayerCenterId, playerCenter);
            Shader.SetGlobalVector(CutoutLookCenterId, lookCenter);
            Shader.SetGlobalFloat(CutoutPlayerRadiusId, playerRadius);
            Shader.SetGlobalFloat(CutoutLookRadiusId, lookRadius);

            // Generic_Basic 그래프는 CutoutPlayerCenter.y + CutoutMinY를 기준으로 높이를 판정한다.
            Shader.SetGlobalFloat(CutoutMinYId, cutoutHeightOffset);
        }

        /// <summary>
        /// 카메라와 플레이어 사이에 있는 오브젝트를 찾고, 해당 Renderer에만 컷아웃 적용 플래그를 켠다.
        /// </summary>
        private void UpdateRaycastOccluders()
        {
            if (targetCamera == null || followTarget == null)
                return;

            previousOccluders.Clear();

            foreach (Renderer renderer in activeOccluders)
            {
                if (renderer != null)
                    previousOccluders.Add(renderer);
            }

            activeOccluders.Clear();

            Vector3 origin = targetCamera.transform.position;
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
                if (renderer == null)
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

            // Generic_Basic에서 _PlayerInside는 마우스 방향 컷아웃 반경 활성화에 사용된다.
            propertyBlock.SetFloat(PlayerInsideId, active ? 1f : 0f);

            renderer.SetPropertyBlock(propertyBlock);
        }

        private Vector3 ResolveLookDirection()
        {
            Vector2 lookInput = fpsController != null
                ? fpsController.LookInput
                : Vector2.zero;

            Vector3 lookDirection = new Vector3(lookInput.x, 0f, lookInput.y);

            if (lookDirection.sqrMagnitude < 0.001f)
                return lastValidLookDirection;

            lastValidLookDirection = lookDirection.normalized;
            return lastValidLookDirection;
        }
    }
}

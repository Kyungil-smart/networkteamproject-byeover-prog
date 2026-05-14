using System.Collections.Generic;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 공유 시야 마스크 렌더링과 시야 마스크를 사용하는 Renderer 등록 정보를 관리한다.
    /// 플레이어 루트는 이벤트로 수신하고, 각 플레이어 루트 하위에 FOVMesh 프리팹을 생성해 VisionMaskCamera의 RenderTexture에 찍히도록 한다.
    /// </summary>
    public class VisionMaskManager : MonoBehaviour
    {
        private static readonly int VisionMaskTextureId = Shader.PropertyToID("_VisionMaskTexture");
        private static readonly int VisionMaskWorldCenterId = Shader.PropertyToID("_VisionMaskWorldCenter");
        private static readonly int VisionMaskWorldSizeId = Shader.PropertyToID("_VisionMaskWorldSize");
        private static readonly int UseVisionMaskVisibilityId = Shader.PropertyToID("_UseVisionMaskVisibility");
        private static readonly int VisionMaskClipThresholdId = Shader.PropertyToID("_VisionMaskClipThreshold");
        private static readonly int VisionMaskHiddenAlphaId = Shader.PropertyToID("_VisionMaskHiddenAlpha");
        private static readonly int DarknessAlphaId = Shader.PropertyToID("_DarknessAlpha");

        [Header("참조")]
        [Tooltip("시야 마스크만 렌더링할 씬 내 단일 카메라")]
        [SerializeField] private Camera visionMaskCamera;

        [Tooltip("VisionMaskCamera가 렌더링할 대상 RenderTexture")]
        [SerializeField] private RenderTexture visionMaskTexture;

        [Tooltip("플레이어 루트 하위에 생성할 FOVMesh 프리팹")]
        [SerializeField] private FOVMesh fovMeshPrefab;

        [Header("추적")]
        [Tooltip("VisionMaskCamera가 현재 따라갈 대상\n처음에는 Owner 플레이어 루트로 설정하고, 사망/관전 시 외부에서 교체할 수 있다")]
        [SerializeField] private Transform target;

        [Tooltip("VisionMaskCamera가 바라볼 고정 월드 높이")]
        [SerializeField] private float cameraHeight = 80f;

        [Tooltip("VisionMaskCamera가 RenderTexture에 담을 월드 XZ 크기")]
        [SerializeField] private Vector2 maskWorldSize = new(50f, 50f);

        [Header("어둠 Plane")]
        [Tooltip("VisionMaskTexture를 읽어 시야 밖을 어둡게 덮는 테스트용 Plane")]
        [SerializeField] private Transform darknessPlane;

        [Tooltip("darknessPlane의 머티리얼 프로퍼티를 전달할 Renderer\n비워두면 darknessPlane에서 자동으로 찾는다")]
        [SerializeField] private Renderer darknessPlaneRenderer;

        [Tooltip("참이면 darknessPlane의 위치를 추적 대상 XZ에 맞춘다")]
        [SerializeField] private bool syncDarknessPlanePosition = true;

        [Tooltip("darknessPlane을 추적 대상 루트보다 띄울 높이")]
        [SerializeField] private float darknessPlaneHeightOffset = 0.1f;

        [Tooltip("darknessPlane Mesh의 기본 한 변 크기\nUnity 기본 Plane은 10, Quad는 1을 사용한다")]
        [SerializeField, Min(0.0001f)] private float darknessPlaneBaseSize = 10f;

        [Tooltip("VisionMaskCamera 범위보다 어둠 Plane을 크게 만들 배율\n카메라 화면 전체를 덮지 못할 때 값을 키운다")]
        [SerializeField, Min(1f)] private float darknessPlaneSizeMultiplier = 2f;

        [Tooltip("어둠 Plane이 적용할 최대 어둠 알파")]
        [SerializeField, Range(0f, 1f)] private float darknessAlpha = 0.75f;

        [Header("VisionMask 대상 Renderer")]
        [Tooltip("등록된 Renderer가 VisionMaskTexture를 기준으로 표시 여부를 판정하도록 활성화한다")]
        [SerializeField] private bool enableVisionMaskVisibility = true;

        [Tooltip("VisionMaskTexture 샘플 값이 이 값 이상일 때 시야 안으로 판정한다")]
        [SerializeField, Range(0f, 1f)] private float visionMaskClipThreshold = 0.5f;

        [Tooltip("시야 밖 Renderer를 완전히 숨기지 않고 남길 알파\n현재 Generic_Basic 그래프에서 연결되어 있을 때만 적용된다")]
        [SerializeField, Range(0f, 1f)] private float visionMaskHiddenAlpha = 0f;

        [Header("렌더링")]
        [Tooltip("활성화 시 VisionMaskCamera의 RenderTexture, Clear, Orthographic 설정을 코드에서 맞춘다")]
        [SerializeField] private bool configureCameraAutomatically = true;

        [Tooltip("VisionMaskCamera가 렌더링할 레이어")]
        [SerializeField] private LayerMask visionMaskCullingMask = ~0;

        [Tooltip("참이면 VisionMaskCamera를 비활성화한 상태로 두고 LateUpdate에서 직접 Render를 호출한다")]
        [SerializeField] private bool renderManually = false;

        [Tooltip("생성된 FOVMesh 프리팹 하위 오브젝트의 레이어를 강제로 지정한다")]
        [SerializeField] private bool overrideSpawnedFovLayer = false;

        [Tooltip("overrideSpawnedFovLayer가 참일 때 적용할 VisionMask 전용 레이어")]
        [SerializeField, Range(0, 31)] private int spawnedFovLayer = 0;

        private Transform ownerPlayerRoot;
        private bool lastRenderManually;
        private MaterialPropertyBlock darknessPlanePropertyBlock;
        private MaterialPropertyBlock visionMaskRendererPropertyBlock;
        private readonly Dictionary<Transform, FOVMesh> fovMeshesByPlayerRoot = new();
        private readonly HashSet<Renderer> visionMaskRenderers = new();

        private void Awake()
        {
            ResolveDarknessPlaneRenderer();
            darknessPlanePropertyBlock = new MaterialPropertyBlock();
            ConfigureVisionMaskRenderingBounds();
            lastRenderManually = renderManually;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerRootRegisteredEvent>(OnPlayerRootRegistered);
            EventBus.Subscribe<PlayerRootUnregisteredEvent>(OnPlayerRootUnregistered);
            EventBus.Subscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Subscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);
            EventBus.Subscribe<VisionMaskRenderersRegisteredEvent>(OnVisionMaskRenderersRegistered);
            EventBus.Subscribe<VisionMaskRenderersUnregisteredEvent>(OnVisionMaskRenderersUnregistered);

            ConfigureVisionMaskRenderingBounds();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerRootRegisteredEvent>(OnPlayerRootRegistered);
            EventBus.Unsubscribe<PlayerRootUnregisteredEvent>(OnPlayerRootUnregistered);
            EventBus.Unsubscribe<OwnerPlayerRootRegisteredEvent>(OnOwnerPlayerRootRegistered);
            EventBus.Unsubscribe<OwnerPlayerRootUnregisteredEvent>(OnOwnerPlayerRootUnregistered);
            EventBus.Unsubscribe<VisionMaskRenderersRegisteredEvent>(OnVisionMaskRenderersRegistered);
            EventBus.Unsubscribe<VisionMaskRenderersUnregisteredEvent>(OnVisionMaskRenderersUnregistered);

            ClearVisionMaskRendererProperties();
            visionMaskRenderers.Clear();
            ClearSpawnedFovMeshes();
        }

        private void LateUpdate()
        {
            if (target == null || visionMaskCamera == null)
                return;

            UpdateRenderModeIfChanged();
            UpdateVisionMaskCameraTransform();
            UpdateDarknessPlanePosition();
            ApplyDynamicVisionMaskGlobals();
            RenderVisionMaskIfNeeded();
        }

        private void OnValidate()
        {
            ResolveDarknessPlaneRenderer();
            ConfigureVisionMaskRenderingBounds();
        }

        /// <summary>
        /// 외부 사망/관전 시스템이 VisionMaskCamera의 추적 대상을 교체할 때 사용한다.
        /// 등록된 플레이어 루트가 아니더라도 테스트 편의를 위해 Transform 참조 자체는 허용한다.
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        /// <summary>
        /// 공유 시야 대상 플레이어가 스폰되면 해당 루트 하위에 FOVMesh 프리팹을 생성한다.
        /// FOVMesh가 플레이어의 자식이 되므로 위치와 기본 회전은 부모 Transform을 자연스럽게 따라간다.
        /// </summary>
        private void OnPlayerRootRegistered(PlayerRootRegisteredEvent e)
        {
            if (e.playerRoot == null || fovMeshPrefab == null)
                return;

            if (fovMeshesByPlayerRoot.ContainsKey(e.playerRoot))
                return;

            FOVMesh fovMesh = Instantiate(fovMeshPrefab, e.playerRoot);
            fovMesh.transform.localPosition = Vector3.zero;
            fovMesh.transform.localRotation = Quaternion.identity;
            fovMesh.transform.localScale = Vector3.one;

            if (overrideSpawnedFovLayer)
                SetLayerRecursively(fovMesh.gameObject, spawnedFovLayer);

            fovMeshesByPlayerRoot.Add(e.playerRoot, fovMesh);
        }

        /// <summary>
        /// 공유 시야 대상 플레이어가 사라지면 생성했던 FOVMesh를 제거하고,
        /// 현재 추적 대상이었다면 Owner 또는 남은 플레이어 루트로 대체한다.
        /// </summary>
        private void OnPlayerRootUnregistered(PlayerRootUnregisteredEvent e)
        {
            if (e.playerRoot == null)
                return;

            if (fovMeshesByPlayerRoot.TryGetValue(e.playerRoot, out FOVMesh fovMesh))
            {
                if (fovMesh != null)
                    Destroy(fovMesh.gameObject);

                fovMeshesByPlayerRoot.Remove(e.playerRoot);
            }

            if (target == e.playerRoot)
                target = ResolveFallbackTarget(e.playerRoot);
        }

        /// <summary>
        /// 로컬 Owner 플레이어 루트가 등록되면 기본 추적 대상으로 설정한다.
        /// 이후 사망/관전 시스템이 SetTarget을 호출하면 다른 아군 루트로 교체할 수 있다.
        /// </summary>
        private void OnOwnerPlayerRootRegistered(OwnerPlayerRootRegisteredEvent e)
        {
            ownerPlayerRoot = e.playerRoot;

            if (target == null)
                target = ownerPlayerRoot;
        }

        /// <summary>
        /// 로컬 Owner 플레이어 루트가 해제되면 Owner 참조를 비우고,
        /// 현재 추적 중이었다면 남은 플레이어 루트로 임시 대체한다.
        /// </summary>
        private void OnOwnerPlayerRootUnregistered(OwnerPlayerRootUnregisteredEvent e)
        {
            if (ownerPlayerRoot == e.playerRoot)
                ownerPlayerRoot = null;

            if (target == e.playerRoot)
                target = ResolveFallbackTarget(e.playerRoot);
        }

        /// <summary>
        /// 적 CharacterVisual, EnemyWeaponVisual처럼 시야 마스크에 의해 표시 여부가 제어될 Renderer들을 등록한다.
        /// 등록 직후 Renderer별 MaterialPropertyBlock에 VisionMask 적용 스위치와 판정 값을 전달한다.
        /// </summary>
        private void OnVisionMaskRenderersRegistered(VisionMaskRenderersRegisteredEvent e)
        {
            RegisterVisionMaskRenderers(e.renderers);
        }

        /// <summary>
        /// 비활성화되거나 제거된 시야 마스크 대상 Renderer들을 캐시에서 해제한다.
        /// 등록 주체가 OnDisable에서 이벤트를 발행하므로, 여기서는 참조 제거만 담당한다.
        /// </summary>
        private void OnVisionMaskRenderersUnregistered(VisionMaskRenderersUnregisteredEvent e)
        {
            UnregisterVisionMaskRenderers(e.renderers);
        }

        /// <summary>
        /// VisionMask 대상 Renderer 배열에서 유효한 Renderer만 캐시에 추가한다.
        /// 동일 Renderer가 여러 등록 주체를 통해 들어와도 HashSet으로 중복 등록을 방지하고, 등록된 Renderer에는 즉시 MPB 값을 적용한다.
        /// </summary>
        private void RegisterVisionMaskRenderers(Renderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                if (targetRenderer == null)
                    continue;

                visionMaskRenderers.Add(targetRenderer);
                ApplyVisionMaskRendererProperties(targetRenderer);
            }
        }

        /// <summary>
        /// VisionMask 대상 Renderer 배열에서 유효한 Renderer만 캐시에서 제거한다.
        /// 해제 시점에는 VisionMask 적용 스위치를 0으로 되돌려 일반 렌더링 상태로 복구한다.
        /// </summary>
        private void UnregisterVisionMaskRenderers(Renderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                if (targetRenderer == null)
                    continue;

                ClearVisionMaskRendererProperties(targetRenderer);
                visionMaskRenderers.Remove(targetRenderer);
            }
        }

        /// <summary>
        /// VisionMaskCamera가 RenderTexture에 마스크를 기록할 수 있도록 기본 렌더 설정을 맞춘다.
        /// 카메라는 XZ 평면을 위에서 내려다보는 Orthographic 카메라로 사용한다.
        /// 매 프레임 변하지 않는 값만 다루므로 초기화, 활성화, 인스펙터 값 변경 시점에만 호출한다.
        /// </summary>
        private void ConfigureVisionMaskRenderingBounds()
        {
            if (configureCameraAutomatically && visionMaskCamera != null)
            {
                visionMaskCamera.orthographic = true;
                visionMaskCamera.orthographicSize = Mathf.Max(0.01f, maskWorldSize.y * 0.5f);
                visionMaskCamera.aspect = Mathf.Max(0.01f, maskWorldSize.x) / Mathf.Max(0.01f, maskWorldSize.y);
                visionMaskCamera.targetTexture = visionMaskTexture;
                visionMaskCamera.cullingMask = visionMaskCullingMask;
                visionMaskCamera.clearFlags = CameraClearFlags.SolidColor;
                visionMaskCamera.backgroundColor = Color.black;

                if (renderManually)
                {
                    visionMaskCamera.enabled = false;
                }
                else
                {
                    visionMaskCamera.enabled = true;
                }

                lastRenderManually = renderManually;
            }

            SyncDarknessPlaneRenderingBounds();
            ApplyStaticVisionMaskGlobals();
            ApplyDynamicVisionMaskGlobals();
            ApplyStaticDarknessPlaneProperties();
            ApplyVisionMaskRendererProperties();
        }

        /// <summary>
        /// 추적 대상의 XZ 좌표를 기준으로 VisionMaskCamera 위치를 갱신한다.
        /// 플레이어 카메라는 조준 오프셋이 있으므로, 마스크 중심은 플레이어 루트 기준으로 유지한다.
        /// </summary>
        private void UpdateVisionMaskCameraTransform()
        {
            Vector3 targetPosition = target.position;
            visionMaskCamera.transform.SetPositionAndRotation(
                new Vector3(targetPosition.x, cameraHeight, targetPosition.z),
                Quaternion.Euler(90f, 0f, 0f));
        }

        /// <summary>
        /// 테스트용 어둠 Plane의 크기와 회전을 VisionMaskCamera가 찍는 월드 범위와 맞춘다.
        /// 크기와 회전은 매 프레임 변하지 않으므로 초기화나 마스크 범위 변경 시점에만 호출한다.
        /// </summary>
        private void SyncDarknessPlaneRenderingBounds()
        {
            if (darknessPlane == null)
                return;

            darknessPlane.rotation = Quaternion.identity;

            float baseSize = Mathf.Max(0.0001f, darknessPlaneBaseSize);
            darknessPlane.localScale = new Vector3(
                maskWorldSize.x * darknessPlaneSizeMultiplier / baseSize,
                1f,
                maskWorldSize.y * darknessPlaneSizeMultiplier / baseSize);
        }

        /// <summary>
        /// 테스트용 어둠 Plane의 위치만 추적 대상 XZ 기준으로 갱신한다.
        /// Plane 크기와 회전은 SyncDarknessPlaneRenderingBounds에서 별도로 맞춘다.
        /// </summary>
        private void UpdateDarknessPlanePosition()
        {
            if (!syncDarknessPlanePosition || darknessPlane == null || target == null)
                return;

            Vector3 targetPosition = target.position;
            darknessPlane.position = new Vector3(
                targetPosition.x,
                targetPosition.y + darknessPlaneHeightOffset,
                targetPosition.z);
        }

        /// <summary>
        /// darknessPlane 참조만 지정된 경우 같은 오브젝트에서 Renderer를 찾아 프로퍼티 전달 대상을 보정한다.
        /// </summary>
        private void ResolveDarknessPlaneRenderer()
        {
            if (darknessPlaneRenderer != null || darknessPlane == null)
                return;

            darknessPlaneRenderer = darknessPlane.GetComponent<Renderer>();
        }

        /// <summary>
        /// VisionMaskTexture와 마스크 월드 크기를 전역 셰이더 프로퍼티로 전달한다.
        /// FOVDarkness와 Generic_Basic이 같은 값을 읽어야 하므로 MaterialPropertyBlock이 아니라 Shader 전역 값으로 관리한다.
        /// </summary>
        private void ApplyStaticVisionMaskGlobals()
        {
            if (visionMaskTexture != null)
                Shader.SetGlobalTexture(VisionMaskTextureId, visionMaskTexture);

            // FOVDarkness는 X/Y를, Generic_Basic의 적 표시 판정은 X/Z를 사용한다.
            // 두 셰이더가 같은 전역 프로퍼티를 안전하게 읽을 수 있도록 깊이 값을 Y와 Z 양쪽에 넣는다.
            Shader.SetGlobalVector(VisionMaskWorldSizeId, new Vector4(maskWorldSize.x, maskWorldSize.y, maskWorldSize.y, 0f));
        }

        /// <summary>
        /// VisionMaskCamera가 추적 대상을 따라 움직인 뒤 현재 마스크 중심 좌표를 전역 셰이더 프로퍼티로 전달한다.
        /// 시야 판정 대상 Renderer와 어둠 Plane이 같은 중심 좌표를 기준으로 RenderTexture를 샘플링한다.
        /// </summary>
        private void ApplyDynamicVisionMaskGlobals()
        {
            if (visionMaskCamera == null)
                return;

            Shader.SetGlobalVector(VisionMaskWorldCenterId, visionMaskCamera.transform.position);
        }

        /// <summary>
        /// 테스트용 어둠 Plane에만 필요한 어둠 알파를 MaterialPropertyBlock으로 전달한다.
        /// VisionMaskTexture, 마스크 중심, 마스크 크기는 여러 셰이더가 공유하므로 전역 프로퍼티에서 관리한다.
        /// </summary>
        private void ApplyStaticDarknessPlaneProperties()
        {
            if (darknessPlaneRenderer == null)
                return;

            darknessPlanePropertyBlock ??= new MaterialPropertyBlock();
            darknessPlaneRenderer.GetPropertyBlock(darknessPlanePropertyBlock);
            darknessPlanePropertyBlock.SetFloat(DarknessAlphaId, darknessAlpha);
            darknessPlaneRenderer.SetPropertyBlock(darknessPlanePropertyBlock);
        }

        /// <summary>
        /// 현재 등록된 모든 Renderer에 VisionMask 적용 여부와 판정 기준을 MaterialPropertyBlock으로 전달한다.
        /// Texture, 중심 좌표, 월드 크기는 전역 프로퍼티를 사용하고 Renderer별 스위치만 이곳에서 제어한다.
        /// </summary>
        private void ApplyVisionMaskRendererProperties()
        {
            foreach (Renderer targetRenderer in visionMaskRenderers)
            {
                ApplyVisionMaskRendererProperties(targetRenderer);
            }
        }

        /// <summary>
        /// 등록된 Renderer 하나에 VisionMask 적용 여부와 판정 기준을 MaterialPropertyBlock으로 전달한다.
        /// </summary>
        private void ApplyVisionMaskRendererProperties(Renderer targetRenderer)
        {
            if (targetRenderer == null)
                return;

            visionMaskRendererPropertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(visionMaskRendererPropertyBlock);
            visionMaskRendererPropertyBlock.SetFloat(UseVisionMaskVisibilityId, enableVisionMaskVisibility ? 1f : 0f);
            visionMaskRendererPropertyBlock.SetFloat(VisionMaskClipThresholdId, visionMaskClipThreshold);
            visionMaskRendererPropertyBlock.SetFloat(VisionMaskHiddenAlphaId, visionMaskHiddenAlpha);
            targetRenderer.SetPropertyBlock(visionMaskRendererPropertyBlock);
        }

        /// <summary>
        /// VisionMask 대상에서 빠지는 Renderer가 일반 렌더링 상태로 돌아가도록 Renderer별 적용 스위치를 해제한다.
        /// 다른 MPB 값은 유지하고 VisionMask 관련 값만 비활성 상태로 덮는다.
        /// </summary>
        private void ClearVisionMaskRendererProperties(Renderer targetRenderer)
        {
            if (targetRenderer == null)
                return;

            visionMaskRendererPropertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(visionMaskRendererPropertyBlock);
            visionMaskRendererPropertyBlock.SetFloat(UseVisionMaskVisibilityId, 0f);
            targetRenderer.SetPropertyBlock(visionMaskRendererPropertyBlock);
        }

        /// <summary>
        /// 매니저가 비활성화될 때 등록된 모든 Renderer의 VisionMask 적용 스위치를 해제한다.
        /// 씬 전환이나 테스트 중 매니저만 꺼졌을 때 Renderer가 계속 숨김 상태로 남는 것을 막는다.
        /// </summary>
        private void ClearVisionMaskRendererProperties()
        {
            foreach (Renderer targetRenderer in visionMaskRenderers)
            {
                ClearVisionMaskRendererProperties(targetRenderer);
            }
        }

        /// <summary>
        /// 수동 렌더링 모드일 때만 VisionMaskCamera.Render를 호출한다.
        /// 자동 렌더링 모드에서는 Unity 카메라 렌더 루프가 TargetTexture를 매 프레임 갱신한다.
        /// </summary>
        private void RenderVisionMaskIfNeeded()
        {
            if (!renderManually || visionMaskCamera == null)
                return;

            visionMaskCamera.Render();
        }

        /// <summary>
        /// 런타임 디버그 중 수동 렌더링 옵션이 바뀐 경우에만 Camera enabled 상태를 갱신한다.
        /// 정적 카메라 설정 전체를 매 프레임 다시 쓰지 않기 위해 렌더 모드 변경 여부만 검사한다.
        /// </summary>
        private void UpdateRenderModeIfChanged()
        {
            if (lastRenderManually == renderManually)
                return;

            if (visionMaskCamera != null)
                visionMaskCamera.enabled = !renderManually;

            lastRenderManually = renderManually;
        }

        /// <summary>
        /// 현재 추적 대상이 사라졌을 때 사용할 임시 대상을 찾는다.
        /// Owner가 살아있으면 Owner를 우선하고, 그렇지 않으면 등록된 다른 플레이어 루트를 사용한다.
        /// </summary>
        private Transform ResolveFallbackTarget(Transform removedTarget)
        {
            if (ownerPlayerRoot != null && ownerPlayerRoot != removedTarget)
                return ownerPlayerRoot;

            foreach (Transform playerRoot in fovMeshesByPlayerRoot.Keys)
            {
                if (playerRoot != null && playerRoot != removedTarget)
                    return playerRoot;
            }

            return null;
        }

        /// <summary>
        /// 컨트롤러가 비활성화될 때 자신이 생성한 FOVMesh 인스턴스를 정리한다.
        /// 플레이어 하위 오브젝트로 생성되지만, 컨트롤러 비활성화 시에는 렌더 대상이 남지 않도록 직접 제거한다.
        /// </summary>
        private void ClearSpawnedFovMeshes()
        {
            foreach (FOVMesh fovMesh in fovMeshesByPlayerRoot.Values)
            {
                if (fovMesh != null)
                    Destroy(fovMesh.gameObject);
            }

            fovMeshesByPlayerRoot.Clear();
        }

        private void SetLayerRecursively(GameObject targetObject, int layer)
        {
            targetObject.layer = layer;

            Transform targetTransform = targetObject.transform;
            for (int i = 0; i < targetTransform.childCount; i++)
            {
                SetLayerRecursively(targetTransform.GetChild(i).gameObject, layer);
            }
        }
    }
}

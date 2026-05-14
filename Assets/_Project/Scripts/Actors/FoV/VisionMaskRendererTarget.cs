using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// VisionMask 셰이더 제어를 받을 Renderer 목록을 EventBus로 등록하는 컴포넌트다.
    /// 적 CharacterVisual처럼 시각 정보가 모여 있는 오브젝트에 부착해 하위 Renderer들을 자동 등록한다.
    /// </summary>
    public class VisionMaskRendererTarget : MonoBehaviour
    {
        [Tooltip("VisionMask 적용 대상 Renderer 목록\n비어 있으면 하위 Renderer를 자동 수집")]
        [SerializeField] private Renderer[] renderers;

        private bool hasStarted;
        private bool isRegistered;

        private void Awake()
        {
            ResolveRenderersIfNeeded();
        }

        private void OnEnable()
        {
            if (!hasStarted)
                return;

            Register();
        }

        private void Start()
        {
            hasStarted = true;
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void ResolveRenderersIfNeeded()
        {
            if (renderers != null && renderers.Length > 0)
                return;

            // 비주얼 교체시 렌더러 재등록(기존 렌더 해제 및 활성 랜더 등록) 과정이 필요합니다.
            renderers = GetComponentsInChildren<Renderer>(false);
        }

        /// <summary>
        /// VisionMaskManager가 이 Renderer들을 시야 마스크 적용 대상으로 관리할 수 있도록 등록 이벤트를 발행한다.
        /// </summary>
        private void Register()
        {
            if (isRegistered)
                return;

            ResolveRenderersIfNeeded();

            if (renderers == null || renderers.Length == 0)
                return;

            isRegistered = true;
            EventBus.Publish(new VisionMaskRenderersRegisteredEvent
            {
                renderers = renderers
            });
        }

        /// <summary>
        /// 오브젝트가 비활성화될 때 VisionMaskManager가 Renderer 목록에서 제거할 수 있도록 해제 이벤트를 발행한다.
        /// </summary>
        private void Unregister()
        {
            if (!isRegistered)
                return;

            isRegistered = false;
            EventBus.Publish(new VisionMaskRenderersUnregisteredEvent
            {
                renderers = renderers
            });
        }
    }
}

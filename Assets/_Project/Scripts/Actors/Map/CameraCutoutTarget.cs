using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 카메라 컷아웃 셰이더 제어 대상임을 EventBus로 알리는 컴포넌트다.
    /// 실제 셰이더 프로퍼티 적용은 매니저가 담당하고, 이 컴포넌트는 자신의 Renderer 목록과 활성 상태만 등록한다.
    /// </summary>
    public class CameraCutoutTarget : MonoBehaviour
    {
        [Tooltip("컷아웃 적용 여부를 제어할 Renderer 목록\n비어 있으면 하위 Renderer를 자동 수집")]
        [SerializeField] private Renderer[] renderers;

        private bool hasStarted;
        private bool isRegistered;

        public Renderer[] Renderers => renderers;

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

        /// <summary>
        /// 인스펙터에 Renderer가 지정되지 않은 경우 현재 오브젝트와 하위 오브젝트의 Renderer를 수집한다.
        /// </summary>
        private void ResolveRenderersIfNeeded()
        {
            if (renderers != null && renderers.Length > 0)
                return;

            renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// 컷아웃 매니저가 이 오브젝트를 관리 대상으로 추가할 수 있도록 등록 이벤트를 발행한다.
        /// Start 이후 다시 활성화되는 경우 OnEnable에서도 호출된다.
        /// </summary>
        private void Register()
        {
            if (isRegistered)
                return;

            isRegistered = true;
            EventBus.Publish(new CameraCutoutTargetRegisteredEvent
            {
                target = this
            });
        }

        /// <summary>
        /// 오브젝트가 비활성화될 때 컷아웃 매니저가 관리 목록에서 제거할 수 있도록 해제 이벤트를 발행한다.
        /// </summary>
        private void Unregister()
        {
            if (!isRegistered)
                return;

            isRegistered = false;
            EventBus.Publish(new CameraCutoutTargetUnregisteredEvent
            {
                target = this
            });
        }
    }
}

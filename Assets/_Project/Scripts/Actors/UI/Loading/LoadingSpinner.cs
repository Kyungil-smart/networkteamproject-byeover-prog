using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class LoadingSpinner : MonoBehaviour
    {
        [Title("회전 대상")]
        [SerializeField, LabelText("회전시킬 RectTransform")]
        private RectTransform rotateTarget;

        [Title("회전 설정")]
        [SerializeField, LabelText("회전 속도")]
        private float rotateSpeed = -180f;

        [SerializeField, LabelText("비활성 시간 무시")]
        private bool useUnscaledTime = true;

        private void Reset()
        {
            rotateTarget = transform as RectTransform;
        }

        private void Awake()
        {
            if (rotateTarget == null)
                rotateTarget = transform as RectTransform;
        }

        private void Update()
        {
            if (rotateTarget == null)
                return;

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            rotateTarget.Rotate(0f, 0f, rotateSpeed * deltaTime);
        }
    }
}
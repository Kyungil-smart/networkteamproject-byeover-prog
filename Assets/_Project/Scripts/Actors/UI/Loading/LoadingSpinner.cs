using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class LoadingSpinner : MonoBehaviour
    {
        public enum SpinnerPlayMode
        {
            RotateSingleImage,
            SpriteSequence,
            SpriteSequenceAndRotate
        }

        [Title("재생 방식")]
        [SerializeField, LabelText("스피너 재생 방식")]
        private SpinnerPlayMode playMode = SpinnerPlayMode.SpriteSequence;

        [Title("이미지 대상")]
        [SerializeField, LabelText("스피너 이미지")]
        private Image targetImage;

        [SerializeField, LabelText("회전 대상")]
        private RectTransform rotateTarget;

        [Title("프레임 애니메이션")]
        [SerializeField, LabelText("스피너 프레임 목록")]
        private Sprite[] spinnerFrames;

        [SerializeField, LabelText("초당 프레임 수")]
        private float framesPerSecond = 18f;

        [SerializeField, LabelText("비활성화 시 첫 프레임으로 초기화")]
        private bool resetFrameOnDisable = true;

        [Title("회전 설정")]
        [SerializeField, LabelText("회전 속도")]
        private float rotateSpeed = -180f;

        [Title("시간 설정")]
        [SerializeField, LabelText("비활성 시간 무시")]
        private bool useUnscaledTime = true;

        private int currentFrameIndex;
        private float frameTimer;

        private void Reset()
        {
            targetImage = GetComponent<Image>();
            rotateTarget = transform as RectTransform;
        }

        private void Awake()
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();

            if (rotateTarget == null)
                rotateTarget = transform as RectTransform;

            ApplyFirstFrame();
        }

        private void OnEnable()
        {
            frameTimer = 0f;
            currentFrameIndex = 0;
            ApplyFirstFrame();
        }

        private void OnDisable()
        {
            if (resetFrameOnDisable)
            {
                frameTimer = 0f;
                currentFrameIndex = 0;
                ApplyFirstFrame();
            }
        }

        private void Update()
        {
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            switch (playMode)
            {
                case SpinnerPlayMode.RotateSingleImage:
                    UpdateRotation(deltaTime);
                    break;

                case SpinnerPlayMode.SpriteSequence:
                    UpdateSpriteSequence(deltaTime);
                    break;

                case SpinnerPlayMode.SpriteSequenceAndRotate:
                    UpdateSpriteSequence(deltaTime);
                    UpdateRotation(deltaTime);
                    break;
            }
        }

        private void UpdateSpriteSequence(float deltaTime)
        {
            if (targetImage == null)
                return;

            if (spinnerFrames == null || spinnerFrames.Length == 0)
                return;

            if (framesPerSecond <= 0f)
                return;

            frameTimer += deltaTime;

            float frameDuration = 1f / framesPerSecond;

            while (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                currentFrameIndex++;

                if (currentFrameIndex >= spinnerFrames.Length)
                    currentFrameIndex = 0;

                targetImage.sprite = spinnerFrames[currentFrameIndex];
            }
        }

        private void UpdateRotation(float deltaTime)
        {
            if (rotateTarget == null)
                return;

            rotateTarget.Rotate(0f, 0f, rotateSpeed * deltaTime);
        }

        private void ApplyFirstFrame()
        {
            if (targetImage == null)
                return;

            if (spinnerFrames == null || spinnerFrames.Length == 0)
                return;

            currentFrameIndex = 0;
            targetImage.sprite = spinnerFrames[currentFrameIndex];
            targetImage.preserveAspect = true;
        }

#if UNITY_EDITOR
        [Button("첫 프레임 적용")]
        private void PreviewFirstFrame()
        {
            ApplyFirstFrame();
        }
#endif
    }
}
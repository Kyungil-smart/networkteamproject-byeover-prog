using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace DeadZone.Actors.UI
{
    public class LoadingDotsTween : MonoBehaviour
    {
        [Title("점 오브젝트")]
        [SerializeField, LabelText("점 RectTransform 목록")]
        private RectTransform[] dotRects;

        [Title("움직임 설정")]
        [SerializeField, LabelText("점프 높이")]
        private float jumpHeight = 14f;

        [SerializeField, LabelText("올라가는 시간")]
        private float upDuration = 0.28f;

        [SerializeField, LabelText("내려오는 시간")]
        private float downDuration = 0.28f;

        [SerializeField, LabelText("점 사이 딜레이")]
        private float intervalDelay = 0.12f;

        [SerializeField, LabelText("한 사이클 대기 시간")]
        private float loopDelay = 0.15f;

        [SerializeField, LabelText("시간 정지 무시")]
        private bool useUnscaledTime = true;

        [Title("자동 설정")]
        [SerializeField, LabelText("자식 TMP 자동 수집")]
        private bool autoCollectDotsOnAwake = true;

        private Sequence sequence;
        private Vector2[] originalPositions;

        private void Awake()
        {
            if (autoCollectDotsOnAwake)
                CollectDotsFromChildren();

            CacheOriginalPositions();
        }

        private void OnEnable()
        {
            Play();
        }

        private void OnDisable()
        {
            KillTween();
            ResetDots();
        }

        private void OnDestroy()
        {
            KillTween();
        }

        [Button("재생")]
        public void Play()
        {
            KillTween();
            ResetDots();

            if (dotRects == null || dotRects.Length == 0)
                return;

            sequence = DOTween.Sequence();
            sequence.SetUpdate(useUnscaledTime);

            for (int i = 0; i < dotRects.Length; i++)
            {
                RectTransform dot = dotRects[i];

                if (dot == null)
                    continue;

                Vector2 originalPosition = originalPositions[i];
                Vector2 upPosition = originalPosition + Vector2.up * jumpHeight;

                Sequence dotSequence = DOTween.Sequence();
                dotSequence.SetUpdate(useUnscaledTime);

                dotSequence.Append(dot.DOAnchorPos(upPosition, upDuration).SetEase(Ease.OutQuad));
                dotSequence.Append(dot.DOAnchorPos(originalPosition, downDuration).SetEase(Ease.InQuad));

                sequence.Insert(i * intervalDelay, dotSequence);
            }

            float totalDuration = upDuration + downDuration + intervalDelay * dotRects.Length + loopDelay;
            sequence.AppendInterval(totalDuration);
            sequence.SetLoops(-1, LoopType.Restart);
        }

        [Button("정지")]
        public void Stop()
        {
            KillTween();
            ResetDots();
        }

        [Button("자식 TMP 자동 수집")]
        private void CollectDotsFromChildren()
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            dotRects = new RectTransform[texts.Length];

            for (int i = 0; i < texts.Length; i++)
                dotRects[i] = texts[i].rectTransform;
        }

        private void CacheOriginalPositions()
        {
            if (dotRects == null)
                return;

            originalPositions = new Vector2[dotRects.Length];

            for (int i = 0; i < dotRects.Length; i++)
            {
                if (dotRects[i] == null)
                    continue;

                originalPositions[i] = dotRects[i].anchoredPosition;
            }
        }

        private void ResetDots()
        {
            if (dotRects == null || originalPositions == null)
                return;

            int count = Mathf.Min(dotRects.Length, originalPositions.Length);

            for (int i = 0; i < count; i++)
            {
                if (dotRects[i] == null)
                    continue;

                dotRects[i].anchoredPosition = originalPositions[i];
            }
        }

        private void KillTween()
        {
            if (sequence == null)
                return;

            sequence.Kill();
            sequence = null;
        }
    }
}
using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class LoadingTipView : MonoBehaviour
    {
        [Title("팁 데이터")]
        [SerializeField, LabelText("Tip Database")]
        private LoadingTipDatabase tipDatabase;

        [SerializeField, LabelText("Image Database")]
        private LoadingTipImageDatabase imageDatabase;

        [Title("텍스트 연결")]
        [SerializeField, LabelText("Tip Title Text")]
        private TMP_Text tipTitleText;

        [SerializeField, LabelText("Tip Message Text")]
        private TMP_Text tipMessageText;

        [Title("이미지 연결")]
        [SerializeField, LabelText("Background Image")]
        private Image backgroundImage;

        [SerializeField, LabelText("Black Background")]
        private GameObject blackBackground;

        [SerializeField, LabelText("Special Image Root")]
        private GameObject specialImageRoot;

        [SerializeField, LabelText("Special Image")]
        private Image specialImage;

        [SerializeField, LabelText("Figure Pair Image Root")]
        private GameObject figurePairImageRoot;

        [SerializeField, LabelText("Figure Pair Image")]
        private Image figurePairImage;

        [SerializeField, LabelText("Figure Pair Image Key")]
        private string figurePairImageKey = "SeungwooFigure&JadeFigure";

        [Title("전환 연출")]
        [SerializeField, LabelText("Tip Fade Group")]
        private CanvasGroup tipFadeGroup;

        [SerializeField, LabelText("자동으로 팁 변경")]
        private bool autoChangeTip = true;

        [SerializeField, LabelText("팁 변경 간격")]
        private float changeInterval = 5f;

        [SerializeField, LabelText("페이드 아웃 시간")]
        private float fadeOutDuration = 0.25f;

        [SerializeField, LabelText("페이드 인 시간")]
        private float fadeInDuration = 0.25f;

        [SerializeField, LabelText("비활성 시간 무시")]
        private bool useUnscaledTime = true;

        [Title("기본값")]
        [SerializeField, LabelText("기본 제목")]
        private string defaultTitle = "데드존 팁";

        [SerializeField, TextArea(2, 4), LabelText("기본 메시지")]
        private string fallbackMessage = "살아서 나가야 전리품입니다.";

        private Sequence changeSequence;
        private int currentTipIndex = -1;

        private void OnEnable()
        {
            ShowRandomTipImmediate();
            StartAutoChange();
        }

        private void OnDisable()
        {
            KillSequence();
        }

        private void OnDestroy()
        {
            KillSequence();
        }

        [Sirenix.OdinInspector.Button("랜덤 팁 즉시 표시")]
        public void ShowRandomTipImmediate()
        {
            LoadingTipEntry selectedTip = GetRandomTip();

            SetTip(
                GetTitleByCategory(selectedTip.category),
                selectedTip.message,
                selectedTip.imageKey
            );

            if (tipFadeGroup != null)
                tipFadeGroup.alpha = 1f;
        }

        [Sirenix.OdinInspector.Button("다음 팁으로 전환")]
        public void ChangeToNextTip()
        {
            if (tipFadeGroup == null)
            {
                ShowRandomTipImmediate();
                return;
            }

            tipFadeGroup.DOKill();

            Sequence sequence = DOTween.Sequence();
            sequence.SetUpdate(useUnscaledTime);

            sequence.Append(
                tipFadeGroup
                    .DOFade(0f, fadeOutDuration)
                    .SetEase(Ease.OutQuad)
            );

            sequence.AppendCallback(() =>
            {
                LoadingTipEntry selectedTip = GetRandomTip();

                SetTip(
                    GetTitleByCategory(selectedTip.category),
                    selectedTip.message,
                    selectedTip.imageKey
                );
            });

            sequence.Append(
                tipFadeGroup
                    .DOFade(1f, fadeInDuration)
                    .SetEase(Ease.InQuad)
            );
        }

        private void StartAutoChange()
        {
            KillSequence();

            if (!autoChangeTip)
                return;

            if (changeInterval <= 0f)
                return;

            changeSequence = DOTween.Sequence();
            changeSequence.SetUpdate(useUnscaledTime);

            changeSequence.AppendInterval(changeInterval);
            changeSequence.AppendCallback(ChangeToNextTip);
            changeSequence.SetLoops(-1, LoopType.Restart);
        }

        private LoadingTipEntry GetRandomTip()
        {
            if (tipDatabase == null || tipDatabase.tips == null || tipDatabase.tips.Count == 0)
            {
                return new LoadingTipEntry
                {
                    category = LoadingTipCategory.Humor,
                    message = fallbackMessage,
                    imageKey = "Default"
                };
            }

            if (tipDatabase.tips.Count == 1)
            {
                currentTipIndex = 0;
                return tipDatabase.tips[0];
            }

            int nextIndex = currentTipIndex;

            int safety = 0;
            while (nextIndex == currentTipIndex && safety < 20)
            {
                nextIndex = Random.Range(0, tipDatabase.tips.Count);
                safety++;
            }

            currentTipIndex = nextIndex;
            return tipDatabase.tips[currentTipIndex];
        }

        private void SetTip(string title, string message, string imageKey)
        {
            if (tipTitleText != null)
                tipTitleText.text = title;

            if (tipMessageText != null)
                tipMessageText.text = message;

            ApplyImageMode(imageKey);
        }

        private void ApplyImageMode(string imageKey)
        {
            Sprite specialSprite = imageDatabase != null
                ? imageDatabase.GetSpecialSprite(imageKey)
                : null;

            if (specialSprite != null && IsFigurePairImageKey(imageKey))
                ApplyFigurePairImageMode(specialSprite);
            else if (specialSprite != null)
                ApplySpecialImageMode(specialSprite);
            else
                ApplyDefaultBackgroundMode();
        }

        private void ApplyDefaultBackgroundMode()
        {
            Sprite backgroundSprite = imageDatabase != null
                ? imageDatabase.GetRandomDefaultBackground()
                : null;

            if (backgroundImage != null)
            {
                backgroundImage.enabled = true;
                backgroundImage.sprite = backgroundSprite;
                backgroundImage.color = Color.white;
                backgroundImage.preserveAspect = false;
            }

            if (blackBackground != null)
                blackBackground.SetActive(false);

            if (specialImageRoot != null)
                specialImageRoot.SetActive(false);

            if (specialImage != null)
            {
                specialImage.sprite = null;
                specialImage.enabled = false;
            }

            if (figurePairImageRoot != null)
                figurePairImageRoot.SetActive(false);

            ClearFigurePairImage();

            if (backgroundSprite == null)
                Debug.LogWarning("[LoadingTipView] Default 배경 Sprite가 없습니다. LoadingTipImageDatabase의 기본 배경 이미지 목록을 확인하세요.");
        }

        private void ApplySpecialImageMode(Sprite sprite)
        {
            if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
                backgroundImage.enabled = false;
            }

            if (blackBackground != null)
                blackBackground.SetActive(true);

            if (specialImageRoot != null)
                specialImageRoot.SetActive(true);

            if (figurePairImageRoot != null)
                figurePairImageRoot.SetActive(false);

            ClearFigurePairImage();

            if (specialImage != null)
            {
                specialImage.sprite = sprite;
                specialImage.enabled = true;
                specialImage.preserveAspect = true;
            }
        }

        private void ApplyFigurePairImageMode(Sprite sprite)
        {
            if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
                backgroundImage.enabled = false;
            }

            if (blackBackground != null)
                blackBackground.SetActive(true);

            if (specialImageRoot != null)
                specialImageRoot.SetActive(false);

            if (specialImage != null)
            {
                specialImage.sprite = null;
                specialImage.enabled = false;
            }

            if (figurePairImageRoot != null)
                figurePairImageRoot.SetActive(true);

            if (figurePairImage != null)
            {
                figurePairImage.sprite = sprite;
                figurePairImage.enabled = true;
                figurePairImage.preserveAspect = true;
            }
        }

        private bool IsFigurePairImageKey(string imageKey)
        {
            return string.Equals(
                NormalizeImageKey(imageKey),
                NormalizeImageKey(figurePairImageKey),
                System.StringComparison.Ordinal);
        }

        private static string NormalizeImageKey(string imageKey)
        {
            return string.IsNullOrWhiteSpace(imageKey) ? "Default" : imageKey.Trim();
        }

        private void ClearFigurePairImage()
        {
            if (figurePairImage == null)
                return;

            figurePairImage.sprite = null;
            figurePairImage.enabled = false;
        }

        private string GetTitleByCategory(LoadingTipCategory category)
        {
            return category switch
            {
                LoadingTipCategory.Control => "조작법",
                LoadingTipCategory.Combat => "전투 팁",
                LoadingTipCategory.Loot => "파밍 팁",
                LoadingTipCategory.Hideout => "은신처 팁",
                LoadingTipCategory.Extraction => "탈출 팁",
                LoadingTipCategory.EasterEgg => "희귀 정보",
                LoadingTipCategory.Humor => "현장 기록",
                _ => defaultTitle
            };
        }

        private void KillSequence()
        {
            if (changeSequence != null)
            {
                changeSequence.Kill();
                changeSequence = null;
            }

            if (tipFadeGroup != null)
                tipFadeGroup.DOKill();
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    public class LoadingTipView : MonoBehaviour
    {
        [Header("데이터")]
        [SerializeField] private LoadingTipDatabase tipDatabase;
        [SerializeField] private LoadingTipImageDatabase imageDatabase;

        [Header("텍스트 연결")]
        [SerializeField] private TMP_Text tipTitleText;
        [SerializeField] private TMP_Text tipMessageText;

        [Header("이미지 연결")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private GameObject blackBackground;
        [SerializeField] private GameObject specialImageRoot;
        [SerializeField] private Image specialImage;

        [Header("기본값")]
        [SerializeField] private string defaultTitle = "데드존 팁";

        private void OnEnable()
        {
            ShowRandomTip();
        }

        public void ShowRandomTip()
        {
            if (tipDatabase == null || tipDatabase.tips == null || tipDatabase.tips.Count == 0)
            {
                SetTip(defaultTitle, "로딩 중입니다.", "Default");
                return;
            }

            int index = Random.Range(0, tipDatabase.tips.Count);
            LoadingTipEntry selectedTip = tipDatabase.tips[index];

            SetTip(
                GetTitleByCategory(selectedTip.category),
                selectedTip.message,
                selectedTip.imageKey
            );
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
            bool isSpecial = imageDatabase != null && imageDatabase.IsSpecialImageKey(imageKey);

            if (isSpecial)
                ApplySpecialImageMode(imageKey);
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
        }

        private void ApplySpecialImageMode(string imageKey)
        {
            Sprite sprite = imageDatabase != null
                ? imageDatabase.GetSpecialSprite(imageKey)
                : null;

            if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
                backgroundImage.enabled = false;
            }

            if (blackBackground != null)
                blackBackground.SetActive(true);

            if (specialImageRoot != null)
                specialImageRoot.SetActive(true);

            if (specialImage != null)
            {
                specialImage.sprite = sprite;
                specialImage.enabled = specialImage.sprite != null;
                specialImage.preserveAspect = true;
            }
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
    }
}

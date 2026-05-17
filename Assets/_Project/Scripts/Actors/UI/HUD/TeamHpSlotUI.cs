using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// 인게임 팀원 HP 슬롯 한 칸. TeamInfoHUD가 clientId와 연동해 갱신합니다.
    /// 구분은 로비 파티와 동일한 팀 색의 아이콘, 수치는 텍스트로 표시합니다.
    /// </summary>
    public sealed class TeamHpSlotUI : MonoBehaviour
    {
        [TitleGroup("슬롯")]
        [Required, SerializeField]
        private GameObject root;

        [TitleGroup("슬롯")]
        [Required, SerializeField]
        private Image hpFill;

        [TitleGroup("슬롯")]
        [Required, SerializeField, Tooltip("로비 파티 생성 시 배정된 색과 동일하게 TeamInfoHUD가 채워 줍니다.")]
        private Image teamIcon;

        [TitleGroup("슬롯")]
        [SerializeField, Tooltip("예: 85 / 120 (HUDManager와 같이 정수 올림)")]
        private TMP_Text hpValueText;

        [TitleGroup("슬롯")]
        [SerializeField] private Color knockedHpColor = new(1f, 0.45f, 0.2f, 1f);

        [SerializeField] private Color normalHpColor = new(0.78f, 0.08f, 0.05f, 1f);

        private ulong boundClientId;

        private bool loggedInvalidRoot;
        private Color normalHpFillColor = Color.white;
        private Color defaultHpTextColor = Color.white;
        private bool isKnockedMode;

        public ulong BoundClientId => boundClientId;

        private void Awake()
        {
            CacheDefaultColors();
        }

        public void Bind(ulong clientId, Color teamColor, float hp, float maxHp)
        {
            boundClientId = clientId;
            CacheDefaultTextColor();
            normalHpFillColor = normalHpColor;

            GetSafeRoot().SetActive(true);

            if (teamIcon != null)
                teamIcon.color = teamColor;

            SetHp(hp, maxHp);
        }

        public void SetHp(float hp, float maxHp)
        {
            isKnockedMode = false;
            ApplyHpColors(normalHpFillColor, defaultHpTextColor);

            float denom = Mathf.Max(1f, maxHp);
            float clampedHp = Mathf.Clamp(hp, 0f, denom);

            if (hpFill != null)
                hpFill.fillAmount = Mathf.Clamp01(clampedHp / denom);

            RefreshHpText(clampedHp, denom);
        }

        public void SetKnocked(float remainingSeconds, float totalSeconds)
        {
            isKnockedMode = true;
            ApplyHpColors(knockedHpColor, knockedHpColor);

            float total = Mathf.Max(1f, totalSeconds);
            float remaining = Mathf.Clamp(remainingSeconds, 0f, total);

            if (hpFill != null)
                hpFill.fillAmount = Mathf.Clamp01(remaining / total);

            if (hpValueText != null)
                hpValueText.text = Mathf.CeilToInt(remaining).ToString();
        }

        public void SetTeamColor(Color teamColor)
        {
            if (teamIcon != null)
                teamIcon.color = teamColor;

            if (!isKnockedMode && hpFill != null)
                hpFill.color = normalHpFillColor;
        }

        private void RefreshHpText(float hp, float maxHp)
        {
            if (hpValueText == null)
                return;

            int cur = Mathf.CeilToInt(hp);
            hpValueText.text = cur.ToString();
        }

        public void Clear()
        {
            boundClientId = 0;
            isKnockedMode = false;
            ApplyHpColors(normalHpFillColor, defaultHpTextColor);

            GetSafeRoot().SetActive(false);
        }

        private void CacheDefaultColors()
        {
            CacheDefaultTextColor();
            normalHpFillColor = normalHpColor;
        }

        private void CacheDefaultTextColor()
        {
            if (hpValueText != null)
                defaultHpTextColor = hpValueText.color;
        }

        private void ApplyHpColors(Color fillColor, Color textColor)
        {
            if (hpFill != null)
                hpFill.color = fillColor;

            if (hpValueText != null)
                hpValueText.color = textColor;
        }

        private GameObject GetSafeRoot()
        {
            if (root == null)
                return gameObject;

            if (root != gameObject && transform.IsChildOf(root.transform))
            {
                if (!loggedInvalidRoot)
                {
                    loggedInvalidRoot = true;
                    Debug.LogWarning(
                        $"[TeamHpSlotUI] Root is an ancestor of this slot. Assign the slot GameObject itself as root. slot={name}, root={root.name}",
                        this);
                }

                return gameObject;
            }

            return root;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Actors.Player;

namespace DeadZone.Actors.UI
{
    /// <summary>
    /// HUD Canvas 위에 그려지는 플레이어 이름표 한 개입니다.
    /// 실제 위치 갱신은 PlayerNameplateManager가 담당합니다.
    /// </summary>
    public sealed class PlayerNameplateUI : MonoBehaviour
    {
        [Header("==== 참조 ====")]
        [SerializeField] private RectTransform rootRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image teamColorIcon;

        [Header("==== 표시 ====")]
        [SerializeField] private bool useTeamColorForText = false;

        private PlayerDisplayNameIdentity displayNameIdentity;
        private PlayerTeamIdentity teamIdentity;

        public RectTransform RootRect
        {
            get
            {
                if (rootRect == null)
                    rootRect = transform as RectTransform;

                return rootRect;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        public void Bind(PlayerDisplayNameIdentity displayNameIdentity, PlayerTeamIdentity teamIdentity)
        {
            Unbind();

            this.displayNameIdentity = displayNameIdentity;
            this.teamIdentity = teamIdentity;

            if (this.displayNameIdentity != null)
            {
                this.displayNameIdentity.DisplayNameChanged += HandleDisplayNameChanged;
                SetDisplayName(this.displayNameIdentity.CurrentDisplayName);
            }

            if (this.teamIdentity != null)
            {
                this.teamIdentity.TeamColorChanged += HandleTeamColorChanged;
                SetTeamColor(this.teamIdentity.CurrentColor);
            }
        }

        public void Unbind()
        {
            if (displayNameIdentity != null)
                displayNameIdentity.DisplayNameChanged -= HandleDisplayNameChanged;

            if (teamIdentity != null)
                teamIdentity.TeamColorChanged -= HandleTeamColorChanged;

            displayNameIdentity = null;
            teamIdentity = null;
        }

        public void SetAnchoredPosition(Vector2 anchoredPosition)
        {
            if (RootRect != null)
                RootRect.anchoredPosition = anchoredPosition;
        }

        public void SetVisible(bool visible)
        {
            ResolveReferences();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                return;
            }

            gameObject.SetActive(visible);
        }

        private void HandleDisplayNameChanged(string displayName)
        {
            SetDisplayName(displayName);
        }

        private void HandleTeamColorChanged(Color32 color)
        {
            SetTeamColor(color);
        }

        private void SetDisplayName(string displayName)
        {
            if (nameText == null)
                return;

            nameText.text = string.IsNullOrWhiteSpace(displayName)
                ? "Player"
                : displayName;
        }

        private void SetTeamColor(Color32 color)
        {
            if (teamColorIcon != null)
                teamColorIcon.color = color;

            if (useTeamColorForText && nameText != null)
                nameText.color = color;
        }

        private void ResolveReferences()
        {
            if (rootRect == null)
                rootRect = transform as RectTransform;

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (nameText == null)
                nameText = GetComponentInChildren<TMP_Text>(true);
        }
    }
}

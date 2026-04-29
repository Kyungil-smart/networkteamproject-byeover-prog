using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 작성자 : 홍정옥
// 기능 : 킬피드 개별 엔트리
// 프리팹 루트에 부착, KillFeedUI가 Instantiate 후 Setup 호출로 초기화
namespace DeadZone.Actors
{
    /// <summary>
    /// 킬피드의 개별 엔트리
    /// 엔트리별 등장/크리티컬/소멸 연출을 스스로 재생
    /// </summary>
    public class KillFeedEntry : MonoBehaviour
    {
        // UI 레퍼런스
        [BoxGroup("References")]
        [Required, SerializeField] private TMP_Text label;// 엔트리 문구 텍스트

        // 설정값
        [BoxGroup("Config")]
        [Tooltip("일반 킬 색상")]
        [SerializeField] private Color normalColor = Color.white;

        [BoxGroup("Config")]
        [Tooltip("크리티컬 히트 색상")]
        [SerializeField] private Color critColor = new(1f, 0.84f, 0f);// 골드

        [BoxGroup("Config")]
        [Tooltip("Expire 피드백 재생 후 실제 Destroy까지 대기 시간 (초)")]
        [MinValue(0f), SerializeField] private float fadeOutDuration = 0.5f;

        // Feel 피드백
        [FoldoutGroup("Feedbacks")]
        [Tooltip("엔트리 생성 시 재생 (슬라이드인/페이드인)")]
        [SerializeField] private MMF_Player onSpawnFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("크리티컬 엔트리일 때 추가 재생 (번쩍임/셰이크)")]
        [SerializeField] private MMF_Player onCritFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("수명 만료로 사라지기 직전 재생 (페이드아웃)")]
        [SerializeField] private MMF_Player onExpireFeedback;

        // 런타임 상태
        private bool expiring;// 중복 Expire 방지 플래그

        // KillFeedUI가 Instantiate 직후 호출하는 초기화 메서드
        public void Setup(string text, bool isCritical)
        {
            ConfigureRect();

            Debug.Log($"[KillFeedEntry] Setup text='{text}', critical={isCritical}", this);

            if (label != null)
            {
                label.text = text;
                label.color = isCritical ? critColor : normalColor;
            }

            UIFeedbackTester.Play(onSpawnFeedback, this, "킬피드 엔트리 생성");
            if (isCritical) UIFeedbackTester.Play(onCritFeedback, this, "킬피드 엔트리 치명타");
        }

        // 수명 만료 시 KillFeedUI가 호출, 페이드아웃 후 자체 파괴
        public void BeginExpire()
        {
            if (expiring) return;
            expiring = true;

            Debug.Log($"[KillFeedEntry] BeginExpire: {name}", this);
            UIFeedbackTester.Play(onExpireFeedback, this, "킬피드 엔트리 만료");
            Destroy(gameObject, fadeOutDuration);
        }

        // maxEntries 초과로 즉시 제거될 때 호출 (페이드 없이 파괴)
        public void DestroyImmediate()
        {
            Destroy(gameObject);
        }

        private void ConfigureRect()
        {
            if (transform is RectTransform root)
            {
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(1f, 1f);
                root.pivot = new Vector2(1f, 1f);
                root.anchoredPosition = Vector2.zero;
            }

            Image background = GetComponentInChildren<Image>(true);
            if (background != null && background.transform is RectTransform backgroundRect)
            {
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.pivot = new Vector2(0.5f, 0.5f);
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;
                backgroundRect.anchoredPosition = Vector2.zero;
            }

            if (label != null && label.transform is RectTransform labelRect)
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.offsetMin = new Vector2(8f, 2f);
                labelRect.offsetMax = new Vector2(-8f, -2f);
                labelRect.anchoredPosition = Vector2.zero;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 16f;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }
        }
    }
}

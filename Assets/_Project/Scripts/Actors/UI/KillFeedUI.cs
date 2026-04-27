using System.Collections.Generic;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 우상단 킬피드 UI
// 적 처치/크리티컬/플레이어 사망 이벤트를 동적 엔트리로 표시
// EventBus로 EnemyKilled / CriticalHit / PlayerDied 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// 우상단 킬피드 관리자
    /// 엔트리 연출은 KillFeedEntry 프리팹 내부에서 처리
    /// </summary>
    public class KillFeedUI : MonoBehaviour
    {
        // UI 레퍼런스
        [BoxGroup("References")]
        [Required, SerializeField] private RectTransform entriesRoot;// 엔트리가 쌓이는 컨테이너

        [BoxGroup("References")]
        [Required, AssetsOnly, SerializeField] private KillFeedEntry entryPrefab;// 동적 생성할 엔트리 프리팹

        [BoxGroup("References")]
        [SerializeField] private RectTransform questRoot;// 비워두면 같은 HUD 아래의 Quest 오브젝트를 자동 탐색

        // 설정값
        [BoxGroup("Config")]
        [MinValue(1), SerializeField] private int maxEntries = 5;// 동시에 표시할 최대 엔트리 수

        [BoxGroup("Config")]
        [MinValue(0.1f), SerializeField] private float entryLifetime = 5f;// 엔트리 수명

        [BoxGroup("Config")]
        [SerializeField] private bool configureLayoutOnAwake = true;

        [BoxGroup("Config")]
        [MinValue(1f), SerializeField] private float entryWidth = 300f;

        [BoxGroup("Config")]
        [MinValue(1f), SerializeField] private float entryHeight = 28f;

        [BoxGroup("Config")]
        [MinValue(0f), SerializeField] private float entrySpacing = 4f;

        [BoxGroup("Config")]
        [SerializeField] private float questBottomGap = 8f;

        // Feel 피드백 (HUD 레벨 - 로컬 플레이어 기준 연출)
        [FoldoutGroup("Feedbacks")]
        [Tooltip("로컬 플레이어가 적을 처치했을 때 재생")]
        [SerializeField] private MMF_Player onLocalKillFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("로컬 플레이어가 크리티컬을 냈을 때 재생")]
        [SerializeField] private MMF_Player onLocalCritFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("팀원이 사망했을 때 재생")]
        [SerializeField] private MMF_Player onTeammateDeathFeedback;

        // 런타임 상태
        private readonly Queue<KillFeedEntry> activeEntries = new();// 현재 활성화된 엔트리 큐

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private int activeEntryCount => activeEntries.Count;// 현재 엔트리 개수

        private void Awake()
        {
            if (configureLayoutOnAwake)
                ConfigureEntriesRoot();
        }

        // 컴포넌트 활성화 시 EventBus 구독 시작
        private void OnEnable()
        {
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<CriticalHitEvent>(OnCritical);
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<CriticalHitEvent>(OnCritical);
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        // 해당 clientId가 로컬 플레이어인지 판별
        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId;
        }

        // 적 처치 이벤트 처리 + 로컬 처치면 HUD 피드백 추가 재생
        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            string attackerName = ResolveName(e.attackerClientId);
            AddEntry($"{attackerName} killed Enemy ({e.tier})", isCritical: false);

            if (IsLocalClient(e.attackerClientId))
                onLocalKillFeedback?.PlayFeedbacks();
        }

        // 크리티컬 히트 이벤트 처리 + 로컬 크리티컬이면 HUD 피드백 추가
        private void OnCritical(CriticalHitEvent e)
        {
            string attackerName = ResolveName(e.attackerClientId);
            AddEntry($"{attackerName} CRIT {e.zone} {e.damage}dmg", isCritical: true);

            if (IsLocalClient(e.attackerClientId))
                onLocalCritFeedback?.PlayFeedbacks();
        }

        // 플레이어 사망 이벤트 처리 + 팀원 사망이면 무거운 피드백 추가
        private void OnPlayerDied(PlayerDiedEvent e)
        {
            string victim = ResolveName(e.victimClientId);
            string killer = ResolveName(e.killerClientId);
            AddEntry($"{killer} killed {victim}", isCritical: false);

            // 로컬 본인의 죽음은 HUDManager의 Dead 전환 피드백에서 처리되므로 제외
            if (!IsLocalClient(e.victimClientId))
                onTeammateDeathFeedback?.PlayFeedbacks();
        }

        // ulong.MaxValue는 Enemy NPC를 의미하는 약속값
        private string ResolveName(ulong clientId)
        {
            if (clientId == ulong.MaxValue) return "Enemy";
            return $"Player {clientId}";
        }

        // 엔트리 프리팹 인스턴스화 + 큐 관리 + 수명 예약
        private void AddEntry(string text, bool isCritical)
        {
            if (entryPrefab == null || entriesRoot == null) return;

            var entry = Instantiate(entryPrefab, entriesRoot);
            ConfigureEntryRect(entry);
            entry.Setup(text, isCritical);
            activeEntries.Enqueue(entry);

            // 최대 개수 초과분은 즉시 제거 (페이드 없음 - 이미 오래된 엔트리)
            while (activeEntries.Count > maxEntries)
            {
                var old = activeEntries.Dequeue();
                if (old != null) old.DestroyImmediate();
            }

            // 수명 만료 시 페이드아웃 후 제거 (코루틴으로 추적)
            StartCoroutine(ExpireAfter(entry, entryLifetime));
        }

        private void ConfigureEntriesRoot()
        {
            if (entriesRoot == null) return;

            entriesRoot.anchorMin = new Vector2(0.5f, 0.5f);
            entriesRoot.anchorMax = new Vector2(0.5f, 0.5f);
            entriesRoot.pivot = new Vector2(1f, 1f);
            entriesRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, entryWidth);
            entriesRoot.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                maxEntries * entryHeight + Mathf.Max(0, maxEntries - 1) * entrySpacing);

            RectTransform targetQuest = questRoot != null ? questRoot : FindQuestRoot();
            if (targetQuest != null && entriesRoot.parent is RectTransform parent)
            {
                Vector3[] questCorners = new Vector3[4];
                targetQuest.GetWorldCorners(questCorners);
                Vector2 questBottomRight = parent.InverseTransformPoint(questCorners[3]);
                entriesRoot.anchoredPosition = new Vector2(questBottomRight.x, questBottomRight.y - questBottomGap);
            }

            var layout = entriesRoot.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = entriesRoot.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.childAlignment = TextAnchor.UpperRight;
            layout.spacing = entrySpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private RectTransform FindQuestRoot()
        {
            Transform current = transform;
            while (current != null)
            {
                Transform quest = current.parent != null ? current.parent.Find("Quest") : null;
                if (quest != null && quest.TryGetComponent(out RectTransform rect))
                    return rect;

                current = current.parent;
            }

            return null;
        }

        private void ConfigureEntryRect(KillFeedEntry entry)
        {
            if (entry == null || !entry.TryGetComponent(out RectTransform rect)) return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, entryHeight);

            var layoutElement = entry.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = entry.gameObject.AddComponent<LayoutElement>();

            layoutElement.preferredWidth = entryWidth;
            layoutElement.preferredHeight = entryHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
        }

        // 지정된 시간 후 엔트리에 페이드아웃 시작 명령
        private System.Collections.IEnumerator ExpireAfter(KillFeedEntry entry, float delay)
        {
            yield return new WaitForSeconds(delay);
            // maxEntries 초과로 이미 파괴됐을 수 있어 null 체크 필수
            if (entry != null) entry.BeginExpire();
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("Add Test Entry (Normal)")]
        private void TestNormalEntry()
        {
            if (!Application.isPlaying) return;
            AddEntry("Player 0 killed Enemy (Elite)", isCritical: false);
        }

        [TitleGroup("Debug")]
        [Button("Add Test Entry (Crit)"), GUIColor(1f, 0.84f, 0f)]
        private void TestCritEntry()
        {
            if (!Application.isPlaying) return;
            AddEntry("Player 0 CRIT Head 120dmg", isCritical: true);
        }

        [TitleGroup("Debug")]
        [Button("Test Local Kill Feedback")]
        private void TestLocalKill() => onLocalKillFeedback?.PlayFeedbacks();

        [TitleGroup("Debug")]
        [Button("Test Local Crit Feedback"), GUIColor(1f, 0.84f, 0f)]
        private void TestLocalCrit() => onLocalCritFeedback?.PlayFeedbacks();
#endif
    }
}

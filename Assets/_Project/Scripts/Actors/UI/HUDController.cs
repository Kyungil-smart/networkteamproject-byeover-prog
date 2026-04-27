using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 플레이어 상태에 따라 HUD 패널을 전환하는 컨트롤러
// PlayerHUD(생존) / KnockedHUD(기절) / SpectatorHUD(사망) 중 하나만 활성화
// EventBus로 PlayerStateChanged 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// HUD 상태 전환 관리자
    /// 로컬 플레이어의 상태 변경에 따라 적절한 HUD 패널만 활성화
    /// UI 부모 오브젝트 등 한 곳에 1개만 부착
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        // 상태 패널 (각각의 HUD 루트 게임오브젝트)
        [BoxGroup("State Panels")]
        [Required, SerializeField] private GameObject playerHUD;// 생존 상태 - 메인 HUD

        [BoxGroup("State Panels")]
        [Required, SerializeField] private GameObject knockedHUD;// 기절 상태 - KnockedHUD 루트

        [BoxGroup("State Panels")]
        [Required, SerializeField] private GameObject spectatorHUD;// 사망 상태 - SpectatorHUD 루트

        // Feel 피드백 - 상태 전환
        [FoldoutGroup("Feedbacks")]
        [Tooltip("Alive 상태 진입 시 재생 (부활 연출)")]
        [SerializeField] private MMF_Player onAliveFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("Knocked 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("Dead 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onSpectatorFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly] private PlayerState currentState;// 현재 적용된 플레이어 상태

        // 컴포넌트 활성화 시 EventBus 구독 + 기본 상태로 초기화
        private void OnEnable()
        {
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);

            // 시작은 Alive 상태로 가정
            ApplyState(PlayerState.Alive, playFeedback: false);
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        // 해당 clientId가 로컬 플레이어인지 판별
        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null
                && clientId == NetworkManager.Singleton.LocalClientId;
        }

        // 로컬 플레이어 상태 변경 시 패널 전환 + 피드백 재생
        private void OnPlayerStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;
            ApplyState(e.newState, playFeedback: true);
        }

        // 상태에 맞춰 패널 활성화/비활성화 + 선택적 피드백 재생
        private void ApplyState(PlayerState state, bool playFeedback)
        {
            currentState = state;

            if (playerHUD != null)    playerHUD.SetActive(state == PlayerState.Alive);
            if (knockedHUD != null)   knockedHUD.SetActive(state == PlayerState.Knocked);
            if (spectatorHUD != null) spectatorHUD.SetActive(state == PlayerState.Dead);

            if (!playFeedback) return;

            switch (state)
            {
                case PlayerState.Alive:   onAliveFeedback?.PlayFeedbacks();     break;
                case PlayerState.Knocked: onKnockedFeedback?.PlayFeedbacks();   break;
                case PlayerState.Dead:    onSpectatorFeedback?.PlayFeedbacks(); break;
            }
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("Debug")]
        [Button("Set Alive"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestAlive() => ApplyState(PlayerState.Alive, playFeedback: true);

        [TitleGroup("Debug")]
        [Button("Set Knocked"), GUIColor(1f, 0.7f, 0.4f)]
        private void TestKnocked() => ApplyState(PlayerState.Knocked, playFeedback: true);

        [TitleGroup("Debug")]
        [Button("Set Dead"), GUIColor(0.5f, 0.5f, 0.5f)]
        private void TestDead() => ApplyState(PlayerState.Dead, playFeedback: true);
#endif
    }
}
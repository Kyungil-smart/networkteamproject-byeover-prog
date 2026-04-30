using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 로컬 플레이어 상태에 따라 PlayerHUD / KnockedHUD / SpectatorHUD를 전환한다.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [BoxGroup("상태 패널")]
        [Required, SerializeField] private GameObject playerHUD;

        [BoxGroup("상태 패널")]
        [Required, SerializeField] private GameObject knockedHUD;

        [BoxGroup("상태 패널")]
        [Required, SerializeField] private GameObject spectatorHUD;

        [FoldoutGroup("피드백")]
        [Tooltip("생존 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onAliveFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("기절 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onKnockedFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("사망 상태 진입 시 재생")]
        [SerializeField] private MMF_Player onSpectatorFeedback;

        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private PlayerState currentState;

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
            ApplyState(PlayerState.Alive, playFeedback: false);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerStateChangedEvent>(OnPlayerStateChanged);
        }

        private bool IsLocalClient(ulong clientId)
        {
            return NetworkManager.Singleton != null
                && clientId == NetworkManager.Singleton.LocalClientId;
        }

        private void OnPlayerStateChanged(PlayerStateChangedEvent e)
        {
            if (!IsLocalClient(e.clientId)) return;

            Debug.Log($"[HUDController] PlayerStateChanged {e.oldState} -> {e.newState}", this);
            ApplyState(e.newState, playFeedback: true);
        }

        private void ApplyState(PlayerState state, bool playFeedback)
        {
            currentState = state;

            if (playerHUD != null) playerHUD.SetActive(state != PlayerState.Dead);
            ApplyKnockedHudVisibility(state == PlayerState.Knocked);
            if (spectatorHUD != null) spectatorHUD.SetActive(state == PlayerState.Dead);

            ApplyLinkedHpBarState(state);

            if (!playFeedback) return;

            switch (state)
            {
                case PlayerState.Alive:
                    UIFeedbackTester.Play(onAliveFeedback, this, "HUDController 생존 상태");
                    break;
                case PlayerState.Knocked:
                    UIFeedbackTester.Play(onKnockedFeedback, this, "HUDController 기절 상태");
                    break;
                case PlayerState.Dead:
                    UIFeedbackTester.Play(onSpectatorFeedback, this, "HUDController 관전 상태");
                    break;
            }
        }

        private void ApplyKnockedHudVisibility(bool visible)
        {
            if (knockedHUD == null) return;

            KnockedHUD knockedHudComponent = knockedHUD.GetComponent<KnockedHUD>();
            if (knockedHudComponent != null)
            {
                knockedHUD.SetActive(true);
                knockedHudComponent.SetVisibleForUI(visible);
                return;
            }

            knockedHUD.SetActive(visible);
        }

        private void ApplyLinkedHpBarState(PlayerState state)
        {
            HUDManager[] hudManagers = GetComponentsInChildren<HUDManager>(true);
            foreach (HUDManager hudManager in hudManagers)
            {
                if (state == PlayerState.Knocked)
                    hudManager.ApplyKnockedHpModeForUI();
                else if (state == PlayerState.Alive)
                    hudManager.ApplyAliveHpModeForUI();
            }
        }

#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("생존 상태로 변경"), GUIColor(0.6f, 1f, 0.6f)]
        private void TestAlive() => ApplyState(PlayerState.Alive, playFeedback: true);

        [TitleGroup("디버그")]
        [Button("기절 상태로 변경"), GUIColor(1f, 0.7f, 0.4f)]
        private void TestKnocked() => ApplyState(PlayerState.Knocked, playFeedback: true);

        [TitleGroup("디버그")]
        [Button("관전 상태로 변경"), GUIColor(0.5f, 0.5f, 0.5f)]
        private void TestDead() => ApplyState(PlayerState.Dead, playFeedback: true);
#endif
    }
}

using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using TMPro;
using Unity.Netcode;
using UnityEngine;

using DeadZone.Core;

// 작성자 : 홍정옥
// 기능 : 사망 후 관전 모드 HUD
// 현재 관전 대상 이름과 조작 키 힌트 표시
// EventBus로 SpectatorTargetChanged 구독
namespace DeadZone.Actors
{
    /// <summary>
    /// 관전 대상 이름 + Q/E/Tab 키 힌트 표시
    /// 로컬 플레이어가 Dead 상태일 때 HUDManager가 활성화
    /// </summary>
    public class SpectatorHUD : MonoBehaviour
    {
        // UI 레퍼런스
        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text targetNameText;// 현재 관전 대상 이름

        [BoxGroup("참조")]
        [Required, SerializeField] private TMP_Text keyHintsText;// 조작 키 힌트 문구

        // 설정값
        [BoxGroup("설정")]
        [Tooltip("자유 카메라 모드일 때 표시할 문구")]
        [SerializeField] private string freeCameraLabel = "자유 카메라";

        [BoxGroup("설정")]
        [Tooltip("팀원 관전 시 표시할 형식입니다. {0}에는 clientId가 들어갑니다.")]
        [SerializeField] private string spectatingFormat = "관전중: Player {0}";

        [BoxGroup("설정")]
        [Tooltip("조작 키 힌트. 로컬라이징/키바인딩 변경 시 여기만 수정")]
        [MultiLineProperty(2), SerializeField]
        private string keyHintsLabel = "[Q/E] 팀원 전환   [Tab] 자유 카메라";

        // Feel 피드백
        [FoldoutGroup("피드백")]
        [Tooltip("팀원 관전 대상으로 전환 시 재생")]
        [SerializeField] private MMF_Player onTeammateTargetFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("자유 카메라로 전환 시 재생")]
        [SerializeField] private MMF_Player onFreeCameraFeedback;

        [FoldoutGroup("피드백")]
        [Tooltip("관전 시작(사망 진입 후 첫 대상 설정) 시 1회 재생")]
        [SerializeField] private MMF_Player onSpectateStartFeedback;

        // 런타임 상태 (디버그 표시용)
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private ulong currentTargetClientId;// 현재 관전 중인 대상 ID
        [TitleGroup("디버그")]
        [ShowInInspector, ReadOnly] private bool hasStartedSpectating;// 관전 시작 여부 (첫 진입 구분용)

        // 컴포넌트 활성화 시 EventBus 구독 + 키 힌트 초기화
        private void OnEnable()
        {
            EventBus.Subscribe<SpectatorTargetChangedEvent>(OnTargetChanged);
            if (keyHintsText != null) keyHintsText.text = keyHintsLabel;

            // Dead -> Revive -> Dead 재진입 시 첫 진입으로 다시 처리되도록 리셋
            hasStartedSpectating = false;
        }

        // 컴포넌트 비활성화 시 구독 해제
        private void OnDisable()
        {
            EventBus.Unsubscribe<SpectatorTargetChangedEvent>(OnTargetChanged);
        }

        // 관전 대상 변경 시 이름 갱신 + 상황별 피드백 재생
        private void OnTargetChanged(SpectatorTargetChangedEvent e)
        {
            if (NetworkManager.Singleton == null) return;
            if (e.spectatorClientId != NetworkManager.Singleton.LocalClientId) return;

            Debug.Log($"[SpectatorHUD] SpectatorTargetChanged spectator={e.spectatorClientId}, target={e.newTargetClientId}", this);

            currentTargetClientId = e.newTargetClientId;

            // ulong.MaxValue는 '자유 카메라 모드'를 의미하는 약속값
            if (targetNameText != null)
            {
                targetNameText.text = e.newTargetClientId == ulong.MaxValue
                    ? freeCameraLabel
                    : string.Format(spectatingFormat, e.newTargetClientId);
            }

            // 관전 첫 진입은 다른 피드백 사용 (전환과 구분)
            if (!hasStartedSpectating)
            {
                UIFeedbackTester.Play(onSpectateStartFeedback, this, "관전 시작");
                hasStartedSpectating = true;
                return;
            }

            if (e.newTargetClientId == ulong.MaxValue)
                UIFeedbackTester.Play(onFreeCameraFeedback, this, "자유 카메라");
            else
                UIFeedbackTester.Play(onTeammateTargetFeedback, this, "팀원 관전 전환");
        }

        // 에디터 전용 테스트 버튼
#if UNITY_EDITOR
        [TitleGroup("디버그")]
        [Button("관전 시작 피드백"), GUIColor(0.5f, 0.5f, 0.5f)]
        private void TestSpectateStart() => UIFeedbackTester.Play(onSpectateStartFeedback, this, "관전 시작");

        [TitleGroup("디버그")]
        [Button("팀원 관전 전환 피드백")]
        private void TestTeammateSwitch() => UIFeedbackTester.Play(onTeammateTargetFeedback, this, "팀원 관전 전환");

        [TitleGroup("디버그")]
        [Button("자유 카메라 피드백")]
        private void TestFreeCamera() => UIFeedbackTester.Play(onFreeCameraFeedback, this, "자유 카메라");
#endif
    }
}

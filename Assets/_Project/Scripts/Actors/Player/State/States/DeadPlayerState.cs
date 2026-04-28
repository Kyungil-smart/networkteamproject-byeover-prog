using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 영구 사망 상태.
    /// 게임플레이 입력과 전투 기능을 비활성화하고, CharacterController는 끄지 않은 채 이동만 잠근다.
    /// 시체 프리팹/본체 비활성화 정책은 네트워크 사망 처리 단계에서 별도로 결정한다.
    /// </summary>
    public class DeadPlayerState : PlayerStateBase
    {
        public override PlayerState State => PlayerState.Dead;

        public override void OnEnter(PlayerStateContext ctx)
        {
            if (ctx.Animator != null)
            {
                ctx.Animator.ResetTrigger(KnockdownHash);
                ctx.Animator.SetBool(IsKnockedHash, false);
                ctx.Animator.SetBool(IsDeadHash, true);
            }

            if (ctx.Roll != null)
            {
                ctx.Roll.CancelRoll();
                ctx.Roll.enabled = false;
            }

            if (ctx.Shooting != null) ctx.Shooting.enabled = false;
            if (ctx.Reload != null) ctx.Reload.enabled = false;

            if (ctx.ADS != null)
            {
                ctx.ADS.SetADS(false);
                ctx.ADS.enabled = false;
            }

            if (ctx.WeaponSwitching != null) ctx.WeaponSwitching.enabled = false;

            if (ctx.FPS != null)
            {
                ctx.FPS.enabled = true;
                ctx.FPS.SetCrawlMode(false);
                ctx.FPS.SetMoveLocked(true);
            }

            if (ctx.Interaction != null) ctx.Interaction.enabled = false;
        }

        public override void OnExit(PlayerStateContext ctx)
        {
            if (ctx.Animator != null) ctx.Animator.SetBool(IsDeadHash, false);
            if (ctx.FPS != null) ctx.FPS.SetMoveLocked(false);
        }
    }
}
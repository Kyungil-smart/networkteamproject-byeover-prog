using DeadZone.Core;

namespace DeadZone.Actors
{
    /// <summary>
    /// 다운(Knocked) 상태.
    /// 전투/구르기/상호작용은 막고, 기어가기 이동만 허용한다.
    /// </summary>
    public class KnockedPlayerState : PlayerStateBase
    {
        public override PlayerState State => PlayerState.Knocked;

        public override void OnEnter(PlayerStateContext ctx)
        {
            if (ctx.Animator != null)
            {
                ctx.Animator.SetBool(IsKnockedHash, true);
                ctx.Animator.SetBool(IsDeadHash, false);
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
                ctx.FPS.SetMoveLocked(false);
                ctx.FPS.SetCrawlMode(true);
                ctx.FPS.SetSprint(false);
            }

            if (ctx.Interaction != null)
            {
                ctx.Interaction.enabled = false;
            }
        }

        public override void OnExit(PlayerStateContext ctx)
        {
            if (ctx.Animator != null) ctx.Animator.SetBool(IsKnockedHash, false);

            if (ctx.FPS != null)
            {
                ctx.FPS.SetCrawlMode(false);
                ctx.FPS.SetMoveLocked(false);
            }
        }
    }
}
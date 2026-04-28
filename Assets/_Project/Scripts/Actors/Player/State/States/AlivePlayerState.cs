using DeadZone.Core;


namespace DeadZone.Actors
{
    /// <summary>
    /// 기본 상태. 모든 서브시스템 활성화.
    /// </summary>
    public class AlivePlayerState : PlayerStateBase
    {
        public override PlayerState State => PlayerState.Alive;

        public override void OnEnter(PlayerStateContext ctx)
        {
            if (ctx.Animator != null)
            {
                ctx.Animator.SetBool(IsKnockedHash, false);
                ctx.Animator.SetBool(IsDeadHash, false);
            }
            
            if (ctx.FPS != null)
            {
                ctx.FPS.enabled = true;
                ctx.FPS.SetMoveLocked(false);
                ctx.FPS.SetCrawlMode(false);
            }
            
            if (ctx.Roll != null) ctx.Roll.enabled = true;
            if (ctx.Shooting != null) ctx.Shooting.enabled = true;
            if (ctx.Reload != null) ctx.Reload.enabled = true;
            if (ctx.ADS != null) ctx.ADS.enabled = true;
            if (ctx.WeaponSwitching != null) ctx.WeaponSwitching.enabled = true;
            if (ctx.Interaction != null) ctx.Interaction.enabled = true;
        }

        public override void OnExit(PlayerStateContext ctx)
        {
            if (ctx.Animator == null) return;

            if (ctx.ToState == PlayerState.Knocked)
            {
                ctx.Animator.SetTrigger(KnockdownHash);
            }
        }

        private void SetCombatEnabled(PlayerStateContext ctx, bool enabled)
        {
            if (ctx.Shooting != null) ctx.Shooting.enabled = enabled;
            if (ctx.Reload != null) ctx.Reload.enabled = enabled;
            if (ctx.ADS != null) ctx.ADS.enabled = enabled;
            if (ctx.Roll != null) ctx.Roll.enabled = enabled;
            if (ctx.WeaponSwitching != null) ctx.WeaponSwitching.enabled = enabled;
        }
    }
}

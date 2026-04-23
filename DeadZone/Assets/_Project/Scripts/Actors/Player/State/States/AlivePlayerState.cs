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
            SetCombatEnabled(ctx, true);
            if (ctx.CharacterController != null) ctx.CharacterController.enabled = true;
        }

        public override void OnExit(PlayerStateContext ctx)
        {
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

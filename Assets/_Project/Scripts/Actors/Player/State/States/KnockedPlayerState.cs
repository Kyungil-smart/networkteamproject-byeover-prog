using DeadZone.Core;


namespace DeadZone.Actors
{
    /// <summary>
    /// 다운(Knocked) 상태. 전투 불가, FPSController가 기어가기 속도로 강제됨, 구르기 비활성.
    /// 플레이어는 느리게 이동할 수 있고 팀원이 부활시킬 수 있다.
    /// </summary>
    public class KnockedPlayerState : PlayerStateBase
    {
        public override PlayerState State => PlayerState.Knocked;

        public override void OnEnter(PlayerStateContext ctx)
        {
            if (ctx.Shooting != null) ctx.Shooting.enabled = false;
            if (ctx.Reload != null) ctx.Reload.enabled = false;
            if (ctx.ADS != null) { ctx.ADS.SetADS(false); ctx.ADS.enabled = false; }
            if (ctx.Roll != null) ctx.Roll.enabled = false;
            if (ctx.WeaponSwitching != null) ctx.WeaponSwitching.enabled = false;

            if (ctx.FPS != null) ctx.FPS.SetCrawlMode(true);
        }

        public override void OnExit(PlayerStateContext ctx)
        {
            if (ctx.FPS != null) ctx.FPS.SetCrawlMode(false);
        }
    }
}

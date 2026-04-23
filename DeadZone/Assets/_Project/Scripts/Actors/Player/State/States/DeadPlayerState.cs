using DeadZone.Core;


namespace DeadZone.Actors
{
    /// <summary>
    /// 영구 사망 상태. 모든 게임플레이 컴포넌트를 비활성화한다.
    /// CharacterController도 끄기 때문에 플레이어 바디가 아무것과도 충돌하지 않는다.
    /// 관전자 서브시스템(별도 컴포넌트)이 카메라 + 입력을 처리한다.
    /// </summary>
    public class DeadPlayerState : PlayerStateBase
    {
        public override PlayerState State => PlayerState.Dead;

        public override void OnEnter(PlayerStateContext ctx)
        {
            if (ctx.Shooting != null) ctx.Shooting.enabled = false;
            if (ctx.Reload != null) ctx.Reload.enabled = false;
            if (ctx.ADS != null) { ctx.ADS.SetADS(false); ctx.ADS.enabled = false; }
            if (ctx.Roll != null) ctx.Roll.enabled = false;
            if (ctx.WeaponSwitching != null) ctx.WeaponSwitching.enabled = false;
            if (ctx.FPS != null) ctx.FPS.enabled = false;
            if (ctx.Interaction != null) ctx.Interaction.enabled = false;
            if (ctx.CharacterController != null) ctx.CharacterController.enabled = false;
        }

        public override void OnExit(PlayerStateContext ctx)
        {
        }
    }
}

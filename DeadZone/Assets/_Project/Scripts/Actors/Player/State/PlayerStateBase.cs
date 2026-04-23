using DeadZone.Core;


namespace DeadZone.Actors
{
    /// <summary>
    /// 플레이어 상태의 베이스 클래스. 상태별로 서브클래스를 만들고 OnEnter/OnExit/OnUpdate를 오버라이드한다.
    /// 개방-폐쇄 원칙: 기존 상태를 건드리지 않고 새 상태를 추가할 수 있다.
    /// </summary>
    public abstract class PlayerStateBase
    {
        public abstract PlayerState State { get; }

        public virtual void OnEnter(PlayerStateContext ctx) { }
        public virtual void OnExit(PlayerStateContext ctx) { }
        public virtual void OnUpdate(PlayerStateContext ctx) { }
    }
}

namespace DeadZone.Core
{
    /// <summary>
    /// EventBus를 통해 발행되는 모든 이벤트의 마커 인터페이스.
    /// GC 할당을 피하기 위해 구현체는 반드시 struct여야 한다.
    /// </summary>
    public interface IGameEvent { }
}

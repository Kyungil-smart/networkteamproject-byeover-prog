using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// 입력 라우팅 전략. 상태가 바뀌면 다른 구현체로 교체된다.
    /// PlayerInputController가 이 메서드들을 호출하고, 각 컨텍스트가 무엇을 할지 결정한다.
    /// </summary>
    public interface IPlayerInputContext
    {
        void Tick(Vector2 move, Vector2 look, Vector2 mousePos);

        void OnFire();
        void OnAim(bool down);
        void OnReload();
        void OnInteract();
        void OnRoll();
        void OnSprint(bool down);
        void OnEquipSlot(WeaponSlot slot);

        void OnSpectatorNext();
        void OnSpectatorPrev();
        void OnSpectatorToggleMode();
    }
}

using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// Dead 상태 입력. 캐릭터 제어와 전투 입력은 막고, Q/E 팀원 관전 전환만 허용한다.
    /// </summary>
    public class SpectatorInputContext : IPlayerInputContext
    {
        private readonly SpectatorController spectator;

        public SpectatorInputContext(SpectatorController spectator)
        {
            this.spectator = spectator;
        }

        public void Tick(Vector2 move, Vector2 look, Vector2 mousePos)
        {
        }

        public void OnFireInput(bool pressedThisFrame, bool held, Vector2 mousePos) { }
        public void OnAim(bool down) { }
        public void OnReload() { }
        public void OnInteract() { }
        public void OnRoll() { }
        public void OnSprint(bool down) { }
        public void OnEquipSlot(WeaponSlot slot) { }

        public void OnSpectatorNext() => spectator?.SwitchTo(+1);
        public void OnSpectatorPrev() => spectator?.SwitchTo(-1);
        public void OnSpectatorToggleMode() { }
    }
}

using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// Dead 상태 입력. 이동 키는 프리캠용 시선 힌트로 SpectatorController에 전달되지만
    /// 캐릭터 제어는 일어나지 않는다.
    /// 발사/조준/재장전은 아무 것도 하지 않는다. Q/E로 팀원 전환.
    /// </summary>
    public class SpectatorInputContext : IPlayerInputContext
    {
        private readonly SpectatorController spectator;

        public SpectatorInputContext(SpectatorController spectator)
        {
            this.spectator = spectator;
        }

        public void Tick(Vector2 move, Vector2 look)
        {
            if (spectator != null) spectator.SetFreeCamInput(move, look);
        }

        public void OnFire() { }
        public void OnAim(bool down) { }
        public void OnReload() { }
        public void OnInteract() { }
        public void OnRoll() { }
        public void OnSprint(bool down) { }
        public void OnEquipSlot(WeaponSlot slot) { }

        public void OnSpectatorNext() => spectator?.SwitchTo(+1);
        public void OnSpectatorPrev() => spectator?.SwitchTo(-1);
        public void OnSpectatorToggleMode() => spectator?.ToggleFreeCam();
    }
}

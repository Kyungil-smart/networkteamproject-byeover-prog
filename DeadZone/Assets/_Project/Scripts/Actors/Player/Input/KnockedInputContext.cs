using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// Knocked 상태 입력. 이동만 허용 (느린 기어가기).
    /// 모든 전투 액션은 무시된다.
    /// </summary>
    public class KnockedInputContext : IPlayerInputContext
    {
        private readonly FPSController fps;

        public KnockedInputContext(FPSController fps)
        {
            this.fps = fps;
        }

        public void Tick(Vector2 move, Vector2 look)
        {
            if (fps != null)
            {
                fps.SetMove(move);
                fps.SetLook(look);
            }
        }

        public void OnFire() { }
        public void OnAim(bool down) { }
        public void OnReload() { }
        public void OnInteract() { }
        public void OnRoll() { }
        public void OnSprint(bool down) { }
        public void OnEquipSlot(WeaponSlot slot) { }

        public void OnSpectatorNext() { }
        public void OnSpectatorPrev() { }
        public void OnSpectatorToggleMode() { }
    }
}

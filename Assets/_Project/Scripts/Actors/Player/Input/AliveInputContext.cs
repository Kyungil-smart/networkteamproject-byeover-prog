using UnityEngine;


namespace DeadZone.Actors
{
    /// <summary>
    /// 기본 입력 동작. 모든 입력을 실제 서브시스템으로 라우팅한다.
    /// </summary>
    public class AliveInputContext : IPlayerInputContext
    {
        private readonly FPSController fps;
        private readonly ShootingSystem shooting;
        private readonly ADSSystem ads;
        private readonly ReloadSystem reload;
        private readonly RollSystem roll;
        private readonly InteractionSystem interaction;
        private readonly WeaponSwitching weaponSwitching;
        private Vector2 mousePos;

        public AliveInputContext(
            FPSController fps, ShootingSystem shooting, ADSSystem ads,
            ReloadSystem reload, RollSystem roll, InteractionSystem interaction,
            WeaponSwitching weaponSwitching)
        {
            this.fps = fps;
            this.shooting = shooting;
            this.ads = ads;
            this.reload = reload;
            this.roll = roll;
            this.interaction = interaction;
            this.weaponSwitching = weaponSwitching;
        }

        public void Tick(Vector2 move, Vector2 look, Vector2 mousePos)
        {
            if (fps != null)
            {
                fps.SetMove(move);
                fps.SetLook(look);
            }
            this.mousePos = mousePos;
        }

        /// <summary>
        /// 사격 입력 상태를 사격 시스템으로 전달한다.
        /// 클릭이 시작된 프레임에는 단발 사격을 먼저 시도하고, 이후 버튼이 유지되는 동안에는 Full 지원 무기만 연사 사격을 시도한다.
        /// </summary>
        public void OnFireInput(bool pressedThisFrame, bool held, Vector2 mousePos)
        {
            if (pressedThisFrame)
            {
                shooting?.TryFire(mousePos);
                return;
            }

            if (held)
            {
                shooting?.TryFullAutoFire(mousePos);
            }
        }
        public void OnAim(bool down) => ads?.SetADS(down);
        public void OnReload() => reload?.TryReload();
        public void OnInteract() => interaction?.TryInteract();
        public void OnRoll() => roll?.TryRoll();
        public void OnSprint(bool down) => fps?.SetSprint(down);
        public void OnEquipSlot(WeaponSlot slot) => weaponSwitching?.RequestEquip(slot);

        public void OnSpectatorNext() { }
        public void OnSpectatorPrev() { }
        public void OnSpectatorToggleMode() { }
    }
}

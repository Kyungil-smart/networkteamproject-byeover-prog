using Unity.Netcode;

namespace DeadZone.Actors
{
    public class ADSSystem : NetworkBehaviour
    {
        public bool IsADS { get; private set; }
        public void SetADS(bool aiming) { if (IsOwner) IsADS = aiming; }
    }
}
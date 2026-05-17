using Unity.Netcode;

using DeadZone.Core;

namespace DeadZone.Actors
{
    public class ADSSystem : NetworkBehaviour
    {
        public bool IsADS { get; private set; }

        public void SetADS(bool aiming)
        {
            if (!IsOwner)
                return;

            if (IsADS == aiming)
                return;

            IsADS = aiming;
            EventBus.Publish(new ADSStateChangedEvent
            {
                clientId = OwnerClientId,
                isAiming = aiming,
            });
        }
    }
}

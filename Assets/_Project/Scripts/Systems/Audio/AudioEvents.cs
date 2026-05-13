using UnityEngine;

using DeadZone.Core;

namespace DeadZone.Systems.Audio
{
    public struct AudioPlayRequestedEvent : IGameEvent
    {
        public AudioCueId cueId;
        public Vector3 position;
        public bool use3D;
        public float volumeMultiplier;
    }

    public struct AudioStopRequestedEvent : IGameEvent
    {
        public AudioCueId cueId;
    }

    public struct BgmChangeRequestedEvent : IGameEvent
    {
        public AudioCueId cueId;
        public bool fade;
    }
}

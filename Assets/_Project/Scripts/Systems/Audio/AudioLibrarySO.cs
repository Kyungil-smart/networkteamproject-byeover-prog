using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Systems.Audio
{
    [CreateAssetMenu(menuName = "DeadZone/Audio/Audio Library", fileName = "AudioLibrary")]
    public sealed class AudioLibrarySO : ScriptableObject
    {
        [Header("====사운드 목록====")]
        [Tooltip("게임에서 사용할 모든 사운드 설정, cueId가 중복되지 않도록 설정")]
        [SerializeField] private List<AudioCueData> cues = new();

        private Dictionary<AudioCueId, AudioCueData> lookup;

        public IReadOnlyList<AudioCueData> Cues => cues;

        public bool TryGetCue(AudioCueId cueId, out AudioCueData cue)
        {
            EnsureLookup();
            return lookup.TryGetValue(cueId, out cue) && cue != null;
        }

        public AudioCueData GetCue(AudioCueId cueId)
        {
            return TryGetCue(cueId, out AudioCueData cue) ? cue : null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
                return;

            lookup = new Dictionary<AudioCueId, AudioCueData>();
            if (cues == null)
                return;

            for (int i = 0; i < cues.Count; i++)
            {
                AudioCueData cue = cues[i];
                if (cue == null || cue.cueId == AudioCueId.None)
                    continue;

                if (lookup.ContainsKey(cue.cueId))
                {
                    Debug.LogWarning($"[AudioLibrarySO] 중복 CueId 무시: {cue.cueId}", this);
                    continue;
                }

                lookup.Add(cue.cueId, cue);
            }
        }

        private void OnValidate()
        {
            lookup = null;

            if (cues == null)
                return;

            for (int i = 0; i < cues.Count; i++)
            {
                AudioCueData cue = cues[i];
                if (cue == null)
                    continue;

                cue.maxPitch = Mathf.Max(cue.minPitch, cue.maxPitch);
                cue.maxDistance = Mathf.Max(0.01f, cue.maxDistance);
            }
        }
    }
}

using System;
using UnityEngine;

namespace DeadZone.Systems.Audio
{
    public enum AudioGroup
    {
        SFX = 0,
        BGM = 1,
        UI = 2,
    }

    // AudioLibrarySO 안에서 한 사운드의 재생 설정을 담는 데이터
    [Serializable]
    public sealed class AudioCueData
    {
        [Tooltip("AudioManager가 찾을 사운드 ID. 같은 ID가 여러 개 있으면 먼저 발견된 항목만 사용")]
        public AudioCueId cueId;

        [Tooltip("인스펙터에서 구분하기 위한 이름")]
        public string displayName;

        [Tooltip("사운드가 속한 볼륨 그룹")]
        public AudioGroup group = AudioGroup.SFX;

        [Tooltip("재생할 AudioClip 목록, 여러 개를 넣으면 재생할 때마다 무작위로 하나를 고릅니다.")]
        public AudioClip[] clips;

        [Tooltip("라이브러리 기준 기본 볼륨,. 0이면 무음, 1이면 원본 크기")]
        [Range(0f, 1f)] public float baseVolume = 1f;

        [Tooltip("개별 사운드 볼륨, 옵션 메뉴나 밸런싱에서 이 사운드만 따로 줄일 때 사용")]
        [Range(0f, 1f)] public float individualVolume = 1f;

        [Tooltip("켜면 월드 위치에서 들리는 3D 사운드로 재생, 끄면 UI처럼 2D 사운드로 재생")]
        public bool use3D;

        [Tooltip("켜면 반복 재생, 보통 BGM에만 사용")]
        public bool loop;

        [Tooltip("재생할 때 무작위로 적용할 최소 피치")]
        [Range(0.1f, 3f)] public float minPitch = 1f;

        [Tooltip("재생할 때 무작위로 적용할 최대 피치, 최소 피치보다 작으면 최소 피치로 보정")]
        [Range(0.1f, 3f)] public float maxPitch = 1f;

        [Tooltip("3D 사운드가 가장 크게 들리는 거리")]
        [Min(0f)] public float minDistance = 1f;

        [Tooltip("3D 사운드가 들리는 최대 거리")]
        [Min(0.01f)] public float maxDistance = 35f;

        [Tooltip("BGM 전환 시 사용할 페이드 시간")]
        [Min(0f)] public float fadeSeconds = 0.5f;

        public AudioClip GetRandomClip()
        {
            if (clips == null || clips.Length == 0)
                return null;

            if (clips.Length == 1)
                return clips[0];

            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }

        public float GetPitch()
        {
            float min = Mathf.Max(0.1f, minPitch);
            float max = Mathf.Max(min, maxPitch);
            return Mathf.Approximately(min, max) ? min : UnityEngine.Random.Range(min, max);
        }
    }
}

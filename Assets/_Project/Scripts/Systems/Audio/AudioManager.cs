using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

using DeadZone.Core;

namespace DeadZone.Systems.Audio
{
    // 게임 전체 오디오를 관리하는 싱글톤 매니저
    // 1차 연결 범위: UI 클릭음, BGM, 파밍음, 플레이어 부상음.
    // 발사/장전/발각/발걸음은 이벤트와 ID만 준비하고 추후 각 시스템에서 이벤트 발행을 연결
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [Serializable]
        private sealed class SceneBgmMapping
        {
            [Tooltip("BGM을 자동 재생할 씬 이름")]
            public string sceneName;

            [Tooltip("해당 씬에서 재생할 BGM ID")]
            public AudioCueId bgmCueId;
        }

        [Serializable]
        private sealed class CueVolumeOverride
        {
            [Tooltip("개별 볼륨을 덮어쓸 사운드 ID")]
            public AudioCueId cueId;

            [Tooltip("이 사운드에만 적용할 개별 볼륨")]
            [Range(0f, 1f)] public float volume = 1f;
        }

        public static AudioManager Instance { get; private set; }

        [Header("====사운드 라이브러리====")]
        [Tooltip("AudioCueId별 AudioClip과 재생 설정을 담은 ScriptableObject.")]
        [SerializeField] private AudioLibrarySO audioLibrary;

        [Header("====전체 볼륨====")]
        [Tooltip("모든 사운드에 적용되는 최종 마스터 볼륨")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

        [Tooltip("BGM 그룹에 적용되는 볼륨")]
        [SerializeField, Range(0f, 1f)] private float bgmVolume = 1f;

        [Tooltip("게임 효과음 그룹에 적용되는 볼륨")]
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

        [Tooltip("UI 효과음 그룹에 적용되는 볼륨")]
        [SerializeField, Range(0f, 1f)] private float uiVolume = 1f;

        [Header("====개별 볼륨====")]
        [Tooltip("특정 사운드만 따로 조절할 때 사용, 비어 있으면 AudioLibrarySO의 개별 볼륨을 사용")]
        [SerializeField] private List<CueVolumeOverride> cueVolumeOverrides = new();

        [Header("====BGM 자동 재생====")]
        [Tooltip("씬이 로드될 때 씬 이름에 맞는 BGM을 자동으로 재생")]
        [SerializeField] private bool playBgmByScene = true;

        [Tooltip("씬 이름과 BGM ID 매핑")]
        [SerializeField] private List<SceneBgmMapping> sceneBgmMappings = new()
        {
            new SceneBgmMapping { sceneName = "Title", bgmCueId = AudioCueId.TitleBGM },
            new SceneBgmMapping { sceneName = "Game_Stage_1", bgmCueId = AudioCueId.Stage1BGM },
            new SceneBgmMapping { sceneName = "Game_Stage_2", bgmCueId = AudioCueId.Stage2BGM },
            new SceneBgmMapping { sceneName = "Ending", bgmCueId = AudioCueId.EndingBGM },
        };

        [Header("====자동 연결====")]
        [Tooltip("ItemLootedEvent를 받아 파밍 사운드1/2 중 하나를 재생")]
        [SerializeField] private bool playLootSoundFromEvent = true;

        [Tooltip("PlayerHpChangedEvent를 받아 로컬 플레이어 체력이 감소하면 부상음을 재생")]
        [SerializeField] private bool playPlayerInjuredFromEvent = true;

        [Tooltip("네트워크 세션 중에는 로컬 플레이어의 체력 감소 이벤트에만 부상음을 재생")]
        [SerializeField] private bool onlyLocalPlayerInjurySound = true;

        [Header("====3D 효과음 풀====")]
        [Tooltip("3D 사운드 재생에 미리 만들어 둘 AudioSource 개수")]
        [SerializeField, Min(1)] private int pooledSourceCount = 16;

        [Tooltip("3D 사운드 AudioSource가 부족할 때 추가 생성을 허용")]
        [SerializeField] private bool expandPoolWhenNeeded = true;

        [Header("====디버그====")]
        [Tooltip("사운드 재생 실패 이유를 콘솔에 출력, 클립 연결 전에는 꺼두는 것을 권장")]
        [SerializeField] private bool logMissingCue;

        private readonly List<AudioSource> pooledSources = new();
        private AudioSource bgmSource;
        private AudioSource sfx2DSource;
        private Coroutine bgmFadeRoutine;
        private AudioCueId currentBgmCueId = AudioCueId.None;

        public float MasterVolume => masterVolume;
        public float BgmVolume => bgmVolume;
        public float SfxVolume => sfxVolume;
        public float UiVolume => uiVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ServiceLocator.Register(this);

            CreateAudioSources();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<AudioPlayRequestedEvent>(OnAudioPlayRequested);
            EventBus.Subscribe<AudioStopRequestedEvent>(OnAudioStopRequested);
            EventBus.Subscribe<BgmChangeRequestedEvent>(OnBgmChangeRequested);
            EventBus.Subscribe<ItemLootedEvent>(OnItemLooted);
            EventBus.Subscribe<PlayerHpChangedEvent>(OnPlayerHpChanged);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (playBgmByScene)
                TryPlayBgmForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<AudioPlayRequestedEvent>(OnAudioPlayRequested);
            EventBus.Unsubscribe<AudioStopRequestedEvent>(OnAudioStopRequested);
            EventBus.Unsubscribe<BgmChangeRequestedEvent>(OnBgmChangeRequested);
            EventBus.Unsubscribe<ItemLootedEvent>(OnItemLooted);
            EventBus.Unsubscribe<PlayerHpChangedEvent>(OnPlayerHpChanged);

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                ServiceLocator.Unregister<AudioManager>();
                Instance = null;
            }
        }

        public void Play(AudioCueId cueId, Vector3 position = default, bool force3D = false, float volumeMultiplier = 1f)
        {
            if (cueId == AudioCueId.None)
                return;

            if (audioLibrary == null || !audioLibrary.TryGetCue(cueId, out AudioCueData cue))
            {
                LogMissing($"Cue를 찾을 수 없습니다. cueId={cueId}");
                return;
            }

            AudioClip clip = cue.GetRandomClip();
            if (clip == null)
            {
                LogMissing($"AudioClip이 연결되지 않았습니다. cueId={cueId}");
                return;
            }

            if (cue.group == AudioGroup.BGM)
            {
                PlayBgm(cueId, useFade: true);
                return;
            }

            float finalVolume = CalculateFinalVolume(cue, Mathf.Max(0f, volumeMultiplier));
            if (finalVolume <= 0f)
                return;

            bool use3D = force3D || cue.use3D;
            if (use3D)
            {
                Play3D(cue, clip, position, finalVolume);
            }
            else
            {
                sfx2DSource.pitch = cue.GetPitch();
                sfx2DSource.PlayOneShot(clip, finalVolume);
            }
        }

        public void PlayBgm(AudioCueId cueId, bool useFade)
        {
            if (cueId == AudioCueId.None)
            {
                StopBgm(useFade);
                return;
            }

            if (currentBgmCueId == cueId && bgmSource.isPlaying)
                return;

            if (audioLibrary == null || !audioLibrary.TryGetCue(cueId, out AudioCueData cue))
            {
                LogMissing($"BGM Cue를 찾을 수 없습니다. cueId={cueId}");
                return;
            }

            AudioClip clip = cue.GetRandomClip();
            if (clip == null)
            {
                LogMissing($"BGM AudioClip이 연결되지 않았습니다. cueId={cueId}");
                return;
            }

            if (bgmFadeRoutine != null)
                StopCoroutine(bgmFadeRoutine);

            currentBgmCueId = cueId;

            if (useFade && cue.fadeSeconds > 0f)
                bgmFadeRoutine = StartCoroutine(FadeToBgm(cue, clip));
            else
                ApplyBgmImmediate(cue, clip);
        }

        public void StopBgm(bool useFade)
        {
            if (bgmFadeRoutine != null)
                StopCoroutine(bgmFadeRoutine);

            if (useFade && bgmSource.isPlaying)
                bgmFadeRoutine = StartCoroutine(FadeOutBgm(0.35f));
            else
                bgmSource.Stop();

            currentBgmCueId = AudioCueId.None;
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            RefreshBgmVolume();
        }

        public void SetBgmVolume(float value)
        {
            bgmVolume = Mathf.Clamp01(value);
            RefreshBgmVolume();
        }

        public void SetSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);
        }

        public void SetUiVolume(float value)
        {
            uiVolume = Mathf.Clamp01(value);
        }

        public void SetCueVolume(AudioCueId cueId, float value)
        {
            if (cueId == AudioCueId.None)
                return;

            CueVolumeOverride existing = cueVolumeOverrides.Find(x => x != null && x.cueId == cueId);
            if (existing == null)
            {
                existing = new CueVolumeOverride { cueId = cueId };
                cueVolumeOverrides.Add(existing);
            }

            existing.volume = Mathf.Clamp01(value);
            RefreshBgmVolume();
        }

        private void OnAudioPlayRequested(AudioPlayRequestedEvent e)
        {
            Play(e.cueId, e.position, e.use3D, e.volumeMultiplier <= 0f ? 1f : e.volumeMultiplier);
        }

        private void OnAudioStopRequested(AudioStopRequestedEvent e)
        {
            if (e.cueId == currentBgmCueId)
                StopBgm(useFade: true);
        }

        private void OnBgmChangeRequested(BgmChangeRequestedEvent e)
        {
            PlayBgm(e.cueId, e.fade);
        }

        private void OnItemLooted(ItemLootedEvent e)
        {
            if (!playLootSoundFromEvent)
                return;

            Play(UnityEngine.Random.value < 0.5f ? AudioCueId.Loot1 : AudioCueId.Loot2);
        }

        private void OnPlayerHpChanged(PlayerHpChangedEvent e)
        {
            if (!playPlayerInjuredFromEvent || e.newValue >= e.oldValue)
                return;

            if (onlyLocalPlayerInjurySound && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (e.clientId != NetworkManager.Singleton.LocalClientId)
                    return;
            }

            Play(AudioCueId.PlayerInjured);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (playBgmByScene)
                TryPlayBgmForScene(scene.name);
        }

        private void TryPlayBgmForScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || sceneBgmMappings == null)
                return;

            for (int i = 0; i < sceneBgmMappings.Count; i++)
            {
                SceneBgmMapping mapping = sceneBgmMappings[i];
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.sceneName))
                    continue;

                if (!string.Equals(mapping.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                    continue;

                PlayBgm(mapping.bgmCueId, useFade: true);
                return;
            }
        }

        private void CreateAudioSources()
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f;

            sfx2DSource = gameObject.AddComponent<AudioSource>();
            sfx2DSource.playOnAwake = false;
            sfx2DSource.loop = false;
            sfx2DSource.spatialBlend = 0f;

            int count = Mathf.Max(1, pooledSourceCount);
            for (int i = 0; i < count; i++)
                pooledSources.Add(CreatePooledSource());
        }

        private AudioSource CreatePooledSource()
        {
            GameObject sourceObject = new GameObject("PooledAudioSource");
            sourceObject.transform.SetParent(transform);

            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            return source;
        }

        private void Play3D(AudioCueData cue, AudioClip clip, Vector3 position, float volume)
        {
            AudioSource source = GetAvailableSource();
            if (source == null)
                return;

            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.pitch = cue.GetPitch();
            source.loop = cue.loop;
            source.spatialBlend = 1f;
            source.minDistance = cue.minDistance;
            source.maxDistance = cue.maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.Play();
        }

        private AudioSource GetAvailableSource()
        {
            for (int i = 0; i < pooledSources.Count; i++)
            {
                AudioSource source = pooledSources[i];
                if (source != null && !source.isPlaying)
                    return source;
            }

            if (!expandPoolWhenNeeded)
                return null;

            AudioSource newSource = CreatePooledSource();
            pooledSources.Add(newSource);
            return newSource;
        }

        private float CalculateFinalVolume(AudioCueData cue, float volumeMultiplier)
        {
            return masterVolume
                   * GetGroupVolume(cue.group)
                   * cue.baseVolume
                   * GetCueVolume(cue)
                   * volumeMultiplier;
        }

        private float GetGroupVolume(AudioGroup group)
        {
            return group switch
            {
                AudioGroup.BGM => bgmVolume,
                AudioGroup.UI => uiVolume,
                _ => sfxVolume,
            };
        }

        private float GetCueVolume(AudioCueData cue)
        {
            CueVolumeOverride volumeOverride = cueVolumeOverrides.Find(x => x != null && x.cueId == cue.cueId);
            return volumeOverride != null ? volumeOverride.volume : cue.individualVolume;
        }

        private IEnumerator FadeToBgm(AudioCueData cue, AudioClip clip)
        {
            float duration = Mathf.Max(0.01f, cue.fadeSeconds);
            float startVolume = bgmSource.volume;

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                bgmSource.volume = Mathf.Lerp(startVolume, 0f, t / duration);
                yield return null;
            }

            ApplyBgmImmediate(cue, clip);
            float targetVolume = CalculateFinalVolume(cue, 1f);

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                bgmSource.volume = Mathf.Lerp(0f, targetVolume, t / duration);
                yield return null;
            }

            bgmSource.volume = targetVolume;
            bgmFadeRoutine = null;
        }

        private IEnumerator FadeOutBgm(float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float startVolume = bgmSource.volume;

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                bgmSource.volume = Mathf.Lerp(startVolume, 0f, t / duration);
                yield return null;
            }

            bgmSource.Stop();
            bgmFadeRoutine = null;
        }

        private void ApplyBgmImmediate(AudioCueData cue, AudioClip clip)
        {
            bgmSource.clip = clip;
            bgmSource.loop = true;
            bgmSource.pitch = cue.GetPitch();
            bgmSource.volume = CalculateFinalVolume(cue, 1f);
            bgmSource.Play();
        }

        private void RefreshBgmVolume()
        {
            if (currentBgmCueId == AudioCueId.None || audioLibrary == null)
                return;

            if (audioLibrary.TryGetCue(currentBgmCueId, out AudioCueData cue))
                bgmSource.volume = CalculateFinalVolume(cue, 1f);
        }

        private void LogMissing(string message)
        {
            if (logMissingCue)
                Debug.LogWarning($"[AudioManager] {message}", this);
        }
    }
}

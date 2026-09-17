using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace GameAudio
{
    public enum SoundCue
    {
        Dig,
        Collapse,
        PlayerHurt,
        PlayerDeath,
        PlayerLand,
        PlayerRun,
        Explosion,
        DynamiteTick,
    }

    /// <summary>Resources/Sounds에서 효과음과 배경음악을 로드하는 클라이언트 재생 도우미.</summary>
    public static class GameSound
    {
        private const string DefaultMusicName = "bgm";
        private const string MenuMusicName = "menu_bgm";
        private const string InGameMusicName = "ingame_bgm";
        private const float DefaultMusicVolume = 1f;
        private const float InGameMusicVolume = 0.5f;
        private const float EffectMinDistance = 2f;
        private const float EffectMaxDistance = 18f;
        private const float DefaultEffectVolumeScale = 1f;
        private const float PlayerReactionVolumeScale = 3.5f;
        private static readonly string[] Names =
            { "dig", "collapse", "player_hurt", "player_death", "player_land", "player_run", "explosion", "dynamite_tick" };
        private static readonly string[] VariantFolders =
            { "DigVariants", "CollapseVariants", "HurtVariants", "DeathVariants", "LandVariants", "RunVariants", "ExplosionVariants", null };
        private static AudioClip[][] clips;
        private static int[] lastPlayedVariantIndices;
        private static float[] nextPlayTimes;
        private static AudioSource musicSource;
        private static readonly HashSet<AudioSource> activeEffectSources = new();
        private static AudioListener activeListener;
        private static bool didWarnMissingListener;
        private static float musicVolume = 1f;
        private static float effectVolume = 1f;
        private static float sceneMusicVolume = 1f;

        public static float MasterVolume => AudioListener.volume;
        public static float MusicVolume => musicVolume;
        public static float EffectVolume => effectVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            clips = new AudioClip[Names.Length][];
            lastPlayedVariantIndices = new int[Names.Length];
            nextPlayTimes = new float[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                lastPlayedVariantIndices[i] = -1;
                AudioClip[] variants = string.IsNullOrEmpty(VariantFolders[i])
                    ? null
                    : Resources.LoadAll<AudioClip>("Sounds/" + VariantFolders[i]);
                if (variants != null && variants.Length > 0)
                {
                    clips[i] = variants;
                    continue;
                }

                AudioClip singleClip = Resources.Load<AudioClip>("Sounds/" + Names[i]);
                clips[i] = singleClip != null ? new[] { singleClip } : System.Array.Empty<AudioClip>();
                if (singleClip == null)
                {
                    string variantHint = string.IsNullOrEmpty(VariantFolders[i])
                        ? string.Empty
                        : $" 또는 Assets/Resources/Sounds/{VariantFolders[i]}/ 폴더";
                    Debug.LogError($"[GameSound] 사운드 파일 누락: Assets/Resources/Sounds/{Names[i]}.wav" +
                        variantHint + "에 오디오 파일을 넣으세요 (.wav, .ogg, .mp3 지원). " +
                        "파일을 넣고 Play Mode를 다시 시작하세요.");
                }
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            PlayMusicForScene(SceneManager.GetActiveScene());
        }

        public static void Play(SoundCue cue, Vector3 worldPosition)
        {
            if (clips == null) Initialize();
            int index = (int)cue;
            AudioClip[] cueClips = clips[index];
            if (cueClips.Length == 0 || Time.time < nextPlayTimes[index]) return;
            int variantIndex = ChooseVariantIndex(index, cueClips.Length);
            AudioClip clip = cueClips[variantIndex];
            // 한 번의 붕괴에서 여러 청크가 생성되어도 소리가 과도하게 겹치지 않게 한다.
            if (cue == SoundCue.Collapse)
                nextPlayTimes[index] = Time.time + 0.06f;
            GameObject voice = new GameObject("Sound_" + Names[index]);
            voice.transform.position = ResolveEffectPosition(worldPosition);
            AudioSource source = voice.AddComponent<AudioSource>();
            ConfigureEffectSource(source);
            activeEffectSources.Add(source);
            source.clip = clip;
            float volumeScale = cue == SoundCue.PlayerHurt || cue == SoundCue.PlayerDeath
                ? PlayerReactionVolumeScale
                : DefaultEffectVolumeScale;
            source.PlayOneShot(clip, volumeScale);
            Object.Destroy(voice, clip.length + 0.1f);
        }

        /// <summary>대상을 따라다니며 반복되는 거리 감쇠 효과음을 재생한다.</summary>
        public static AudioSource PlayLoop(SoundCue cue, Transform followTarget)
        {
            if (followTarget == null) return null;
            if (clips == null) Initialize();

            int index = (int)cue;
            AudioClip[] cueClips = clips[index];
            if (cueClips.Length == 0) return null;

            int variantIndex = ChooseVariantIndex(index, cueClips.Length);
            GameObject voice = new GameObject("LoopSound_" + Names[index]);
            voice.transform.position = ResolveEffectPosition(followTarget.position);
            voice.transform.SetParent(followTarget, true);

            AudioSource source = voice.AddComponent<AudioSource>();
            ConfigureEffectSource(source);
            activeEffectSources.Add(source);
            source.loop = true;
            source.clip = cueClips[variantIndex];
            source.Play();
            return source;
        }

        /// <summary>거리 감쇠 없이 반복되는 2D 효과음을 지정 배율로 재생한다.</summary>
        public static AudioSource PlayGlobalLoop(
            SoundCue cue,
            float volumeScale,
            Transform owner = null)
        {
            if (clips == null) Initialize();

            int index = (int)cue;
            AudioClip[] cueClips = clips[index];
            if (cueClips.Length == 0 || volumeScale <= 0f) return null;

            int variantIndex = ChooseVariantIndex(index, cueClips.Length);
            AudioClip clip = cueClips[variantIndex];
            GameObject voice = new GameObject("GlobalLoopSound_" + Names[index]);
            if (owner != null) voice.transform.SetParent(owner, false);

            // AudioSource.volume은 최대 1이므로, 1을 넘는 배율은 동기화된 소스를 합산한다.
            float remainingVolume = Mathf.Clamp(volumeScale, 0f, 4f);
            AudioSource primarySource = null;
            double startTime = AudioSettings.dspTime + 0.05d;
            while (remainingVolume > 0f)
            {
                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.loop = true;
                source.clip = clip;
                source.volume = Mathf.Min(1f, remainingVolume);
                source.PlayScheduled(startTime);
                primarySource ??= source;
                remainingVolume -= source.volume;
            }

            return primarySource;
        }

        public static void StopLoop(AudioSource source)
        {
            if (source == null) return;
            activeEffectSources.Remove(source);
            source.Stop();
            Object.Destroy(source.gameObject);
        }

        private static Vector3 ResolveEffectPosition(Vector3 worldPosition)
        {
            AudioListener listener = GetActiveListener();
            if (listener != null)
            {
                worldPosition.z = listener.transform.position.z;
                didWarnMissingListener = false;
            }
            else if (didWarnMissingListener == false)
            {
                didWarnMissingListener = true;
                Debug.LogWarning(
                    "[GameSound] 활성 AudioListener를 찾지 못해 효과음의 2D 거리 감쇠 평면을 보정할 수 없습니다.");
            }

            return worldPosition;
        }

        private static AudioListener GetActiveListener()
        {
            if (activeListener != null && activeListener.isActiveAndEnabled)
                return activeListener;

            activeListener = null;
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (AudioListener listener in listeners)
            {
                // FindObjectsByType는 활성 GameObject의 비활성 Behaviour도 반환할 수 있다.
                // 카메라 씬에는 비활성 Cinemachine Listener와 실제 Main Camera Listener가
                // 함께 있으므로 enabled까지 확인해야 거리 기준 평면이 안정적이다.
                if (listener != null && listener.isActiveAndEnabled)
                {
                    activeListener = listener;
                    break;
                }
            }

            return activeListener;
        }

        private static void ConfigureEffectSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.volume = effectVolume;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = EffectMinDistance;
            source.maxDistance = EffectMaxDistance;
            source.dopplerLevel = 0f;
        }

        private static int ChooseVariantIndex(int cueIndex, int variantCount)
        {
            if (variantCount <= 1) return 0;

            int previousIndex = lastPlayedVariantIndices[cueIndex];
            if (previousIndex < 0)
            {
                int firstIndex = Random.Range(0, variantCount);
                lastPlayedVariantIndices[cueIndex] = firstIndex;
                return firstIndex;
            }

            int selectedIndex = Random.Range(0, variantCount - 1);
            if (selectedIndex >= previousIndex) selectedIndex++;
            lastPlayedVariantIndices[cueIndex] = selectedIndex;
            return selectedIndex;
        }

        /// <summary>
        /// Resources/Sounds에서 배경음악을 불러와 반복 재생한다.
        /// 이름을 생략하면 bgm 파일을 사용한다.
        /// </summary>
        public static void PlayMusic(string name = DefaultMusicName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning("[GameSound] 재생할 배경음악 이름이 비어 있습니다.");
                return;
            }

            if (TryPlayMusic(name)) return;

            Debug.LogWarning($"[GameSound] 배경음악 파일 누락: Assets/Resources/Sounds/{name}.wav " +
                "(동일한 이름의 .ogg 또는 .mp3도 가능).");
        }

        private static bool TryPlayMusic(string name)
        {
            AudioClip music = Resources.Load<AudioClip>("Sounds/" + name);
            if (music == null) return false;

            EnsureMusicSource();
            if (musicSource.clip == music && musicSource.isPlaying)
            {
                ApplyMusicVolume();
                return true;
            }

            musicSource.clip = music;
            musicSource.Play();
            ApplyMusicVolume();
            return true;
        }

        public static void StopMusic()
        {
            if (musicSource == null) return;
            musicSource.Stop();
            musicSource.clip = null;
        }

        public static void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            ApplyMusicVolume();
        }

        public static void SetMasterVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
        }

        public static void SetEffectVolume(float volume)
        {
            effectVolume = Mathf.Clamp01(volume);
            activeEffectSources.RemoveWhere(source => source == null);
            foreach (AudioSource source in activeEffectSources)
                source.volume = effectVolume;
        }

        private static void ApplyMusicVolume()
        {
            if (musicSource != null)
                musicSource.volume = sceneMusicVolume * musicVolume;
        }

        private static void EnsureMusicSource()
        {
            if (musicSource != null) return;

            GameObject musicObject = new GameObject("BackgroundMusic");
            Object.DontDestroyOnLoad(musicObject);
            musicSource = musicObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && IsMusicScene(scene.name) == false) return;
            PlayMusicForScene(scene);
        }

        private static bool IsMusicScene(string sceneName)
        {
            return sceneName == "MainMenuScene" ||
                   sceneName == "RoomLobbyScene" ||
                   sceneName == "InGame";
        }

        private static void PlayMusicForScene(Scene scene)
        {
            string musicName;
            float musicVolume = DefaultMusicVolume;
            switch (scene.name)
            {
                case "MainMenuScene":
                case "RoomLobbyScene":
                    musicName = MenuMusicName;
                    break;
                case "InGame":
                    musicName = InGameMusicName;
                    musicVolume = InGameMusicVolume;
                    break;
                default:
                    musicName = DefaultMusicName;
                    break;
            }

            if (TryPlayMusic(musicName))
            {
                sceneMusicVolume = musicVolume;
                ApplyMusicVolume();
                return;
            }
            if (musicName != DefaultMusicName && TryPlayMusic(DefaultMusicName))
            {
                sceneMusicVolume = musicVolume;
                ApplyMusicVolume();
                Debug.LogWarning($"[GameSound] {scene.name} 전용 배경음악 '{musicName}'이 없어 " +
                    $"기본 배경음악 '{DefaultMusicName}'을 재생합니다.");
                return;
            }

            StopMusic();
            Debug.LogWarning($"[GameSound] 배경음악 파일 누락: Assets/Resources/Sounds/{musicName}.wav " +
                "(동일한 이름의 .ogg 또는 .mp3도 가능).");
        }
    }
}

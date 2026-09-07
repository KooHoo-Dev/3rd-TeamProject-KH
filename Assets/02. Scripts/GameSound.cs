using UnityEngine;

namespace GameAudio
{
    public enum SoundCue { Dig, Collapse, PlayerHurt, PlayerDeath, PlayerLand, PlayerRun }

    /// <summary>Resources/Sounds에서 효과음을 로드하는 클라이언트 재생 전용 도우미.</summary>
    public static class GameSound
    {
        private static readonly string[] Names =
            { "dig", "collapse", "player_hurt", "player_death", "player_land", "player_run" };
        private static AudioClip[] clips;
        private static float[] nextPlayTimes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            clips = new AudioClip[Names.Length];
            nextPlayTimes = new float[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                clips[i] = Resources.Load<AudioClip>("Sounds/" + Names[i]);
                if (clips[i] == null)
                    Debug.LogError($"[GameSound] 사운드 파일 누락: Assets/Resources/Sounds/{Names[i]}.wav " +
                        "(동일한 이름의 .ogg 또는 .mp3도 가능). 파일을 넣고 Play Mode를 다시 시작하세요.");
            }
        }

        public static void Play(SoundCue cue)
        {
            if (clips == null) Initialize();
            int index = (int)cue;
            AudioClip clip = clips[index];
            if (clip == null || Time.time < nextPlayTimes[index]) return;
            // 한 번의 붕괴에서 여러 청크가 생성되어도 소리가 과도하게 겹치지 않게 한다.
            nextPlayTimes[index] = Time.time + 0.06f;
            GameObject voice = new GameObject("Sound_" + Names[index]);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.clip = clip;
            source.Play();
            Object.Destroy(voice, clip.length + 0.1f);
        }
    }
}

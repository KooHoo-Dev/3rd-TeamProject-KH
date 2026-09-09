using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameAudio;

namespace TerrainCollapse
{
    /// <summary>
    /// 화면 모드와 창 모드 해상도를 관리한다.
    /// 전체화면/전체 창 모드는 현재 모니터 해상도를 사용하며, 창 모드에서만 지정 해상도를 적용한다.
    /// </summary>
    public sealed class SettingsController : MonoBehaviour
    {
        private const string ScreenModePreferenceKey = "ScreenMode";
        private const string ResolutionPreferenceKey = "WindowResolution";
        private const string MasterVolumePreferenceKey = "MasterVolume";
        private const string MusicVolumePreferenceKey = "MusicVolume";
        private const string EffectVolumePreferenceKey = "EffectVolume";

        private static readonly Vector2Int[] WindowResolutions =
        {
            new(1280, 720),
            new(1366, 768),
            new(1600, 900),
            new(1920, 1080),
        };

        private static readonly ScreenModeOption[] ScreenModes =
        {
            new("전체 화면", FullScreenMode.ExclusiveFullScreen),
            new("전체 창 모드", FullScreenMode.FullScreenWindow),
            new("창 모드", FullScreenMode.Windowed),
        };

        [Header("Screen Mode UI")]
        [SerializeField] private TMP_Text screenModeLabel;
        [SerializeField] private Button previousScreenModeButton;
        [SerializeField] private Button nextScreenModeButton;

        [Header("Resolution UI")]
        [SerializeField] private TMP_Text resolutionLabel;
        [SerializeField] private Button previousResolutionButton;
        [SerializeField] private Button nextResolutionButton;

        [Header("Sound UI")]
        [SerializeField] private TMP_Text masterVolumeLabel;
        [SerializeField] private Button previousMasterVolumeButton;
        [SerializeField] private Button nextMasterVolumeButton;
        [SerializeField] private TMP_Text musicVolumeLabel;
        [SerializeField] private Button previousMusicVolumeButton;
        [SerializeField] private Button nextMusicVolumeButton;
        [SerializeField] private TMP_Text effectVolumeLabel;
        [SerializeField] private Button previousEffectVolumeButton;
        [SerializeField] private Button nextEffectVolumeButton;

        [Header("Behavior")]
        [SerializeField] private bool loadSavedSettingsOnAwake = true;
        [SerializeField, Range(1, 100)] private int volumeStep = 10;

        private int screenModeIndex;
        private int resolutionIndex;
        private float masterVolume;
        private float musicVolume;
        private float effectVolume;

        // PlayerPrefs는 기본적으로 같은 CompanyName/ProductName 빌드가 모두 공유한다.
        // 실행 파일별 독립 설정을 위해 빌드의 Data 경로를 해시한 접두어를 사용한다.
        // 같은 폴더에 빌드를 덮어쓰는 경우에는 경로가 같으므로 기존 설정도 유지된다.
        private static string PreferencePrefix =>
            $"Settings.{Hash128.Compute(Application.dataPath)}.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySavedSettingsOnStartup()
        {
            // Setting UI는 기본 비활성화라 Awake가 창을 열 때까지 실행되지 않는다.
            // UI 수명과 분리해 첫 씬이 열리기 전에 로컬 저장값을 먼저 반영한다.
            int savedScreenModeIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(
                    PreferenceKey(ScreenModePreferenceKey),
                    FindScreenModeIndex(Screen.fullScreenMode)),
                0,
                ScreenModes.Length - 1);
            int savedResolutionIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(
                    PreferenceKey(ResolutionPreferenceKey),
                    FindResolutionIndex(Screen.width, Screen.height)),
                0,
                WindowResolutions.Length - 1);

            FullScreenMode mode = ScreenModes[savedScreenModeIndex].Mode;
            if (mode == FullScreenMode.Windowed)
            {
                Vector2Int resolution = WindowResolutions[savedResolutionIndex];
                Screen.SetResolution(resolution.x, resolution.y, mode);
            }
            else
            {
                Resolution monitorResolution = Screen.currentResolution;
                Screen.SetResolution(monitorResolution.width, monitorResolution.height, mode);
            }

            GameSound.SetMasterVolume(PlayerPrefs.GetFloat(
                PreferenceKey(MasterVolumePreferenceKey), GameSound.MasterVolume));
            GameSound.SetMusicVolume(PlayerPrefs.GetFloat(
                PreferenceKey(MusicVolumePreferenceKey), GameSound.MusicVolume));
            GameSound.SetEffectVolume(PlayerPrefs.GetFloat(
                PreferenceKey(EffectVolumePreferenceKey), GameSound.EffectVolume));
        }

        private void Awake()
        {
            EnsureDimmerBlocksInput();
            if (loadSavedSettingsOnAwake)
                LoadSavedSettings();
            else
                ReadCurrentSettings();

            RefreshLabels();
        }

        private void OnEnable()
        {
            previousScreenModeButton?.onClick.AddListener(SelectPreviousScreenMode);
            nextScreenModeButton?.onClick.AddListener(SelectNextScreenMode);
            previousResolutionButton?.onClick.AddListener(SelectPreviousResolution);
            nextResolutionButton?.onClick.AddListener(SelectNextResolution);
            previousMasterVolumeButton?.onClick.AddListener(DecreaseMasterVolume);
            nextMasterVolumeButton?.onClick.AddListener(IncreaseMasterVolume);
            previousMusicVolumeButton?.onClick.AddListener(DecreaseMusicVolume);
            nextMusicVolumeButton?.onClick.AddListener(IncreaseMusicVolume);
            previousEffectVolumeButton?.onClick.AddListener(DecreaseEffectVolume);
            nextEffectVolumeButton?.onClick.AddListener(IncreaseEffectVolume);
        }

        private void OnDisable()
        {
            previousScreenModeButton?.onClick.RemoveListener(SelectPreviousScreenMode);
            nextScreenModeButton?.onClick.RemoveListener(SelectNextScreenMode);
            previousResolutionButton?.onClick.RemoveListener(SelectPreviousResolution);
            nextResolutionButton?.onClick.RemoveListener(SelectNextResolution);
            previousMasterVolumeButton?.onClick.RemoveListener(DecreaseMasterVolume);
            nextMasterVolumeButton?.onClick.RemoveListener(IncreaseMasterVolume);
            previousMusicVolumeButton?.onClick.RemoveListener(DecreaseMusicVolume);
            nextMusicVolumeButton?.onClick.RemoveListener(IncreaseMusicVolume);
            previousEffectVolumeButton?.onClick.RemoveListener(DecreaseEffectVolume);
            nextEffectVolumeButton?.onClick.RemoveListener(IncreaseEffectVolume);
        }

        public void SelectPreviousScreenMode() => SetScreenMode(screenModeIndex - 1);
        public void SelectNextScreenMode() => SetScreenMode(screenModeIndex + 1);
        public void SelectPreviousResolution() => SetResolution(resolutionIndex - 1);
        public void SelectNextResolution() => SetResolution(resolutionIndex + 1);
        public void DecreaseMasterVolume() => SetMasterVolume(masterVolume - VolumeStep);
        public void IncreaseMasterVolume() => SetMasterVolume(masterVolume + VolumeStep);
        public void DecreaseMusicVolume() => SetMusicVolume(musicVolume - VolumeStep);
        public void IncreaseMusicVolume() => SetMusicVolume(musicVolume + VolumeStep);
        public void DecreaseEffectVolume() => SetEffectVolume(effectVolume - VolumeStep);
        public void IncreaseEffectVolume() => SetEffectVolume(effectVolume + VolumeStep);

        private float VolumeStep => volumeStep / 100f;

        private void LoadSavedSettings()
        {
            screenModeIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(PreferenceKey(ScreenModePreferenceKey), FindScreenModeIndex(Screen.fullScreenMode)),
                0,
                ScreenModes.Length - 1);
            resolutionIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(PreferenceKey(ResolutionPreferenceKey), FindResolutionIndex(Screen.width, Screen.height)),
                0,
                WindowResolutions.Length - 1);
            masterVolume = PlayerPrefs.GetFloat(PreferenceKey(MasterVolumePreferenceKey), GameSound.MasterVolume);
            musicVolume = PlayerPrefs.GetFloat(PreferenceKey(MusicVolumePreferenceKey), GameSound.MusicVolume);
            effectVolume = PlayerPrefs.GetFloat(PreferenceKey(EffectVolumePreferenceKey), GameSound.EffectVolume);

            ApplyCurrentSelection();
            ApplySoundVolumes();
        }

        private void ReadCurrentSettings()
        {
            screenModeIndex = FindScreenModeIndex(Screen.fullScreenMode);
            resolutionIndex = FindResolutionIndex(Screen.width, Screen.height);
            masterVolume = GameSound.MasterVolume;
            musicVolume = GameSound.MusicVolume;
            effectVolume = GameSound.EffectVolume;
        }

        private void SetScreenMode(int index)
        {
            screenModeIndex = Wrap(index, ScreenModes.Length);
            ApplyCurrentSelection();
        }

        private void SetResolution(int index)
        {
            resolutionIndex = Wrap(index, WindowResolutions.Length);
            ApplyCurrentSelection();
        }

        private void ApplyCurrentSelection()
        {
            FullScreenMode mode = ScreenModes[screenModeIndex].Mode;
            if (mode == FullScreenMode.Windowed)
            {
                Vector2Int resolution = WindowResolutions[resolutionIndex];
                Screen.SetResolution(resolution.x, resolution.y, mode);
            }
            else
            {
                Resolution monitorResolution = Screen.currentResolution;
                Screen.SetResolution(monitorResolution.width, monitorResolution.height, mode);
            }

            PlayerPrefs.SetInt(PreferenceKey(ScreenModePreferenceKey), screenModeIndex);
            PlayerPrefs.SetInt(PreferenceKey(ResolutionPreferenceKey), resolutionIndex);
            PlayerPrefs.Save();
            RefreshLabels();
        }

        private void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            ApplySoundVolumes();
        }

        private void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);
            ApplySoundVolumes();
        }

        private void SetEffectVolume(float value)
        {
            effectVolume = Mathf.Clamp01(value);
            ApplySoundVolumes();
        }

        private void ApplySoundVolumes()
        {
            GameSound.SetMasterVolume(masterVolume);
            GameSound.SetMusicVolume(musicVolume);
            GameSound.SetEffectVolume(effectVolume);
            PlayerPrefs.SetFloat(PreferenceKey(MasterVolumePreferenceKey), masterVolume);
            PlayerPrefs.SetFloat(PreferenceKey(MusicVolumePreferenceKey), musicVolume);
            PlayerPrefs.SetFloat(PreferenceKey(EffectVolumePreferenceKey), effectVolume);
            PlayerPrefs.Save();
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            bool isWindowed = ScreenModes[screenModeIndex].Mode == FullScreenMode.Windowed;
            if (previousResolutionButton != null)
                previousResolutionButton.interactable = isWindowed;
            if (nextResolutionButton != null)
                nextResolutionButton.interactable = isWindowed;

            if (screenModeLabel != null)
                screenModeLabel.text = ScreenModes[screenModeIndex].Label;

            if (resolutionLabel != null)
            {
                if (isWindowed)
                {
                    Vector2Int resolution = WindowResolutions[resolutionIndex];
                    resolutionLabel.text = $"{resolution.x}x{resolution.y}";
                }
                else
                {
                    Resolution monitorResolution = Screen.currentResolution;
                    resolutionLabel.text = $"{monitorResolution.width}x{monitorResolution.height}";
                }
            }

            SetVolumeLabel(masterVolumeLabel, masterVolume);
            SetVolumeLabel(musicVolumeLabel, musicVolume);
            SetVolumeLabel(effectVolumeLabel, effectVolume);
            RefreshVolumeButtons(masterVolume, previousMasterVolumeButton, nextMasterVolumeButton);
            RefreshVolumeButtons(musicVolume, previousMusicVolumeButton, nextMusicVolumeButton);
            RefreshVolumeButtons(effectVolume, previousEffectVolumeButton, nextEffectVolumeButton);
        }

        private static void SetVolumeLabel(TMP_Text label, float value)
        {
            if (label != null)
                label.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private static void RefreshVolumeButtons(float value, Button previousButton, Button nextButton)
        {
            if (previousButton != null)
                previousButton.interactable = value > 0f;
            if (nextButton != null)
                nextButton.interactable = value < 1f;
        }

        private void EnsureDimmerBlocksInput()
        {
            Transform dimmer = transform.Find("PopupRoot/Dimmer") ?? transform.Find("Dimmer");
            if (dimmer == null) return;

            foreach (Graphic graphic in dimmer.GetComponents<Graphic>())
                graphic.raycastTarget = true;

            CanvasGroup canvasGroup = dimmer.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }
        }

        private static int FindScreenModeIndex(FullScreenMode mode)
        {
            for (int i = 0; i < ScreenModes.Length; i++)
                if (ScreenModes[i].Mode == mode) return i;

            return 1;
        }

        private static int FindResolutionIndex(int width, int height)
        {
            for (int i = 0; i < WindowResolutions.Length; i++)
                if (WindowResolutions[i].x == width && WindowResolutions[i].y == height) return i;

            return 0;
        }

        private static int Wrap(int value, int count) => (value % count + count) % count;

        private static string PreferenceKey(string settingName) => PreferencePrefix + settingName;

        private readonly struct ScreenModeOption
        {
            public readonly string Label;
            public readonly FullScreenMode Mode;

            public ScreenModeOption(string label, FullScreenMode mode)
            {
                Label = label;
                Mode = mode;
            }
        }
    }
}

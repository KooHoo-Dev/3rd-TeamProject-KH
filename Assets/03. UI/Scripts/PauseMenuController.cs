using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>인게임에서만 사용하는 로컬 일시정지 메뉴의 열기와 버튼 동작을 관리한다.</summary>
    public sealed class PauseMenuController : MonoBehaviour
    {
        [Header("Pause UI")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button exitButton;

        [Header("Blocked UI")]
        [SerializeField] private ShopAccessController shopAccess;
        [SerializeField] private GameTimerBarView gameTimerBar;

        [Header("Setting UI")]
        [SerializeField] private GameObject settingPopup;
        [SerializeField] private Button settingCloseButton;

        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        private static PauseMenuController instance;

        /// <summary>Pause 또는 인게임 설정 창이 열린 동안 월드 입력을 차단한다.</summary>
        public static bool IsGameplayInputBlocked => instance != null && instance.IsAnyModalOpen;

        private bool IsAnyModalOpen =>
            (popupRoot != null && popupRoot.activeInHierarchy) ||
            (settingPopup != null && settingPopup.activeInHierarchy);

        private void Awake()
        {
            instance = this;
            popupRoot ??= transform.Find("PopupRoot")?.gameObject;
            resumeButton ??= FindButton(popupRoot, "ResumeButton");
            settingButton ??= FindButton(popupRoot, "SettingButton");
            mainMenuButton ??= FindButton(popupRoot, "MainMenuButton");
            exitButton ??= FindButton(popupRoot, "ExitButton");
            shopAccess ??= FindFirstObjectByType<ShopAccessController>();
            gameTimerBar ??= FindFirstObjectByType<GameTimerBarView>();
            settingPopup ??= FindSceneObject("Setting UI");
            settingCloseButton ??= FindButton(settingPopup, "CloseButton");

            EnsureDimmerBlocksInput(popupRoot);
            EnsureDimmerBlocksInput(settingPopup);
            popupRoot?.SetActive(false);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void OnEnable()
        {
            resumeButton?.onClick.AddListener(HidePause);
            settingButton?.onClick.AddListener(ShowSettings);
            mainMenuButton?.onClick.AddListener(ReturnToMainMenu);
            exitButton?.onClick.AddListener(QuitGame);
            settingCloseButton?.onClick.AddListener(HideSettingsAndShowPause);
        }

        private void OnDisable()
        {
            resumeButton?.onClick.RemoveListener(HidePause);
            settingButton?.onClick.RemoveListener(ShowSettings);
            mainMenuButton?.onClick.RemoveListener(ReturnToMainMenu);
            exitButton?.onClick.RemoveListener(QuitGame);
            settingCloseButton?.onClick.RemoveListener(HideSettingsAndShowPause);
        }

        private void Update()
        {
            if (Keyboard.current?[Key.Escape].wasPressedThisFrame != true)
                return;

            if (settingPopup != null && settingPopup.activeSelf)
            {
                HideSettingsAndShowPause();
                return;
            }

            if (popupRoot != null && popupRoot.activeSelf)
            {
                HidePause();
                return;
            }

            if (shopAccess?.IsShopOpen == true ||
                shopAccess?.IsClosingWithEscapeThisFrame == true ||
                gameTimerBar?.IsResultPanelOpen == true)
                return;

            popupRoot?.SetActive(true);
        }

        public void HidePause() => popupRoot?.SetActive(false);

        public void CloseAll()
        {
            HidePause();
            settingPopup?.SetActive(false);
        }

        private void ShowSettings()
        {
            HidePause();
            settingPopup?.SetActive(true);
        }

        private void HideSettingsAndShowPause()
        {
            settingPopup?.SetActive(false);
            popupRoot?.SetActive(true);
        }

        private void ReturnToMainMenu()
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static Button FindButton(GameObject root, string buttonName)
        {
            if (root == null) return null;

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.name == buttonName) return button;

            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            foreach (Transform candidate in FindObjectsByType<Transform>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                    return candidate.gameObject;
            }

            return null;
        }

        private static void EnsureDimmerBlocksInput(GameObject root)
        {
            if (root == null) return;

            Transform dimmer = root.transform.Find("Dimmer") ?? root.transform.Find("PopupRoot/Dimmer");
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
    }
}

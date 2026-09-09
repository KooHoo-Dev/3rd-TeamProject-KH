using System;
using System.Text;
using TMPro;
using TerrainCollapse.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>서버가 확정한 게임 시작 시각부터 흐른 시간을 HUD에 표시한다.</summary>
    public sealed class GameTimerBarView : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text goalGoldText;

        [Header("Game Result")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text rankingText;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button roomLobbyButton;
        [SerializeField] private MinimapView minimapView;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";
        [SerializeField] private string roomLobbySceneName = "RoomLobbyScene";

        private bool isEnded;
        private bool announcedFiveMinutesRemaining;
        private bool announcedThreeMinutesRemaining;
        private double previousRemainingSeconds = double.NaN;

        private const int FiveMinutesInSeconds = 5 * 60;
        private const int ThreeMinutesInSeconds = 3 * 60;

        public bool IsResultPanelOpen => resultPanel != null && resultPanel.activeInHierarchy;

        private void Awake()
        {
            if (roomClient == null) roomClient = FindFirstObjectByType<RoomClient>();
            if (goalGoldText == null)
                goalGoldText = transform.Find("GoalGold")?.GetComponent<TMP_Text>();
            if (minimapView == null)
                minimapView = FindFirstObjectByType<MinimapView>();
        }

        private void OnEnable()
        {
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            if (roomLobbyButton != null) roomLobbyButton.onClick.AddListener(ReturnToRoomLobby);
            if (roomClient == null) return;
            roomClient.GameStarted += HandleGameStarted;
            roomClient.GameEnded += HandleGameEnded;
        }

        private void Start()
        {
            if (resultPanel != null) resultPanel.SetActive(false);
            minimapView?.SetGameResultDimmerActive(false);
            RenderGoalGold();
            if (roomClient != null && roomClient.GameStartedAtUnixMilliseconds > 0)
                BeginTimer();
            else
                Render(1f, "게임 시작 대기중");
        }

        private void OnDisable()
        {
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            if (roomLobbyButton != null) roomLobbyButton.onClick.RemoveListener(ReturnToRoomLobby);
            if (roomClient == null) return;
            roomClient.GameStarted -= HandleGameStarted;
            roomClient.GameEnded -= HandleGameEnded;
        }

        private void Update()
        {
            if (roomClient == null || isEnded) return;
            if (roomClient.IsGameplayActive == false)
            {
                double secondsUntilStart = roomClient.SecondsUntilGameplayStart;
                bool isCountingDown = roomClient.HasGameStartSchedule &&
                                      secondsUntilStart <= roomClient.GameCountdownSeconds;
                string waitingText = isCountingDown
                    ? Mathf.Max(1, Mathf.CeilToInt((float)secondsUntilStart)).ToString()
                    : "게임 시작 대기중";
                Render(0f, waitingText);
                return;
            }

            double elapsedSeconds = roomClient.GameElapsedSeconds;
            float duration = Mathf.Max(1, roomClient.GameDurationSeconds);

            AnnounceRemainingTime(duration, elapsedSeconds);
            Render(Mathf.Clamp01((float)(elapsedSeconds / duration)), FormatTime(elapsedSeconds));
        }

        private void HandleGameStarted(GameStartedMessage message)
        {
            RenderGoalGold();
            BeginTimer();
        }

        private void BeginTimer()
        {
            isEnded = false;
            announcedFiveMinutesRemaining = false;
            announcedThreeMinutesRemaining = false;
            previousRemainingSeconds = Mathf.Max(
                0f,
                (roomClient?.GameDurationSeconds ?? 0) - (float)(roomClient?.GameElapsedSeconds ?? 0d));
            Render(0f, FormatTime(roomClient?.GameElapsedSeconds ?? 0d));
        }

        private void AnnounceRemainingTime(float duration, double elapsedSeconds)
        {
            double remainingSeconds = Mathf.Max(0f, duration - (float)elapsedSeconds);

            if (announcedFiveMinutesRemaining == false &&
                duration >= FiveMinutesInSeconds &&
                previousRemainingSeconds >= FiveMinutesInSeconds &&
                remainingSeconds <= FiveMinutesInSeconds)
            {
                announcedFiveMinutesRemaining = true;
                roomClient.ShowLocalSystemMessage("게임 종료까지 5분 남았습니다.");
            }

            if (announcedThreeMinutesRemaining == false &&
                duration >= ThreeMinutesInSeconds &&
                previousRemainingSeconds >= ThreeMinutesInSeconds &&
                remainingSeconds <= ThreeMinutesInSeconds)
            {
                announcedThreeMinutesRemaining = true;
                roomClient.ShowLocalSystemMessage("게임 종료까지 3분 남았습니다.");
            }

            previousRemainingSeconds = remainingSeconds;
        }

        private void HandleGameEnded(GameEndedMessage message)
        {
            isEnded = true;
            FindFirstObjectByType<ShopAccessController>()?.CloseShop();
            FindFirstObjectByType<PauseMenuController>()?.CloseAll();
            minimapView?.SetGameResultDimmerActive(true);
            Render(0f, "게임 종료");
            RenderRanking(message);
            if (resultPanel != null)
            {
                resultPanel.SetActive(true);
                resultPanel.transform.SetAsLastSibling();
            }
        }

        private void RenderRanking(GameEndedMessage message)
        {
            if (rankingText == null) return;

            StringBuilder builder = new("");
            GameResultPlayerDto[] players = message?.Players ?? Array.Empty<GameResultPlayerDto>();
            int rank = 0;
            int previousGold = int.MinValue;
            for (int index = 0; index < players.Length; index++)
            {
                GameResultPlayerDto player = players[index];
                if (index == 0 || player.Gold != previousGold) rank = index + 1;
                previousGold = player.Gold;
                bool isLocalPlayer = string.Equals(
                    player.PlayerID,
                    roomClient?.LocalPlayerID,
                    StringComparison.Ordinal);
                builder.Append(rank)
                    .Append(". ")
                    .Append(string.IsNullOrWhiteSpace(player.NickName) ? player.PlayerID : player.NickName)
                    .Append("  ")
                    .Append(player.Gold.ToString("N0"))
                    .Append(" G");
                if (isLocalPlayer) builder.Append("  (YOU)");
                builder.AppendLine();
            }

            rankingText.text = builder.ToString();
        }

        private void ReturnToMainMenu()
        {
            LoadScene(mainMenuSceneName);
        }

        private void ReturnToRoomLobby() => LoadScene(roomLobbySceneName);

        private static void LoadScene(string sceneName)
        {
            Time.timeScale = 1f;
            if (string.IsNullOrWhiteSpace(sceneName) == false)
                SceneManager.LoadScene(sceneName);
        }

        private bool IsLocalWinner(GameEndedMessage message)
        {
            if (message?.WinnerPlayerIDs == null || roomClient == null) return false;
            foreach (string winnerPlayerID in message.WinnerPlayerIDs)
                if (string.Equals(winnerPlayerID, roomClient.LocalPlayerID, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private void Render(float fillAmount, string text)
        {
            if (fillImage != null) fillImage.fillAmount = fillAmount;
            if (valueText != null) valueText.text = text;
        }

        private void RenderGoalGold()
        {
            if (goalGoldText == null) return;
            goalGoldText.text = (roomClient?.VictoryGold ?? 0).ToString("N0");
        }

        private static string FormatTime(double seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }
    }
}

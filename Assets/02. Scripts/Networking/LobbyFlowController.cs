using TMPro;
using TerrainCollapse.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

namespace TerrainCollapse
{
    /// <summary>씬에 직접 배치된 로비 UI의 입력과 WebSocket 메시지만 연결</summary>
    public sealed class LobbyFlowController : MonoBehaviour
    {
        [SerializeField] private bool isRoomScene;
        [SerializeField] private string serverUrl = "ws://34.50.63.204:5002";
        [SerializeField] private string roomSceneName = "RoomLobbyScene";
        [SerializeField] private string gameSceneName = "ClientTestScene";
        [Header("Main Menu UI")]
        [SerializeField] private TMP_InputField nickNameInput;
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private GameObject roomCodePopup;
        [SerializeField] private Button confirmJoinButton;
        [SerializeField] private Button cancelJoinButton;
        [Header("Room Lobby UI")]
        [SerializeField] private TMP_Text roomInfoText;
        [SerializeField] private TMP_Text playerCountText;
        [SerializeField] private LobbyPlayerSlotView[] playerSlots;
        [SerializeField] private Button copyRoomCodeButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        private LobbySession session;
        private LobbySocketClient lobbyClient;
        private bool isLoadingGame;

        private void Start()
        {
            session = LobbySession.Ensure(serverUrl);
            lobbyClient = session.LobbyClient;
            lobbyClient.StateReceived += HandleState;
            lobbyClient.ErrorReceived += HandleError;
            lobbyClient.Started += HandleStarted;
            if (roomInfoText != null) roomInfoText.raycastTarget = false;
            if (statusText != null) statusText.raycastTarget = false;
            createRoomButton?.onClick.AddListener(CreateRoom);
            joinRoomButton?.onClick.AddListener(ShowJoinPopup);
            confirmJoinButton?.onClick.AddListener(JoinRoom);
            cancelJoinButton?.onClick.AddListener(HideJoinPopup);
            returnButton?.onClick.AddListener(ReturnToMainMenu);
            startGameButton?.onClick.AddListener(StartGame);
            copyRoomCodeButton?.onClick.AddListener(CopyRoomCode);

            if (isRoomScene == false) return;
            if (session.HasRoom == false)
            {
                SetStatus("참여 중인 방이 없습니다. 메인 메뉴로 돌아가세요.");
                return;
            }
            if (session.IsHost == false) SetStatus("호스트가 게임을 시작할 때까지 기다리는 중입니다.");
            if (startGameButton != null) startGameButton.gameObject.SetActive(session.IsHost);
            ReturnToLobbyAsync();
        }

        private void OnDestroy()
        {
            if (lobbyClient == null) return;
            lobbyClient.StateReceived -= HandleState;
            lobbyClient.ErrorReceived -= HandleError;
            lobbyClient.Started -= HandleStarted;
        }

        private async void CreateRoom()
        {
            if (TryReadNickName(out string nickName) == false) return;
            SetStatus("방을 생성하는 중입니다...");
            await lobbyClient.ConnectCreateAsync(session.GetWebSocketBaseUrl(), nickName, session.ClientId);
        }

        private async void JoinRoom()
        {
            if (TryReadNickName(out string nickName) == false) return;
            string code = roomCodeInput?.text.Trim();
            if (code?.Length != 6)
            {
                SetStatus("방 코드는 숫자 6자리여야 합니다.");
                return;
            }
            SetStatus("방에 참가하는 중입니다...");
            await lobbyClient.ConnectJoinAsync(session.GetWebSocketBaseUrl(), code, nickName, session.ClientId);
        }

        private void ShowJoinPopup()
        {
            if (TryReadNickName(out _) == false) return;
            if (roomCodePopup == null) return;
            roomCodePopup.SetActive(true);
            roomCodeInput?.Select();
        }

        private void HideJoinPopup()
        {
            if (roomCodePopup != null) roomCodePopup.SetActive(false);
        }

        private void StartGame()
        {
            if (session.IsHost == false || startGameButton?.interactable == false) return;
            SetStatus("게임을 시작하는 중입니다...");
            lobbyClient.RequestStart(session.HostToken);
        }

        private void CopyRoomCode()
        {
            if (session?.HasRoom != true) return;
            GUIUtility.systemCopyBuffer = session.RoomCode;
            lobbyClient.ShowLocalSystemMessage("방 코드가 복사되었습니다.");
        }

        private void HandleState(LobbyStateMessage message)
        {
            if (message?.Room == null) return;
            string nickName = nickNameInput?.text.Trim() ?? session.NickName;
            session.Configure(serverUrl, nickName, message.Room.RoomCode, message.HostToken);
            session.UpdatePlayers(message.Room.PlayerDetails);
            if (isRoomScene == false)
            {
                SceneManager.LoadScene(roomSceneName);
                return;
            }

            roomInfoText.text = $"방 코드 : {message.Room.RoomCode}";
            playerCountText.text = $"({message.Room.Players.Count}/{message.Room.MaxPlayers})";
            SetStatus(session.IsHost
                ? string.Empty
                : "호스트가 게임을 시작할 때까지 기다리는 중입니다.");
            UpdatePlayerSlots(message.Room.PlayerDetails);
            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(session.IsHost);
                startGameButton.interactable = session.IsHost && message.Room.IsStarted == false;
            }
        }

        private void UpdatePlayerSlots(List<LobbyPlayerInfo> players)
        {
            if (playerSlots == null) return;

            for (int i = 0; i < playerSlots.Length; i++)
            {
                if (playerSlots[i] == null) continue;
                if (players != null && i < players.Count)
                    playerSlots[i].SetPlayer(
                        players[i].NickName,
                        players[i].ClientID == session.ClientId,
                        i);
                else
                    playerSlots[i].SetEmpty();
            }
        }

        private async void ReturnToMainMenu()
        {
            if (isLoadingGame) return;

            isLoadingGame = true;

            SetStatus("메인 메뉴로 돌아가는 중입니다...");
            await lobbyClient.DisconnectAsync();

            session.ClearRoom();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private void HandleStarted(string roomCode)
        {
            if (session.HasRoom && session.RoomCode == roomCode) LoadGame();
        }

        private void HandleError(string error)
        {
            if (error == "room.not_enough_players")
            {
                lobbyClient.ShowLocalSystemMessage("게임을 시작하려면 최소 2명의 플레이어가 필요합니다.");
                return;
            }

            lobbyClient.ShowLocalSystemMessage($"요청 처리에 실패했습니다: {error}");
        }

        private bool TryReadNickName(out string nickName)
        {
            nickName = nickNameInput?.text.Trim();
            if (IsValidNickName(nickName)) return true;
            SetStatus("닉네임은 1~16자로 입력해야 합니다.");
            return false;
        }

        private static bool IsValidNickName(string nickName)
            => nickName?.Length is >= 1 and <= 16;

        private async void LoadGame()
        {
            if (isLoadingGame) return;
            isLoadingGame = true;
            await lobbyClient.DisconnectAsync();
            SceneManager.LoadScene(gameSceneName);
        }

        private async void ReturnToLobbyAsync()
        {
            if (lobbyClient.IsConnected)
            {
                if (lobbyClient.LastState != null) HandleState(lobbyClient.LastState);
                return;
            }

            SetStatus("대기실로 돌아가는 중입니다...");
            await lobbyClient.ConnectReturnAsync(
                session.GetWebSocketBaseUrl(),
                session.RoomCode,
                session.NickName,
                session.ClientId);
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}

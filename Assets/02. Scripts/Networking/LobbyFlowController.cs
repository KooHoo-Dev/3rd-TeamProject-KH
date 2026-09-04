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
        [SerializeField] private LobbyPlayerSlotView[] playerSlots;
        [SerializeField] private Button copyRoomCodeButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private TMP_Text statusText;

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
            startGameButton?.onClick.AddListener(StartGame);
            copyRoomCodeButton?.onClick.AddListener(CopyRoomCode);

            if (isRoomScene == false) return;
            if (session.HasRoom == false)
            {
                SetStatus("No room session. Return to the main menu.");
                return;
            }
            if (session.IsHost == false) SetStatus("Waiting for the host to start the game.");
            if (startGameButton != null) startGameButton.gameObject.SetActive(session.IsHost);
            if (lobbyClient.LastState != null)
                HandleState(lobbyClient.LastState);
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
            SetStatus("Creating room...");
            await lobbyClient.ConnectCreateAsync(session.GetWebSocketBaseUrl(), nickName, session.ClientId);
        }

        private async void JoinRoom()
        {
            if (TryReadNickName(out string nickName) == false) return;
            string code = roomCodeInput?.text.Trim();
            if (code?.Length != 6)
            {
                SetStatus("Room code must be 6 digits.");
                return;
            }
            SetStatus("Joining room...");
            await lobbyClient.ConnectJoinAsync(session.GetWebSocketBaseUrl(), code, nickName, session.ClientId);
        }

        private void ShowJoinPopup()
        {
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
            SetStatus("Starting game...");
            lobbyClient.RequestStart(session.HostToken);
        }

        private void CopyRoomCode()
        {
            if (session?.HasRoom != true) return;
            GUIUtility.systemCopyBuffer = session.RoomCode;
            SetStatus("Room code copied.");
        }

        private void HandleState(LobbyStateMessage message)
        {
            if (message?.Room == null) return;
            string nickName = nickNameInput?.text.Trim() ?? session.NickName;
            session.Configure(serverUrl, nickName, message.Room.RoomCode, message.HostToken);
            if (isRoomScene == false)
            {
                SceneManager.LoadScene(roomSceneName);
                return;
            }

            if (roomInfoText != null)
                roomInfoText.text = $"ROOM CODE: {message.Room.RoomCode}  ({message.Room.Players.Count}/{message.Room.MaxPlayers})";
            UpdatePlayerSlots(message.Room.PlayerDetails);
            if (startGameButton != null)
                startGameButton.interactable = session.IsHost && message.Room.Players.Count >= 2 &&
                                               message.Room.Players.Count <= 4 && message.Room.IsStarted == false;
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
                        players[i].ClientID == session.ClientId);
                else
                    playerSlots[i].SetEmpty();
            }
        }

        private void HandleStarted(string roomCode)
        {
            if (session.HasRoom && session.RoomCode == roomCode) LoadGame();
        }

        private void HandleError(string error) => SetStatus($"Request failed: {error}");

        private bool TryReadNickName(out string nickName)
        {
            nickName = nickNameInput?.text.Trim();
            if (nickName?.Length is >= 1 and <= 16) return true;
            SetStatus("Nickname must be 1-16 characters.");
            return false;
        }

        private async void LoadGame()
        {
            if (isLoadingGame) return;
            isLoadingGame = true;
            await lobbyClient.DisconnectAsync();
            SceneManager.LoadScene(gameSceneName);
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}

using TMPro;
using TerrainCollapse.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

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
        [SerializeField] private GameObject kickedNoticePopup;
        [SerializeField] private Button kickedNoticeCloseButton;
        [Header("Room Lobby UI")]
        [SerializeField] private TMP_Text roomInfoText;
        [SerializeField] private TMP_Text playerCountText;
        [SerializeField] private LobbyPlayerSlotView[] playerSlots;
        [SerializeField] private Button copyRoomCodeButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";
        [SerializeField] private GameObject kickConfirmPopup;
        [SerializeField] private TMP_Text kickConfirmMessage;
        [SerializeField] private Button kickConfirmButton;
        [SerializeField] private Button kickCancelButton;

        private LobbySession session;
        private LobbySocketClient lobbyClient;
        private bool isLoadingGame;
        private string pendingKickClientId;

        private void Start()
        {
            session = LobbySession.Ensure(serverUrl);
            lobbyClient = session.LobbyClient;
            lobbyClient.StateReceived += HandleState;
            lobbyClient.ErrorReceived += HandleError;
            lobbyClient.Started += HandleStarted;
            lobbyClient.Kicked += HandleKicked;
            if (roomInfoText != null) roomInfoText.raycastTarget = false;
            if (statusText != null) statusText.raycastTarget = false;
            createRoomButton?.onClick.AddListener(CreateRoom);
            joinRoomButton?.onClick.AddListener(ShowJoinPopup);
            confirmJoinButton?.onClick.AddListener(JoinRoom);
            cancelJoinButton?.onClick.AddListener(HideJoinPopup);
            returnButton?.onClick.AddListener(ReturnToMainMenu);
            startGameButton?.onClick.AddListener(StartGame);
            copyRoomCodeButton?.onClick.AddListener(CopyRoomCode);

            if (isRoomScene == false)
            {
                if (kickedNoticePopup != null) kickedNoticePopup.SetActive(false);
                kickedNoticeCloseButton?.onClick.AddListener(HideKickedNotice);
                ShowKickedNoticeIfNeeded();
                return;
            }

            MoveKickPopupToForegroundCanvas();
            if (kickConfirmPopup != null) kickConfirmPopup.SetActive(false);
            kickCancelButton?.onClick.AddListener(HideKickConfirmation);
            kickConfirmButton?.onClick.AddListener(ConfirmKick);
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
            lobbyClient.Kicked -= HandleKicked;
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
            session.Configure(
                serverUrl,
                nickName,
                message.Room.RoomCode,
                message.Room.HostClientID,
                message.HostToken);
            session.UpdatePlayers(message.Room.PlayerDetails);
            if (isRoomScene == false)
            {
                SceneManager.LoadScene(roomSceneName);
                return;
            }

            roomInfoText.text = $"방 코드 : {message.Room.RoomCode}";
            playerCountText.text = $"({message.Room.PlayerDetails.Count(player => player.IsConnected)}/{message.Room.MaxPlayers})";
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
                {
                    LobbyPlayerInfo player = players[i];
                    playerSlots[i].SetPlayer(
                        player.NickName,
                        player.ClientID == session.ClientId,
                        i,
                        player.IsConnected,
                        session.IsHost && i > 0,
                        () => ShowKickConfirmation(player.ClientID, player.NickName));
                }
                else playerSlots[i].SetEmpty();
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
            if (error == "room.players_not_returned")
            {
                lobbyClient.ShowLocalSystemMessage("모든 기존 플레이어가 대기실로 돌아와야 게임을 시작할 수 있습니다.");
                return;
            }
            lobbyClient.ShowLocalSystemMessage($"요청 처리에 실패했습니다: {error}");
        }

        private void ShowKickConfirmation(string targetClientId, string nickName)
        {
            if (session.IsHost == false || string.IsNullOrWhiteSpace(targetClientId)) return;
            if (kickConfirmPopup == null || kickConfirmMessage == null) return;

            pendingKickClientId = targetClientId;
            kickConfirmMessage.text = $"{nickName}님을 추방하시겠습니까?";
            kickConfirmPopup.SetActive(true);
        }

        private void ConfirmKick()
        {
            if (session.IsHost && string.IsNullOrWhiteSpace(pendingKickClientId) == false)
                lobbyClient.RequestKick(session.HostToken, pendingKickClientId);
            HideKickConfirmation();
        }

        private void HideKickConfirmation()
        {
            pendingKickClientId = null;
            if (kickConfirmPopup != null) kickConfirmPopup.SetActive(false);
        }

        private async void HandleKicked(LobbyKickedMessage _)
        {
            if (isLoadingGame) return;
            isLoadingGame = true;
            await lobbyClient.DisconnectAsync();
            session.NotifyKicked();
            session.ClearRoom();
            SceneManager.LoadScene(mainMenuSceneName);
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

        private void MoveKickPopupToForegroundCanvas()
        {
            if (kickConfirmPopup == null) return;

            // 아바타 프리뷰는 SpriteRenderer이고, 로비 UI는 Screen Space - Camera다.
            // 따라서 팝업만 Screen Space - Overlay 루트로 옮겨 항상 모든 프리뷰보다 앞에 그린다.
            Canvas sourceCanvas = kickConfirmPopup.GetComponentInParent<Canvas>();
            GameObject root = new("Lobby Modal Overlay", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;

            CanvasScaler sourceScaler = sourceCanvas != null
                ? sourceCanvas.GetComponent<CanvasScaler>()
                : null;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (sourceScaler != null)
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            }

            kickConfirmPopup.transform.SetParent(root.transform, false);
        }

        private void ShowKickedNoticeIfNeeded()
        {
            if (session.ConsumeKickedNotice() == false) return;
            if (kickedNoticePopup == null)
            {
                Debug.LogWarning("[LobbyFlowController] Kicked Notice Popup is not assigned.", this);
                return;
            }
            kickedNoticePopup.SetActive(true);
        }

        private void HideKickedNotice()
        {
            if (kickedNoticePopup != null) kickedNoticePopup.SetActive(false);
        }
    }
}

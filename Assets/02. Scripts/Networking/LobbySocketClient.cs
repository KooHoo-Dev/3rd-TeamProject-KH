using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using TerrainCollapse.Networking;

namespace TerrainCollapse
{
    /// <summary>메인/대기방에서 사용하는 별도 WebSocket 연결</summary>
    public sealed class LobbySocketClient : MonoBehaviour
    {
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private bool expectedClose;
        private long lastStateRevision;

        public event Action<LobbyStateMessage> StateReceived;
        public event Action<string> ErrorReceived;
        public event Action<string> Started;
        public event Action<LobbyChatMessage> ChatMessageReceived;
        public event Action<LobbySystemMessage> SystemMessageReceived;
        public event Action<LobbyKickedMessage> Kicked;

        public bool IsConnected => socket?.State == WebSocketState.Open;
        public LobbyStateMessage LastState { get; private set; } // 씬 전환 중 놓칠 수 있는 가장 최근의 대기방 상태

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out Action action)) action.Invoke();
        }

        private async void OnDestroy() => await DisconnectAsync();

        public Task ConnectCreateAsync(string serverUrl, string nickName, string clientId) =>
            ConnectAsync(serverUrl, new LobbyCreateMessage
            {
                NickName = nickName,
                ClientID = clientId,
            });

        public Task ConnectJoinAsync(string serverUrl, string roomCode, string nickName, string clientId) =>
            ConnectAsync(serverUrl, new LobbyJoinMessage
            {
                RoomCode = roomCode,
                NickName = nickName,
                ClientID = clientId,
            });

        public Task ConnectReturnAsync(string serverUrl, string roomCode, string nickName, string clientId) =>
            ConnectAsync(serverUrl, new LobbyReturnMessage
            {
                RoomCode = roomCode,
                NickName = nickName,
                ClientID = clientId,
            });

        public bool RequestStart(string hostToken) => Send(new LobbyStartRequest { HostToken = hostToken });
        public bool RequestKick(string hostToken, string targetClientId) =>
            Send(new LobbyKickRequest { HostToken = hostToken, TargetClientID = targetClientId });
        public bool SendChat(string text)
        {
            string normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (normalized.Length > 200) normalized = normalized.Substring(0, 200);
            return Send(new LobbyChatSendMessage { Text = normalized });
        }

        /// <summary>현재 클라이언트에만 표시할 로비 시스템 안내를 채팅 UI에 전달한다.</summary>
        public void ShowLocalSystemMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            mainThreadActions.Enqueue(() => SystemMessageReceived?.Invoke(
                new LobbySystemMessage { Text = text }));
        }

        public async Task DisconnectAsync()
        {
            expectedClose = true;
            CancellationTokenSource source = cancellation;
            ClientWebSocket current = socket;
            cancellation = null;
            socket = null;
            source?.Cancel();
            if (current != null)
            {
                try
                {
                    if (current.State == WebSocketState.Open)
                        await current.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scene changed", CancellationToken.None);
                }
                catch (WebSocketException) { }
                current.Dispose();
            }
            source?.Dispose();
        }

        private async Task ConnectAsync(string serverUrl, object firstMessage)
        {
            await DisconnectAsync();
            expectedClose = false;
            lastStateRevision = 0;
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();
            ClientWebSocket activeSocket = socket;
            try
            {
                await activeSocket.ConnectAsync(new Uri($"{serverUrl.TrimEnd('/')}/lobby"), cancellation.Token);
                await SendAsync(activeSocket, firstMessage, cancellation.Token);
                _ = ReceiveLoopAsync(activeSocket, cancellation.Token);
            }
            catch (Exception exception)
            {
                mainThreadActions.Enqueue(() => ErrorReceived?.Invoke(exception.Message));
                await DisconnectAsync();
            }
        }

        private bool Send(object message)
        {
            if (socket?.State != WebSocketState.Open || cancellation == null) return false;
            _ = SendAsync(socket, message, cancellation.Token);
            return true;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            StringBuilder text = new();
            try
            {
                while (token.IsCancellationRequested == false)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await activeSocket.ReceiveAsync(buffer, token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (result.EndOfMessage == false);

                    Route(text.ToString());
                    text.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (expectedClose == false && socket == activeSocket)
                    mainThreadActions.Enqueue(() => ErrorReceived?.Invoke(exception.Message));
            }
        }

        private void Route(string json)
        {
            LobbyMessageHeader header = JsonConvert.DeserializeObject<LobbyMessageHeader>(json);
            switch (header?.Type)
            {
                case "lobby.state":
                    LobbyStateMessage state = JsonConvert.DeserializeObject<LobbyStateMessage>(json);
                    mainThreadActions.Enqueue(() =>
                    {
                        if (state.Revision < lastStateRevision) return;
                        lastStateRevision = state.Revision;
                        LastState = state;
                        StateReceived?.Invoke(state);
                    });
                    break;
                case "lobby.started":
                    LobbyStartedMessage started = JsonConvert.DeserializeObject<LobbyStartedMessage>(json);
                    mainThreadActions.Enqueue(() => Started?.Invoke(started.RoomCode));
                    break;
                case "lobby.error":
                    LobbyErrorMessage error = JsonConvert.DeserializeObject<LobbyErrorMessage>(json);
                    mainThreadActions.Enqueue(() => ErrorReceived?.Invoke(error.Code));
                    break;
                case "lobby.chat":
                    LobbyChatMessage chat = JsonConvert.DeserializeObject<LobbyChatMessage>(json);
                    mainThreadActions.Enqueue(() => ChatMessageReceived?.Invoke(chat));
                    break;
                case "lobby.system":
                    LobbySystemMessage system = JsonConvert.DeserializeObject<LobbySystemMessage>(json);
                    mainThreadActions.Enqueue(() => SystemMessageReceived?.Invoke(system));
                    break;
                case "lobby.kicked":
                    LobbyKickedMessage kicked = JsonConvert.DeserializeObject<LobbyKickedMessage>(json);
                    // 서버가 강퇴 알림 뒤 소켓을 닫는 것은 정상 흐름이다.
                    expectedClose = true;
                    mainThreadActions.Enqueue(() => Kicked?.Invoke(kicked));
                    break;
            }
        }

        private static async Task SendAsync(ClientWebSocket target, object message, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            await target.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }
    }
}

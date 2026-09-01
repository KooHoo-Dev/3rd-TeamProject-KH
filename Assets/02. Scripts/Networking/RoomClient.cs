using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>방 WebSocket 연결과 서버 권위 지형 메시지 수신을 담당한다.</summary>
    public sealed class RoomClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string serverUrl = "ws://localhost:5000";
        [SerializeField] private string roomCode = "ABCE";
        [SerializeField] private string nickName = "Player";
        [SerializeField] private bool connectOnStart = true;
        [Header("Scene References")]
        [SerializeField] private TerrainGridWorld terrainWorld;
        [SerializeField] private TerrainManager terrainManager;

        private readonly ConcurrentQueue<string> outgoingJson = new();
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private readonly SemaphoreSlim sendSignal = new(0);
        private readonly HashSet<long> pendingExcavations = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private Task sendTask;
        private long nextRequestId;
        private bool hasTerrainSnapshot;

        public static RoomClient Instance { get; private set; }
        public bool IsConnected => socket?.State == WebSocketState.Open;
        public bool IsAuthoritativeTerrainMode => IsConnected && hasTerrainSnapshot;
        public MapSessionDescriptor MapSession { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[RoomClient] 씬에는 RoomClient를 하나만 두어야 합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private async void Start()
        {
            if (connectOnStart) await ConnectAsync();
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out Action action))
                action.Invoke();
        }

        private async void OnDestroy()
        {
            if (Instance == this) Instance = null;
            await DisconnectAsync();
            sendSignal.Dispose();
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;
            await DisconnectAsync();
            hasTerrainSnapshot = false;
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();

            try
            {
                Uri uri = BuildRoomUri();
                await socket.ConnectAsync(uri, cancellation.Token);
                sendTask = SendLoopAsync(socket, cancellation.Token);
                receiveTask = ReceiveLoopAsync(socket, cancellation.Token);
                Send(new { Type = "hello", NickName = nickName });
                Debug.Log($"[RoomClient] Connected: {uri}", this);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RoomClient] Connection failed: {exception.Message}", this);
                await DisconnectAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            CancellationTokenSource source = cancellation;
            ClientWebSocket currentSocket = socket;
            cancellation = null;
            socket = null;
            hasTerrainSnapshot = false;
            source?.Cancel();

            if (currentSocket != null)
            {
                try
                {
                    if (currentSocket.State == WebSocketState.Open)
                        await currentSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                }
                catch (WebSocketException) { }
                currentSocket.Dispose();
            }
            source?.Dispose();
            pendingExcavations.Clear();
        }

        public bool RequestExcavation(Vector2Int targetCell, int damageAmount)
        {
            if (IsAuthoritativeTerrainMode == false)
            {
                Debug.LogWarning("[RoomClient] 지형 Snapshot을 받기 전이거나 서버에 연결되어 있지 않아 채굴 요청을 보내지 않았습니다.", this);
                return false;
            }

            long requestId = Interlocked.Increment(ref nextRequestId);
            pendingExcavations.Add(requestId);
            Send(new TerrainExcavationRequest
            {
                ClientRequestID = requestId,
                ExpectedTerrainRevision = terrainManager.Revision,
                TargetCell = new GridCoord(targetCell.x, targetCell.y),
                DamageAmount = Mathf.Max(1, damageAmount),
            });
            return true;
        }

        private Uri BuildRoomUri()
        {
            string baseUrl = serverUrl.TrimEnd('/');
            return new Uri($"{baseUrl}/room?code={Uri.EscapeDataString(roomCode)}");
        }

        private void Send(object message)
        {
            outgoingJson.Enqueue(JsonConvert.SerializeObject(message));
            sendSignal.Release();
        }

        private async Task SendLoopAsync(ClientWebSocket activeSocket, CancellationToken token)
        {
            try
            {
                while (token.IsCancellationRequested == false)
                {
                    await sendSignal.WaitAsync(token);
                    while (outgoingJson.TryDequeue(out string json))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        await activeSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { QueueConnectionError("send", exception); }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            StringBuilder message = new();
            try
            {
                while (token.IsCancellationRequested == false)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (result.EndOfMessage == false);

                    RouteIncoming(message.ToString());
                    message.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { QueueConnectionError("receive", exception); }
        }

        private void RouteIncoming(string json)
        {
            try
            {
                NetworkMessageHeader header = JsonConvert.DeserializeObject<NetworkMessageHeader>(json);
                switch (header?.Type)
                {
                    case NetworkMessageTypes.MapSession:
                        EnqueueMainThread(() => HandleMapSession(JsonConvert.DeserializeObject<MapSessionMessage>(json)));
                        break;
                    case NetworkMessageTypes.TerrainSnapshot:
                        EnqueueMainThread(() => HandleTerrainSnapshot(JsonConvert.DeserializeObject<TerrainSnapshotMessage>(json)));
                        break;
                    case NetworkMessageTypes.TerrainChangeBatch:
                        EnqueueMainThread(() => HandleTerrainBatch(JsonConvert.DeserializeObject<TerrainChangeBatchMessage>(json)));
                        break;
                    case "error":
                        EnqueueMainThread(() => HandleError(JsonConvert.DeserializeObject<ErrorMessage>(json)));
                        break;
                    default:
                        EnqueueMainThread(() => Debug.Log($"[RoomClient] Ignored message type: {header?.Type}", this));
                        break;
                }
            }
            catch (Exception exception) { QueueConnectionError("message parse", exception); }
        }

        private void EnqueueMainThread(Action action) => mainThreadActions.Enqueue(action);

        private void HandleMapSession(MapSessionMessage message)
        {
            MapSession = message?.Session;
            Debug.Log($"[RoomClient] Map session received: {MapSession?.MapSessionID}", this);
        }

        private void HandleTerrainSnapshot(TerrainSnapshotMessage message)
        {
            if (terrainWorld == null)
            {
                Debug.LogError("[RoomClient] TerrainGridWorld reference is not assigned.", this);
                return;
            }
            TerrainSnapshot snapshot = TerrainNetworkAdapter.ToRuntime(message?.Snapshot);
            if (snapshot == null || snapshot.CellSize <= 0f)
            {
                Debug.LogError("[RoomClient] Invalid terrain snapshot.", this);
                return;
            }
            terrainWorld.LoadSnapshot(snapshot);
            hasTerrainSnapshot = true;
            pendingExcavations.Clear();
            Debug.Log($"[RoomClient] Terrain snapshot loaded: revision={snapshot.Revision}, cells={snapshot.Cells.Count}", this);
        }

        private void HandleTerrainBatch(TerrainChangeBatchMessage message)
        {
            if (terrainManager == null)
            {
                Debug.LogError("[RoomClient] TerrainManager reference is not assigned.", this);
                return;
            }
            TerrainChangeBatch batch = TerrainNetworkAdapter.ToRuntime(message?.Batch);
            if (batch == null)
            {
                Debug.LogError("[RoomClient] Invalid terrain batch.", this);
                return;
            }
            pendingExcavations.Remove(batch.CollapseId);
            if (terrainManager.ApplyAuthoritativeBatch(batch, out string error) == false)
                Debug.LogWarning($"[RoomClient] Terrain batch rejected; awaiting snapshot resync. {error}", this);
        }

        private void HandleError(ErrorMessage message)
        {
            Debug.LogError($"[RoomClient] Server error {message?.Code}: {message?.Message}", this);
            if (message?.Code == "terrain.revision_mismatch")
                Debug.LogWarning("[RoomClient] Revision mismatch; the server will send a terrain snapshot for resync.", this);
        }

        private void QueueConnectionError(string operation, Exception exception)
            => EnqueueMainThread(() => Debug.LogError($"[RoomClient] {operation} failed: {exception.Message}", this));
    }
}

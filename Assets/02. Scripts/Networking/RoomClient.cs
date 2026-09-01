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

        [SerializeField] private TestPlayerController localPlayer;
        [SerializeField, Min(1f)] private float positionSendRate = 10f;

        private Vector2 lastSentPosition;
        private float nextPositionSendTime;
        private bool hasSentPosition;

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
        private bool handshakeComplete;

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

            if (IsConnected && handshakeComplete &&
                localPlayer != null &&
                Time.unscaledTime >= nextPositionSendTime)
            {
                Vector2 position = localPlayer.Position;

                if (hasSentPosition == false ||
                    (position - lastSentPosition).sqrMagnitude > 0.0001f)
                {
                    if (Send(new
                    {
                        Type = "player.move",
                        X = position.x,
                        Y = position.y
                    }) == false)
                        return;

                    lastSentPosition = position;
                    hasSentPosition = true;
                }

                nextPositionSendTime = Time.unscaledTime + 1f / positionSendRate;
            }
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
            // 이전 연결에서 전송하지 못한 패킷은 새 연결의 hello보다 먼저 보내면 안 된다.
            outgoingJson.Clear();
            hasTerrainSnapshot = false;
            hasSentPosition = false;
            handshakeComplete = false;
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
            hasSentPosition = false;
            handshakeComplete = false;
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
            outgoingJson.Clear();
            pendingExcavations.Clear();
        }

        public bool RequestExcavation(Vector2Int targetCell, int damageAmount)
        {
            if (IsAuthoritativeTerrainMode == false)
            {
                Debug.LogWarning("[RoomClient] 지형 Snapshot을 받기 전이거나 서버에 연결되어 있지 않아 채굴 요청을 보내지 않았습니다.", this);
                return false;
            }
            if (pendingExcavations.Count > 0)
            {
                Debug.Log("[RoomClient] 이전 채굴 요청의 서버 확정을 기다리는 중입니다.", this);
                return false;
            }

            long requestId = Interlocked.Increment(ref nextRequestId);
            pendingExcavations.Add(requestId);
            if (Send(new TerrainExcavationRequest
            {
                ClientRequestID = requestId,
                ExpectedTerrainRevision = terrainManager.Revision,
                TargetCell = new GridCoord(targetCell.x, targetCell.y),
                DamageAmount = Mathf.Max(1, damageAmount),
            }) == false)
            {
                pendingExcavations.Remove(requestId);
                Debug.LogWarning("[RoomClient] 연결이 종료되어 채굴 요청을 보내지 못했습니다.", this);
                return false;
            }
            return true;
        }

        private Uri BuildRoomUri()
        {
            string baseUrl = serverUrl.TrimEnd('/');
            return new Uri($"{baseUrl}/room?code={Uri.EscapeDataString(roomCode)}");
        }

        private bool Send(object message)
        {
            if (socket?.State != WebSocketState.Open || cancellation?.IsCancellationRequested != false)
                return false;

            outgoingJson.Enqueue(JsonConvert.SerializeObject(message));
            sendSignal.Release();
            return true;
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
                        if (activeSocket.State != WebSocketState.Open) return;
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        await activeSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException exception) { QueueTransportClosed(activeSocket, "send", exception); }
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
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            QueueTransportClosed(activeSocket, "receive", null);
                            return;
                        }
                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (result.EndOfMessage == false);

                    RouteIncoming(message.ToString());
                    message.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException exception) { QueueTransportClosed(activeSocket, "receive", exception); }
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
                    case "welcome":
                        EnqueueMainThread(HandleWelcome);
                        break;
                    // 서버가 주기적으로 보내는 기존 위치 동기화 패킷이다.
                    // 원격 플레이어 동기화는 아직 범위 밖이므로 로그/메인 스레드 작업 없이 무시한다.
                    case "join":
                    case "leave":
                    case "state":
                    case "world_item.snapshot":
                    case "world_item.spawned":
                    case "world_item.removed":
                    case "inventory.snapshot":
                        break;
                    default:
                        EnqueueMainThread(() => Debug.LogWarning($"[RoomClient] Unsupported message type: {header?.Type}", this));
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

        private void HandleWelcome()
        {
            handshakeComplete = true;
            // 서버가 hello를 수락한 뒤에만 위치/게임플레이 패킷을 보낸다.
            nextPositionSendTime = Time.unscaledTime;
            Debug.Log("[RoomClient] Hello accepted.", this);
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
            RespawnLocalPlayerOnSnapshot(snapshot);
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
            if (message?.Code == "terrain.revision_mismatch")
            {
                // 이 Snapshot이 도착할 때까지 이전 Revision으로 새 요청을 보내지 않는다.
                hasTerrainSnapshot = false;
                pendingExcavations.Clear();
                Debug.LogWarning("[RoomClient] Revision mismatch; the server will send a terrain snapshot for resync.", this);
                return;
            }

            Debug.LogError($"[RoomClient] Server error {message?.Code}: {message?.Message}", this);
        }

        private void QueueConnectionError(string operation, Exception exception)
            => EnqueueMainThread(() => Debug.LogError($"[RoomClient] {operation} failed: {exception.Message}", this));

        private void QueueTransportClosed(ClientWebSocket activeSocket, string operation, WebSocketException exception)
        {
            EnqueueMainThread(async () =>
            {
                // 이미 새 연결로 교체되었거나 종료 처리된 소켓이면 중복 로그/정리를 하지 않는다.
                if (socket != activeSocket) return;

                string detail = exception == null ? "close frame received" : exception.Message;
                Debug.LogWarning($"[RoomClient] Connection closed during {operation}: {detail}", this);
                await DisconnectAsync();
            });
        }

        private void RespawnLocalPlayerOnSnapshot(TerrainSnapshot snapshot)
        {
            if (localPlayer == null) return;

            HashSet<Vector2Int> occupiedCells = new();
            foreach (TerrainCellChange cell in snapshot.Cells)
            {
                if (cell.Type != TerrainTileType.Empty)
                    occupiedCells.Add(cell.Coord);
            }

            Vector2Int? bestCell = null;
            float bestDistance = float.MaxValue;
            Vector2 currentPosition = localPlayer.Position;
            foreach (Vector2Int groundCell in occupiedCells)
            {
                Vector2Int spawnCell = groundCell + Vector2Int.up;
                if (occupiedCells.Contains(spawnCell) || occupiedCells.Contains(spawnCell + Vector2Int.up))
                    continue;

                Vector2 candidate = terrainWorld.CellToWorldCenter(spawnCell);
                float distance = (candidate - currentPosition).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestCell = spawnCell;
                bestDistance = distance;
            }

            if (bestCell == null)
            {
                Debug.LogWarning("[RoomClient] Snapshot에서 안전한 플레이어 스폰 셀을 찾지 못했습니다.", this);
                return;
            }

            localPlayer.Respawn(terrainWorld.CellToWorldCenter(bestCell.Value));
        }
    }
}

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
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private ItemDropSpawner itemDropSpawner;
        [SerializeField] private PlayerInventory playerInventory;

        [SerializeField] private TestPlayerController localPlayer;
        [SerializeField, Min(1f)] private float positionSendRate = 10f;

        private Vector2 lastSentPosition;
        private float nextPositionSendTime;
        private bool hasSentPosition;

        private readonly ConcurrentQueue<string> outgoingJson = new();
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private readonly SemaphoreSlim sendSignal = new(0);
        private readonly HashSet<string> pendingWorldItemPickups = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private Task sendTask;
        private long nextRequestId;
        private long pendingExcavationId;
        private bool hasTerrainSnapshot;
        private bool handshakeComplete;
        private long pendingCollapsePlacementId;

        public static RoomClient Instance { get; private set; }
        public bool IsConnected => socket?.State == WebSocketState.Open;
        public bool IsAuthoritativeTerrainMode => IsConnected && hasTerrainSnapshot;
        public MapSessionDescriptor MapSession { get; private set; }
        public TerrainSnapshot CurrentTerrainSnapshot { get; private set; }
        public event Action<TerrainSnapshot> TerrainSnapshotLoaded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[RoomClient] 씬에는 RoomClient를 하나만 두어야 합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (collapseManager == null)
                collapseManager = FindFirstObjectByType<CollapseManager>();
            if (itemDropSpawner == null)
                itemDropSpawner = FindFirstObjectByType<ItemDropSpawner>();
            if (playerInventory == null && localPlayer != null)
                playerInventory = localPlayer.GetComponent<PlayerInventory>();
        }

        private void OnEnable()
        {
            if (collapseManager != null)
                collapseManager.PlacementProposed += HandlePlacementProposed;
        }

        private void OnDisable()
        {
            if (collapseManager != null)
                collapseManager.PlacementProposed -= HandlePlacementProposed;
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
            CurrentTerrainSnapshot = null;
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
            CurrentTerrainSnapshot = null;
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
            pendingExcavationId = 0;
            pendingWorldItemPickups.Clear();
            pendingCollapsePlacementId = 0;
        }

        public bool RequestExcavation(Vector2Int targetCell, int itemId, int damageAmount)
        {
            if (IsAuthoritativeTerrainMode == false)
            {
                Debug.LogWarning("[RoomClient] 지형 Snapshot을 받기 전이거나 서버에 연결되어 있지 않아 채굴 요청을 보내지 않았습니다.", this);
                return false;
            }
            if (pendingExcavationId > 0)
            {
                Debug.Log("[RoomClient] 이전 채굴 요청의 서버 확정을 기다리는 중입니다.", this);
                return false;
            }
            // 낙하 중인 Chunk는 로컬 물리 표현일 뿐이며 서버 지형 Revision을 점유하지 않는다.
            // 실제 배치 요청을 서버가 처리 중인 짧은 구간에만 다음 채굴 요청을 직렬화한다.
            if (pendingCollapsePlacementId > 0)
            {
                Debug.Log("[RoomClient] 낙하 지형의 서버 병합 확정을 기다리는 중입니다.", this);
                return false;
            }
            long requestId = Interlocked.Increment(ref nextRequestId);
            pendingExcavationId = requestId;
            if (Send(new TerrainExcavationRequest
            {
                ClientRequestID = requestId,
                ExpectedTerrainRevision = terrainManager.Revision,
                TargetCell = new GridCoord(targetCell.x, targetCell.y),
                ItemID = itemId,
                DamageAmount = Mathf.Max(1, damageAmount),
            }) == false)
            {
                pendingExcavationId = 0;
                Debug.LogWarning("[RoomClient] 연결이 종료되어 채굴 요청을 보내지 못했습니다.", this);
                return false;
            }
            return true;
        }

        public bool RequestWorldItemPickup(string dropID, Vector2 dropPosition)
        {
            if (!IsConnected || !handshakeComplete || string.IsNullOrWhiteSpace(dropID) ||
                pendingWorldItemPickups.Add(dropID) == false)
                return false;

            string requestId = $"pickup-{Interlocked.Increment(ref nextRequestId)}";
            if (Send(new WorldItemPickupRequest
                {
                    RequestId = requestId,
                    DropID = dropID,
                    X = dropPosition.x,
                    Y = dropPosition.y,
                }))
                return true;

            pendingWorldItemPickups.Remove(dropID);
            return false;
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
                        EnqueueMessage<MapSessionMessage>(json, HandleMapSession);
                        break;
                    case NetworkMessageTypes.TerrainSnapshot:
                        EnqueueMessage<TerrainSnapshotMessage>(json, HandleTerrainSnapshot);
                        break;
                    case NetworkMessageTypes.TerrainChangeBatch:
                        EnqueueMessage<TerrainChangeBatchMessage>(json, HandleTerrainBatch);
                        break;
                    case NetworkMessageTypes.WorldItemSnapshot:
                        EnqueueMessage<WorldItemSnapshotMessage>(json, HandleWorldItemSnapshot);
                        break;
                    case NetworkMessageTypes.WorldItemSpawned:
                        EnqueueMessage<WorldItemSpawnedMessage>(json, HandleWorldItemSpawned);
                        break;
                    case NetworkMessageTypes.WorldItemRemoved:
                        EnqueueMessage<WorldItemRemovedMessage>(json, HandleWorldItemRemoved);
                        break;
                    case NetworkMessageTypes.InventorySnapshot:
                        EnqueueMessage<InventorySnapshotMessage>(json, HandleInventorySnapshot);
                        break;
                    case "error":
                        EnqueueMessage<ErrorMessage>(json, HandleError);
                        break;
                    case "welcome":
                        EnqueueMainThread(HandleWelcome);
                        break;
                    // 서버가 주기적으로 보내는 기존 위치 동기화 패킷이다.
                    // 원격 플레이어 동기화는 아직 범위 밖이므로 로그/메인 스레드 작업 없이 무시한다.
                    case "join":
                    case "leave":
                    case "state":
                        break;
                    default:
                        EnqueueMainThread(() => Debug.LogWarning($"[RoomClient] Unsupported message type: {header?.Type}", this));
                        break;
                }
            }
            catch (Exception exception) { QueueConnectionError("message parse", exception); }
        }

        private void EnqueueMainThread(Action action) => mainThreadActions.Enqueue(action);

        private void EnqueueMessage<T>(string json, Action<T> handler)
        {
            T message = JsonConvert.DeserializeObject<T>(json);
            EnqueueMainThread(() => handler(message));
        }

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
            if (snapshot == null || snapshot.CellSize <= 0f ||
                snapshot.MapWidth <= 0 || snapshot.MapHeight <= 0)
            {
                Debug.LogError("[RoomClient] Invalid terrain snapshot.", this);
                return;
            }
            terrainWorld.LoadSnapshot(snapshot);
            RespawnLocalPlayerOnSnapshot(snapshot);
            CurrentTerrainSnapshot = snapshot;
            hasTerrainSnapshot = true;
            pendingExcavationId = 0;
            TerrainSnapshotLoaded?.Invoke(snapshot);
            Debug.Log(
                $"[RoomClient] Terrain snapshot loaded: revision={snapshot.Revision}, " +
                $"size={snapshot.MapWidth}x{snapshot.MapHeight}, cells={snapshot.Cells.Count}",
                this);
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
            if (pendingExcavationId == batch.CollapseId)
                pendingExcavationId = 0;
            if (pendingCollapsePlacementId == batch.CollapseId)
                pendingCollapsePlacementId = 0;
            if (terrainManager.ApplyAuthoritativeBatch(batch, out string error) == false)
                Debug.LogWarning($"[RoomClient] Terrain batch rejected; awaiting snapshot resync. {error}", this);
        }

        private void HandleError(ErrorMessage message)
        {
            // 현재는 채굴 요청을 하나만 허용하므로 terrain 오류는 해당 요청의 종료 응답이다.
            // 성공 Batch뿐 아니라 실패 응답에서도 반드시 pending을 해제해야 다음 요청이 가능하다.
            if (message?.Code?.StartsWith("terrain.", StringComparison.Ordinal) == true)
            {
                pendingExcavationId = 0;
                pendingCollapsePlacementId = 0;
            }

            if (message?.Code?.StartsWith("item.", StringComparison.Ordinal) == true ||
                message?.Code?.StartsWith("inventory.", StringComparison.Ordinal) == true)
                pendingWorldItemPickups.Clear();

            if (message?.Code == "terrain.revision_mismatch")
            {
                // 이 Snapshot이 도착할 때까지 이전 Revision으로 새 요청을 보내지 않는다.
                hasTerrainSnapshot = false;
                Debug.LogWarning("[RoomClient] Revision mismatch; the server will send a terrain snapshot for resync.", this);
                return;
            }

            Debug.LogError($"[RoomClient] Server error {message?.Code}: {message?.Message}", this);
        }

        private void HandleWorldItemSnapshot(WorldItemSnapshotMessage message)
        {
            if (itemDropSpawner == null)
            {
                Debug.LogError("[RoomClient] ItemDropSpawner reference is not assigned.", this);
                return;
            }

            pendingWorldItemPickups.Clear();
            itemDropSpawner.ReplaceNetworkDrops(message?.Drops);
        }

        private void HandleWorldItemSpawned(WorldItemSpawnedMessage message)
        {
            WorldItemDropDto drop = message?.Drop;
            if (itemDropSpawner == null || drop == null) return;

            itemDropSpawner.TrySpawnNetworkDrop(
                drop.DropID,
                drop.ItemID,
                drop.Quantity,
                new Vector2(drop.X, drop.Y),
                out _);
        }

        private void HandleWorldItemRemoved(WorldItemRemovedMessage message)
        {
            if (string.IsNullOrWhiteSpace(message?.DropID)) return;

            pendingWorldItemPickups.Remove(message.DropID);
            itemDropSpawner?.RemoveNetworkDrop(message.DropID);
        }

        private void HandleInventorySnapshot(InventorySnapshotMessage message)
        {
            if (playerInventory == null || message?.Items == null) return;

            List<KeyValuePair<int, int>> quantities = new(message.Items.Length);
            foreach (InventoryItemDto item in message.Items)
                if (item != null) quantities.Add(new KeyValuePair<int, int>(item.ItemID, item.Quantity));
            playerInventory.ReplaceWithSnapshot(quantities);
        }

        private void HandlePlacementProposed(TerrainPlacementProposal proposal)
        {
            if (proposal == null || IsAuthoritativeTerrainMode == false) return;
            if (pendingCollapsePlacementId > 0)
            {
                Debug.LogWarning("[RoomClient] 이미 지형 붕괴 배치 확정을 기다리고 있습니다.", this);
                return;
            }

            TerrainCollapsePlacementRequest request = new()
            {
                CollapseID = proposal.CollapseId,
                ExpectedTerrainRevision = proposal.BaseRevision,
            };

            foreach (Vector2Int sourceCell in proposal.SourceCells)
                request.SourceCells.Add(new GridCoord(sourceCell.x, sourceCell.y));

            foreach (TerrainCellChange change in proposal.Changes)
            {
                List<TerrainLootEntryDto> lootEntries = new();
                if (change.LootEntries != null)
                    foreach (TerrainCellLootEntry entry in change.LootEntries)
                        lootEntries.Add(new TerrainLootEntryDto(entry.ItemID, entry.Quantity));

                request.Changes.Add(new TerrainCellChangeDto
                {
                    Coord = new GridCoord(change.Coord.x, change.Coord.y),
                    TileTypeID = (int)change.Type,
                    Durability = change.Durability,
                    ResourceID = change.ResourceID,
                    LootEntries = lootEntries,
                });
            }

            pendingCollapsePlacementId = proposal.CollapseId;
            if (Send(request)) return;

            pendingCollapsePlacementId = 0;
            Debug.LogWarning("[RoomClient] 붕괴 지형 배치 요청을 보내지 못했습니다.", this);
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

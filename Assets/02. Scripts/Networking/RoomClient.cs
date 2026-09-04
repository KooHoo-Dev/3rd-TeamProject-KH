using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    /// <remarks>
    /// Terrain관련 내용들(쌤이 변경한거)은 RoomClient.Terrain 참고
    /// </remarks>
    public sealed partial class RoomClient : MonoBehaviour
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
        [SerializeField] private TerrainCollapseCoordinator collapseCoordinator;
        [SerializeField] private ItemDropSpawner itemDropSpawner;
        [SerializeField] private PlayerInventory playerInventory;

        [SerializeField] private PlayerController localPlayer;
        [SerializeField, Min(1f)] private float positionSendRate = 10f;

        [Header("Remote Player")]
        [SerializeField] private RemotePlayerView remotePlayerPrefab;
        [SerializeField] private Transform remotePlayerRoot;

        private Vector2 lastSentPosition;
        private float nextPositionSendTime;
        private bool hasSentPosition;

        private readonly ConcurrentQueue<string> outgoingJson = new();
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private readonly SemaphoreSlim sendSignal = new(0);
        private readonly HashSet<string> pendingWorldItemPickups = new();
        private readonly HashSet<string> pendingWorldItemDrops = new();

        private readonly SortedDictionary<uint, TerrainChangeBatchMessage> bufferedTerrainBatches = new();
        private readonly Dictionary<string, RemotePlayerView> remotePlayers = new();
        private readonly string clientSessionId = Guid.NewGuid().ToString("N");
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private long nextRequestId;
        private bool hasTerrainSnapshot;
        private bool handshakeComplete;
        private long pendingCollapsePlacementId;
        private string pendingCollapsePlacementRequestId;
        private string pendingDeathLootRequestId;
        private string localPlayerId;

        public static RoomClient Instance { get; private set; }
        public bool IsConnected => socket?.State == WebSocketState.Open;
        public bool IsAuthoritativeTerrainMode => IsConnected && hasTerrainSnapshot;
        public bool IsLocalPlayerInSpawnArea =>
            localPlayer != null && IsInSpawnArea(localPlayer.Position);
        public MapSessionDescriptor MapSession { get; private set; }
        public TerrainSnapshot CurrentTerrainSnapshot { get; private set; }
        /// <summary>현재 씬에 표시 중인 원격 플레이어 View 목록이다.</summary>
        public IEnumerable<RemotePlayerView> RemotePlayerViews => remotePlayers.Values;
        public event Action<TerrainSnapshot> TerrainSnapshotLoaded;
        public event Action<string> RemotePlayerRemoved;

        /// <summary>서버 스냅샷 정의한 스폰/안전 구역에 월드 좌표가 포함되는지 확인한다</summary>
        public bool IsInSpawnArea(Vector2 worldPosition)
        {
            TerrainSnapshot snapshot = CurrentTerrainSnapshot;
            if (IsAuthoritativeTerrainMode == false || snapshot == null ||
                snapshot.CellSize <= 0f || snapshot.SpawnArea.width <= 0 ||
                snapshot.SpawnArea.height <= 0)
                return false;

            Vector2Int cell = new(
                Mathf.FloorToInt((worldPosition.x - snapshot.Origin.x) / snapshot.CellSize),
                Mathf.FloorToInt((worldPosition.y - snapshot.Origin.y) / snapshot.CellSize));
            return snapshot.SpawnArea.Contains(cell);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[RoomClient] 씬에는 RoomClient를 하나만 두어야 합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (terrainWorld == null)
                terrainWorld = FindFirstObjectByType<TerrainGridWorld>();
            if (terrainManager == null)
                terrainManager = FindFirstObjectByType<TerrainManager>();
            if (collapseManager == null)
                collapseManager = FindFirstObjectByType<CollapseManager>();
            if (collapseCoordinator == null)
                collapseCoordinator = FindFirstObjectByType<TerrainCollapseCoordinator>();
            if (itemDropSpawner == null)
                itemDropSpawner = FindFirstObjectByType<ItemDropSpawner>();
            if (localPlayer == null)
                localPlayer = FindFirstObjectByType<PlayerController>();
            if (playerInventory == null && localPlayer != null)
                playerInventory = localPlayer.GetComponent<PlayerInventory>();
            if (remotePlayerRoot == null)
                remotePlayerRoot = transform;
        }

        private void OnEnable()
        {
            if (collapseManager != null)
            {
                collapseManager.PlacementProposed += HandlePlacementProposed;
            }
            if (collapseCoordinator != null)
                collapseCoordinator.CollapseStartRequested += HandleCollapseStartRequested;
        }

        private void OnDisable()
        {
            if (collapseManager != null)
            {
                collapseManager.PlacementProposed -= HandlePlacementProposed;
            }
            if (collapseCoordinator != null)
                collapseCoordinator.CollapseStartRequested -= HandleCollapseStartRequested;
        }

        private async void Start()
        {
            if (connectOnStart) await ConnectAsync();
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out Action action))
                action.Invoke();

            SweepPendingTimeouts();

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
                        Type = NetworkMessageTypes.PlayerMove,
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
            localPlayer?.SetWaitingForTerrainSnapshot(true);
            await DisconnectAsync();
            // 이전 연결에서 전송하지 못한 패킷은 새 연결의 hello보다 먼저 보내면 안 된다.
            outgoingJson.Clear();
            hasTerrainSnapshot = false;
            CurrentTerrainSnapshot = null;
            bufferedTerrainBatches.Clear();
            ClearRemotePlayers();
            hasSentPosition = false;
            handshakeComplete = false;
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();

            try
            {
                Uri uri = BuildRoomUri();
                await socket.ConnectAsync(uri, cancellation.Token);
                _ = SendLoopAsync(socket, cancellation.Token);
                _ = ReceiveLoopAsync(socket, cancellation.Token);
                Send(new { Type = NetworkMessageTypes.Hello, NickName = nickName });
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
            bufferedTerrainBatches.Clear();
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
            ClearAllPendingExcavations();
            pendingWorldItemPickups.Clear();
            pendingWorldItemDrops.Clear();
            pendingDeathLootRequestId = null;
            ClearPendingCollapsePlacement();
            ClearRemotePlayers();
        }

        public bool RequestExcavation(Vector2Int targetCell, int itemId)
        {
            if (IsAuthoritativeTerrainMode == false)
            {
                Debug.LogWarning("[RoomClient] 서버 지형 준비 전이거나 연결되어 있지 않아 채굴 요청을 보내지 않았습니다.", this);
                return false;
            }
            if (itemId <= 0)
            {
                Debug.LogWarning($"[RoomClient] 서버가 허용하지 않는 곡괭이 ItemID입니다: {itemId}", this);
                return false;
            }
            // 같은 칸을 서버가 확정하기 전에 또 보내지 않는다. 다른 칸은 막지 않는다.
            //
            // 서버는 더 이상 Revision 을 대조하지 않으므로 굴착 여러 건이 동시에 떠 있어도 된다.
            // 순서는 서버가 매기는 Batch 의 BaseRevision/ResultRevision 이 지킨다.
            if (pendingExcavationCells.ContainsValue(targetCell))
            {
                Debug.Log("[RoomClient] 이 칸의 서버 확정을 기다리는 중입니다.", this);
                return false;
            }
            if (pendingExcavations.Count >= MaxInFlightExcavations)
            {
                Debug.Log("[RoomClient] 확정을 기다리는 채굴이 너무 많습니다.", this);
                return false;
            }
            long clientRequestId = Interlocked.Increment(ref nextRequestId);
            string requestId = CreateRequestId("excavate", clientRequestId);
            pendingExcavations[requestId] = Time.unscaledTime;
            pendingExcavationCells[requestId] = targetCell;
            if (Send(new TerrainExcavationRequest
            {
                RequestId = requestId,
                TargetCell = new GridCoord(targetCell.x, targetCell.y),
                ItemID = itemId,
            }) == false)
            {
                ClearPendingExcavation(requestId);
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

            string requestId = CreateRequestId(
                "pickup",
                Interlocked.Increment(ref nextRequestId));
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

        public bool RequestWorldItemDrop(int itemID, int quantity, Vector2 dropPosition)
        {
            if (IsAuthoritativeTerrainMode == false || itemID <= 0 || quantity <= 0)
                return false;

            string requestId = CreateRequestId(
                "drop",
                Interlocked.Increment(ref nextRequestId));
            if (pendingWorldItemDrops.Add(requestId) == false)
                return false;

            if (Send(new WorldItemDropRequest
                {
                    RequestId = requestId,
                    ItemID = itemID,
                    Quantity = quantity,
                    X = dropPosition.x,
                    Y = dropPosition.y,
                }))
                return true;

            pendingWorldItemDrops.Remove(requestId);
            return false;
        }

        public bool RequestDeathLoot(Vector2Int targetCell)
        {
            if (IsAuthoritativeTerrainMode == false ||
                string.IsNullOrEmpty(pendingDeathLootRequestId) == false)
                return false;

            string requestId = CreateRequestId(
                "death-loot",
                Interlocked.Increment(ref nextRequestId));
            pendingDeathLootRequestId = requestId;
            if (Send(new TerrainDeathLootRequest
                {
                    RequestId = requestId,
                    TargetCell = new GridCoord(targetCell.x, targetCell.y),
                }))
                return true;

            pendingDeathLootRequestId = null;
            return false;
        }

        private Uri BuildRoomUri()
        {
            string baseUrl = serverUrl.TrimEnd('/');
            return new Uri($"{baseUrl}/room?code={Uri.EscapeDataString(roomCode)}");
        }

        private string CreateRequestId(string operation, long sequence)
            => $"{operation}-{clientSessionId}-{sequence}";

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
            using MemoryStream message = new();
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
                        message.Write(buffer, 0, result.Count);
                    } while (result.EndOfMessage == false);

                    RouteIncoming(Encoding.UTF8.GetString(
                        message.GetBuffer(),
                        0,
                        (int)message.Length));
                    message.SetLength(0);
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

                    case NetworkMessageTypes.TerrainChangeBatch:
                        EnqueueMessage<TerrainChangeBatchMessage>(json, HandleTerrainBatch);
                        break;
                    case NetworkMessageTypes.TerrainCollapseStarted:
                        EnqueueMessage<TerrainCollapseStartedMessage>(json, HandleCollapseStarted);
                        break;
                    case NetworkMessageTypes.TerrainCollapseCancelled:
                        EnqueueMessage<TerrainCollapseCancelledMessage>(json, HandleCollapseCancelled);
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
                    case NetworkMessageTypes.Error:
                        EnqueueMessage<ErrorMessage>(json, HandleError);
                        break;
                    case NetworkMessageTypes.Welcome:
                        EnqueueMessage<WelcomeMessage>(json, HandleWelcome);
                        break;
                    case NetworkMessageTypes.Join:
                        EnqueueMessage<PlayerJoinedMessage>(json, HandlePlayerJoined);
                        break;
                    case NetworkMessageTypes.Leave:
                        EnqueueMessage<PlayerLeftMessage>(json, HandlePlayerLeft);
                        break;
                    case NetworkMessageTypes.State:
                        EnqueueMessage<PlayerStateMessage>(json, HandlePlayerState);
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

        private void HandleWelcome(WelcomeMessage message)
        {
            localPlayerId = message?.User?.Id;
            handshakeComplete = true;
            // 서버가 hello를 수락한 뒤에만 위치/게임플레이 패킷을 보낸다.
            nextPositionSendTime = Time.unscaledTime;

            if (message?.Users != null)
                foreach (NetworkUser user in message.Users)
                    SpawnRemotePlayer(user, Vector2.zero);

            Debug.Log("[RoomClient] Hello accepted.", this);
        }

        private void HandlePlayerJoined(PlayerJoinedMessage message)
        {
            SpawnRemotePlayer(message?.User, Vector2.zero);
        }

        private void HandlePlayerLeft(PlayerLeftMessage message)
        {
            RemoveRemotePlayer(message?.Id);
        }

        private void HandlePlayerState(PlayerStateMessage message)
        {
            if (message?.States == null) return;

            foreach (PlayerStateDto state in message.States)
            {
                if (state == null || state.Id == localPlayerId) continue;

                Vector2 position = new(state.X, state.Y);
                RemotePlayerView view = GetOrCreateRemotePlayer(state.Id, position);
                view?.SetServerPosition(position);
            }
        }

        private RemotePlayerView GetOrCreateRemotePlayer(string playerId, Vector2 position)
        {
            if (remotePlayers.TryGetValue(playerId, out RemotePlayerView existing))
                return existing;

            return SpawnRemotePlayer(new NetworkUser
            {
                Id = playerId,
                NickName = playerId,
            }, position);
        }

        private RemotePlayerView SpawnRemotePlayer(NetworkUser user, Vector2 position)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id) || user.Id == localPlayerId)
                return null;
            if (remotePlayers.TryGetValue(user.Id, out RemotePlayerView existing))
                return existing;
            if (remotePlayerPrefab == null)
            {
                Debug.LogError("[RoomClient] Remote Player Prefab reference is not assigned.", this);
                return null;
            }

            RemotePlayerView view = Instantiate(
                remotePlayerPrefab,
                position,
                Quaternion.identity,
                remotePlayerRoot);
            view.Initialize(user.Id, position);
            remotePlayers.Add(user.Id, view);
            return view;
        }

        private void ClearRemotePlayers()
        {
            foreach (KeyValuePair<string, RemotePlayerView> pair in remotePlayers)
            {
                if (pair.Value != null) Destroy(pair.Value.gameObject);
                RemotePlayerRemoved?.Invoke(pair.Key);
            }

            remotePlayers.Clear();
        }

        private void RemoveRemotePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) ||
                remotePlayers.Remove(playerId, out RemotePlayerView view) == false)
                return;

            if (view != null) Destroy(view.gameObject);
            RemotePlayerRemoved?.Invoke(playerId);
        }

        private void ApplyTerrainSnapshot(TerrainSnapshot snapshot)
        {
            if (terrainWorld == null)
            {
                Debug.LogError("[RoomClient] TerrainGridWorld reference is not assigned.", this);
                return;
            }
            if (snapshot == null || snapshot.CellSize <= 0f ||
                snapshot.MapWidth <= 0 || snapshot.MapHeight <= 0)
            {
                Debug.LogError("[RoomClient] 만들어진 지형이 올바르지 않습니다.", this);
                return;
            }
            terrainWorld.LoadSnapshot(snapshot);
            collapseManager?.ClearActiveChunks();
            RespawnLocalPlayerOnSnapshot(snapshot);
            localPlayer?.SetWaitingForTerrainSnapshot(false);
            CurrentTerrainSnapshot = snapshot;
            hasTerrainSnapshot = true;
            // Snapshot은 해당 Revision까지의 완전한 상태다. 이전에 쌓인 Batch는 이미 반영됐거나
            // 더 이상 이어 붙일 수 없으므로 버린다.
            bufferedTerrainBatches.Clear();
            ClearAllPendingExcavations();
            TerrainSnapshotLoaded?.Invoke(snapshot);
            Debug.Log(
                $"[RoomClient] Terrain built locally: revision={snapshot.Revision}, " +
                $"size={snapshot.MapWidth}x{snapshot.MapHeight}, cells={snapshot.Cells.Count}",
                this);
        }

        private void DrainBufferedTerrainBatches()
        {
            while (bufferedTerrainBatches.TryGetValue(
                       terrainManager.Revision,
                       out TerrainChangeBatchMessage next))
            {
                bufferedTerrainBatches.Remove(terrainManager.Revision);

                if (ApplyTerrainBatch(next) == false) return;
            }
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

            uint localRevision = terrainManager.Revision;
            // 재전송 또는 Snapshot 이전에 큐잉된 과거 Batch다.
            //
            // 아래 갈래들은 Apply 를 거치지 않고 빠져나간다.
            // 그래도 서버는 이 요청에 답을 한 것이므로 대기는 여기서 풀어야 한다.
            // 안 풀면 그 RequestId 가 영영 남아 그 칸을 다시 못 판다.
            if (batch.ResultRevision <= localRevision)
            {
                ReleasePendingFor(message.RequestId);
                return;
            }

            if (batch.BaseRevision > localRevision)
            {
                // 현재 Batch보다 앞선 Revision이 아직 도착하지 않았다. 다음 수신/적용 후
                // DrainBufferedTerrainBatches에서 자연스럽게 적용된다.
                bufferedTerrainBatches[batch.BaseRevision] = message;
                Debug.LogWarning(
                    $"[RoomClient] Terrain batch buffered: local={localRevision}, " +
                    $"required={batch.BaseRevision}, result={batch.ResultRevision}",
                    this);
                return;
            }

            if (batch.BaseRevision < localRevision)
            {
                // ResultRevision이 현재보다 큰데 BaseRevision이 과거인 Batch는 단일 Revision
                // 변경 계약을 위반한 메시지다. Apply 호출 대신 안전하게 무시한다.
                ReleasePendingFor(message.RequestId);
                Debug.LogWarning(
                    $"[RoomClient] Ignored invalid terrain batch: local={localRevision}, " +
                    $"base={batch.BaseRevision}, result={batch.ResultRevision}",
                    this);
                return;
            }

            if (ApplyTerrainBatch(message)) DrainBufferedTerrainBatches();
        }

        private bool ApplyTerrainBatch(TerrainChangeBatchMessage message)
        {
            TerrainChangeBatch batch = TerrainNetworkAdapter.ToRuntime(message?.Batch);

            if (batch == null) return false;

            bool isLocalExcavationResult =
                string.IsNullOrEmpty(message.RequestId) == false &&
                pendingExcavations.ContainsKey(message.RequestId);
            bool isLocalCollapsePlacementResult = string.Equals(
                message.RequestId,
                pendingCollapsePlacementRequestId,
                StringComparison.Ordinal);
            batch.IsPlacementOwner =
                isLocalExcavationResult || isLocalCollapsePlacementResult;

            // 서버가 이 요청에 답을 했다. 로컬 적용의 성패와 상관없이 기다림은 끝난다.
            // 적용 뒤에 풀면 적용이 한 번 실패하는 순간 그 칸이 영영 잠긴다.
            if (isLocalExcavationResult) ClearPendingExcavation(message.RequestId);
            if (isLocalCollapsePlacementResult) ClearPendingCollapsePlacement();

            if (terrainManager.ApplyAuthoritativeBatch(batch, out string error) == false)
            {
                Debug.LogWarning($"[RoomClient] Terrain batch rejected. {error}", this);
                return false;
            }

            return true;
        }

        private void HandleError(ErrorMessage message)
        {
            // 어느 요청의 답인지로만 푼다.
            //
            // 예전에는 코드가 "terrain." 으로 시작하기만 하면 굴착과 낙하를 한꺼번에 풀었다.
            // 그래서 낙하 시작 실패가 남의 굴착 대기를 잘못 풀어 주었다.
            // 반대로 굴착이 player.not_found 로 거절되면 아무것도 안 풀렸다.
            ReleasePendingFor(message?.RequestId);

            if (string.Equals(message?.RequestId, pendingDeathLootRequestId, StringComparison.Ordinal))
                pendingDeathLootRequestId = null;
            if (string.IsNullOrEmpty(message?.RequestId) == false)
                pendingWorldItemDrops.Remove(message.RequestId);

            if (message?.Code?.StartsWith("item.", StringComparison.Ordinal) == true ||
                message?.Code?.StartsWith("inventory.", StringComparison.Ordinal) == true)
                pendingWorldItemPickups.Clear();

            if (message?.Code == "terrain.collapse_conflict")
            {
                Debug.LogWarning("[RoomClient] 다른 클라이언트의 지형 확정을 기다립니다.", this);
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
            pendingWorldItemDrops.Clear();
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
                drop.PickupAvailableAtUnixMilliseconds,
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

            if (string.Equals(message.RequestId, pendingDeathLootRequestId, StringComparison.Ordinal))
                pendingDeathLootRequestId = null;
            if (string.IsNullOrEmpty(message.RequestId) == false)
                pendingWorldItemDrops.Remove(message.RequestId);

            List<KeyValuePair<int, int>> quantities = new(message.Items.Length);
            foreach (InventoryItemDto item in message.Items)
                if (item != null) quantities.Add(new KeyValuePair<int, int>(item.ItemID, item.Quantity));
            playerInventory.ReplaceWithSnapshot(
                quantities,
                message.CurrentWeight,
                message.MaxWeight);
        }

        private void HandlePlacementProposed(TerrainPlacementProposal proposal)
        {
            if (proposal == null || IsAuthoritativeTerrainMode == false) return;
            if (proposal.IsPlacementOwner == false) return;
            if (pendingCollapsePlacementId > 0)
            {
                Debug.LogWarning("[RoomClient] 이미 지형 붕괴 배치 확정을 기다리고 있습니다.", this);
                return;
            }

            TerrainCollapsePlacementRequest request = new()
            {
                RequestId = CreateRequestId(
                    "collapse",
                    Interlocked.Increment(ref nextRequestId)),
                CollapseID = proposal.CollapseId,
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
            pendingCollapsePlacementRequestId = request.RequestId;
            pendingCollapsePlacementSentAt = Time.unscaledTime;
            if (Send(request)) return;

            ClearPendingCollapsePlacement();
            Debug.LogWarning("[RoomClient] 붕괴 지형 배치 요청을 보내지 못했습니다.", this);
        }

        private void HandleCollapseStartRequested(IReadOnlyList<Vector2Int> sourceCells)
        {
            if (IsAuthoritativeTerrainMode == false || sourceCells == null || sourceCells.Count == 0)
                return;

            TerrainCollapseStartRequest request = new()
            {
                RequestId = CreateRequestId(
                    "collapse-start",
                    Interlocked.Increment(ref nextRequestId)),
            };
            foreach (Vector2Int sourceCell in sourceCells)
                request.SourceCells.Add(new GridCoord(sourceCell.x, sourceCell.y));

            if (Send(request) == false)
                Debug.LogWarning("[RoomClient] 낙하 지형 시작 요청을 보내지 못했습니다.", this);
        }

        private void HandleCollapseStarted(TerrainCollapseStartedMessage message)
        {
            if (collapseManager == null || message?.SourceCells == null ||
                message.SourceCells.Count == 0 || message.CollapseID <= 0)
                return;

            List<Vector2Int> sourceCells = new(message.SourceCells.Count);
            foreach (GridCoord cell in message.SourceCells)
                sourceCells.Add(new Vector2Int(cell.X, cell.Y));

            bool isPlacementOwner = string.Equals(
                message.OwnerPlayerID,
                localPlayerId,
                StringComparison.Ordinal);
            collapseManager.CreateChunk(
                sourceCells,
                message.CollapseID,
                isPlacementOwner);
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

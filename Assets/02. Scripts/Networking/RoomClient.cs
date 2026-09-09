using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Character;
using Newtonsoft.Json;
using TerrainCollapse.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [Header("Debug")]
        [SerializeField] private bool debugMode;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        [Header("Scene References")]
        [SerializeField] private TerrainGridWorld terrainWorld;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private TerrainCollapseCoordinator collapseCoordinator;
        [SerializeField] private ItemDropSpawner itemDropSpawner;
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private PlayerHp localPlayerHp;

        [SerializeField] private PlayerController localPlayer;
        [SerializeField, Min(1f)] private float positionSendRate = 10f;

        [Header("Remote Player")]
        [SerializeField] private RemotePlayerView remotePlayerPrefab;
        [SerializeField] private Transform remotePlayerRoot;

        [Header("Dynamite")]
        [SerializeField] private DynamiteExplosive dynamiteExplosivePrefab;
        [SerializeField] private Transform dynamiteRoot;

        [Header("Mine")]
        [SerializeField] private MineExplosive mineExplosivePrefab;
        [SerializeField] private Transform mineRoot;

        [Header("Game End Presentation")]
        [SerializeField, Range(0.01f, 1f)] private float gameEndTimeScale = 0.08f;
        [SerializeField, Min(0f)] private float gameEndSlowMotionSeconds = 1.5f;

        private Vector2 lastSentPosition;
        private Vector2 lastSentVelocity;
        private bool lastSentGrounded;
        private bool lastSentClimbing;
        private bool lastSentBuried;
        private float nextPositionSendTime;
        private bool hasSentPosition;

        private readonly ConcurrentQueue<string> outgoingJson = new();
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private readonly SemaphoreSlim sendSignal = new(0);
        private readonly HashSet<string> pendingWorldItemPickups = new();
        private readonly HashSet<string> pendingWorldItemDrops = new();
        private readonly Dictionary<string, string> pendingDynamiteExplosions = new();
        private readonly Dictionary<string, DynamiteExplosive> activeDynamites = new();
        private readonly Dictionary<string, MineExplosive> activeMines = new();
        private readonly Dictionary<string, float> nextDamageRequestTimes = new();
        private float nextAnyDamageRequestTime;
        private PickaxeInventory localPickaxeInventory;

        private readonly SortedDictionary<uint, TerrainChangeBatchMessage> bufferedTerrainBatches = new();
        private readonly Dictionary<string, RemotePlayerView> remotePlayers = new();
        private readonly string clientSessionId = Guid.NewGuid().ToString("N");
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private long nextRequestId;
        private bool hasTerrainSnapshot;
        private bool handshakeComplete;
        private bool hasSentPlayerReady;
        private bool isGameStarted;
        private bool isGameEnded;
        private string pendingInventorySellRequestId;
        private string pendingShopRequestId;
        private string pendingDebugGoldRequestId;
        private long pendingCollapsePlacementId;
        private string pendingCollapsePlacementRequestId;
        private string pendingDeathLootRequestId;
        private string localPlayerId;
        private Vector2? pendingInitialSpawn;
        private Coroutine gameEndPresentationCoroutine;
        private float timeScaleBeforeGameEnd = 1f;

        public static RoomClient Instance { get; private set; }
        public bool IsConnected => socket?.State == WebSocketState.Open;
        public bool IsAuthoritativeTerrainMode => IsConnected && hasTerrainSnapshot;
        private long serverClockOffsetMilliseconds;
        private long EstimatedServerNowUnixMilliseconds =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverClockOffsetMilliseconds;
        public bool IsMovementActive =>
            IsConnected && handshakeComplete && hasTerrainSnapshot && isGameStarted && isGameEnded == false;
        public bool IsGameplayActive =>
            IsMovementActive && GameStartedAtUnixMilliseconds > 0 &&
            EstimatedServerNowUnixMilliseconds >= GameStartedAtUnixMilliseconds;
        public bool HasGameStartSchedule => isGameStarted && GameStartedAtUnixMilliseconds > 0;
        public int GameCountdownSeconds { get; private set; }
        public double SecondsUntilGameplayStart => Math.Max(
            0d,
            (GameStartedAtUnixMilliseconds - EstimatedServerNowUnixMilliseconds) / 1000d);
        public long GameStartedAtUnixMilliseconds { get; private set; }
        public long GameEndsAtUnixMilliseconds { get; private set; }
        public long GameEndedAtUnixMilliseconds { get; private set; }
        public int GameDurationSeconds { get; private set; }
        public int VictoryGold { get; private set; }
        public string LocalPlayerID => localPlayerId;
        public double GameElapsedSeconds
        {
            get
            {
                if (GameStartedAtUnixMilliseconds <= 0)
                    return 0d;

                long currentMilliseconds =
                    isGameEnded && GameEndedAtUnixMilliseconds > 0
                        ? GameEndedAtUnixMilliseconds
                        : EstimatedServerNowUnixMilliseconds;

                return Math.Max(
                    0d,
                    (currentMilliseconds - GameStartedAtUnixMilliseconds) / 1000d);
            }
        }
        public bool IsLocalPlayerInSpawnArea =>
            localPlayer != null && IsInSpawnArea(localPlayer.Position);
        public Vector2 LocalPlayerPosition => localPlayer != null ? localPlayer.Position : Vector2.zero;
        public MapSessionDescriptor MapSession { get; private set; }
        public TerrainSnapshot CurrentTerrainSnapshot { get; private set; }
        /// <summary>현재 씬에 표시 중인 원격 플레이어 View 목록이다.</summary>
        public IEnumerable<RemotePlayerView> RemotePlayerViews => remotePlayers.Values;
        public int LocalPlayerColorIndex { get; private set; }
        public event Action<TerrainSnapshot> TerrainSnapshotLoaded;
        public event Action<string> RemotePlayerRemoved;
        public event Action LocalPlayerRespawned;
        public event Action<GameEndedMessage> GameEnded;
        public event Action<GameStartedMessage> GameStarted;
        public event Action<ChatBroadcastMessage> ChatMessageReceived;
        public event Action<ChatSystemMessage> SystemMessageReceived;
        public event Action<string> ShopNotificationReceived;
        public event Action<UsableItemUsedMessage> UsableItemUsed;
        public event Action<GoldRankingMessage> GoldRankingChanged;
        public GoldRankingMessage CurrentGoldRanking { get; private set; }

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
            LobbySession.ApplyTo(this);
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
            if (localPlayer != null)
                localPickaxeInventory = localPlayer.GetComponent<PickaxeInventory>();
            if (localPickaxeInventory == null && playerInventory != null)
                localPickaxeInventory = playerInventory.GetComponent<PickaxeInventory>();
            if (localPlayerHp == null && localPlayer != null)
                localPlayerHp = localPlayer.GetComponent<PlayerHp>();
            if (remotePlayerRoot == null)
                remotePlayerRoot = transform;
            if (dynamiteExplosivePrefab == null)
                dynamiteExplosivePrefab = Resources.Load<DynamiteExplosive>("DynamiteExplosive");
            if (dynamiteRoot == null)
                dynamiteRoot = transform;
            if (mineExplosivePrefab == null)
                mineExplosivePrefab = Resources.Load<MineExplosive>("Items/Usable/MineExplosive");
            if (mineRoot == null)
                mineRoot = transform;
        }

        /// <summary>대기방에서 확정한 접속 정보를 인게임 WebSocket 연결 전에 적용한다.</summary>
        public void ConfigureConnection(string nextServerUrl, string nextRoomCode, string nextNickName)
        {
            if (IsConnected || string.IsNullOrWhiteSpace(nextServerUrl) ||
                string.IsNullOrWhiteSpace(nextRoomCode) || string.IsNullOrWhiteSpace(nextNickName))
                return;

            serverUrl = nextServerUrl.TrimEnd('/');
            roomCode = nextRoomCode.Trim();
            nickName = nextNickName.Trim();
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

            if (IsMovementActive &&
                localPlayer != null &&
                Time.unscaledTime >= nextPositionSendTime)
            {
                if (TrySendLocalPlayerState(false) == false) return;

                nextPositionSendTime = Time.unscaledTime + 1f / positionSendRate;
            }
        }

        /// <summary>
        /// 위치를 검증하는 요청 바로 앞에 최신 이동 상태를 같은 송신 큐에 넣는다.
        /// 서버는 이 패킷 뒤의 판매/획득 요청을 순서대로 처리한다.
        /// </summary>
        public bool SendLocalPlayerStateNow() => TrySendLocalPlayerState(true);

        private bool TrySendLocalPlayerState(bool force)
        {
            if (IsMovementActive == false || localPlayer == null) return false;

            Vector2 position = localPlayer.Position;
            Vector2 velocity = localPlayer.Velocity;
            bool grounded = localPlayer.IsGrounded;
            bool climbing = localPlayer.IsClimbing;
            bool buried = localPlayer.IsBuried;
            bool hasChanged = hasSentPosition == false ||
                              (position - lastSentPosition).sqrMagnitude > 0.0001f ||
                              (velocity - lastSentVelocity).sqrMagnitude > 0.0001f ||
                              grounded != lastSentGrounded ||
                              climbing != lastSentClimbing ||
                              buried != lastSentBuried;

            if (force == false && hasChanged == false) return true;

            if (Send(new
            {
                Type = NetworkMessageTypes.PlayerMove,
                X = position.x,
                Y = position.y,
                VelocityX = velocity.x,
                VelocityY = velocity.y,
                IsGrounded = grounded,
                IsClimbing = climbing,
                IsBuried = buried,
            }) == false)
                return false;

            lastSentPosition = position;
            lastSentVelocity = velocity;
            lastSentGrounded = grounded;
            lastSentClimbing = climbing;
            lastSentBuried = buried;
            hasSentPosition = true;
            return true;
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
            CurrentGoldRanking = null;
            bufferedTerrainBatches.Clear();
            ClearRemotePlayers();
            ClearActiveDynamites();
            ClearActiveMines();
            hasSentPosition = false;
            handshakeComplete = false;
            pendingInitialSpawn = null;
            hasSentPlayerReady = false;
            isGameStarted = false;
            isGameEnded = false;
            pendingInventorySellRequestId = null;
            pendingShopRequestId = null;
            pendingDebugGoldRequestId = null;
            GameStartedAtUnixMilliseconds = 0;
            GameEndsAtUnixMilliseconds = 0;
            GameEndedAtUnixMilliseconds = 0;
            GameDurationSeconds = 0;
            VictoryGold = 0;
            GameCountdownSeconds = 0;
            serverClockOffsetMilliseconds = 0;
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();

            try
            {
                Uri uri = BuildRoomUri();
                await socket.ConnectAsync(uri, cancellation.Token);
                _ = SendLoopAsync(socket, cancellation.Token);
                _ = ReceiveLoopAsync(socket, cancellation.Token);
                Send(new
                {
                    Type = NetworkMessageTypes.Hello,
                    NickName = nickName,
                    ClientID = LobbySession.Instance?.ClientId,
                    DebugMode = debugMode,
                });
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
            StopGameEndPresentation();
            CancellationTokenSource source = cancellation;
            ClientWebSocket currentSocket = socket;
            cancellation = null;
            socket = null;
            hasTerrainSnapshot = false;
            CurrentTerrainSnapshot = null;
            CurrentGoldRanking = null;
            bufferedTerrainBatches.Clear();
            hasSentPosition = false;
            handshakeComplete = false;
            pendingInitialSpawn = null;
            hasSentPlayerReady = false;
            isGameStarted = false;
            isGameEnded = false;
            pendingInventorySellRequestId = null;
            pendingShopRequestId = null;
            pendingDebugGoldRequestId = null;
            GameStartedAtUnixMilliseconds = 0;
            GameEndsAtUnixMilliseconds = 0;
            GameEndedAtUnixMilliseconds = 0;
            GameDurationSeconds = 0;
            VictoryGold = 0;
            GameCountdownSeconds = 0;
            serverClockOffsetMilliseconds = 0;
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
            pendingDynamiteExplosions.Clear();
            nextDamageRequestTimes.Clear();
            nextAnyDamageRequestTime = 0f;
            pendingDeathLootRequestId = null;
            ClearPendingCollapsePlacement();
            ClearRemotePlayers();
            ClearActiveDynamites();
            ClearActiveMines();
        }

        public bool RequestExcavation(Vector2Int targetCell, int itemId)
        {
            if (IsGameplayActive == false)
            {
                Debug.LogWarning("[RoomClient] 서버 지형 준비 전이거나 연결되어 있지 않아 채굴 요청을 보내지 않았습니다.", this);
                return false;
            }
            if (itemId < 0)
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
            if (!IsGameplayActive || string.IsNullOrWhiteSpace(dropID) ||
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
            if (IsGameplayActive == false || itemID <= 0 || quantity <= 0)
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

        public bool RequestInventorySell()
        {
            if (IsGameplayActive == false || string.IsNullOrEmpty(pendingInventorySellRequestId) == false)
                return false;

            string requestId = CreateRequestId(
                "sell",
                Interlocked.Increment(ref nextRequestId));
            pendingInventorySellRequestId = requestId;

            if (Send(new InventorySellRequest { RequestId = requestId }))
                return true;

            pendingInventorySellRequestId = null;
            return false;

        }

        public bool RequestShop(string action, int itemID = 0)
        {
            if (IsGameplayActive == false || string.IsNullOrEmpty(pendingShopRequestId) == false)
                return false;

            string requestId = CreateRequestId(
                "shop",
                Interlocked.Increment(ref nextRequestId));
            pendingShopRequestId = requestId;
            if (Send(new ShopRequest
            {
                RequestId = requestId,
                Action = action,
                ItemID = itemID,
            }))
                return true;

            pendingShopRequestId = null;
            return false;
        }

        public bool RequestUsableItemUse(int itemID)
        {
            return IsGameplayActive && itemID > 0 && Send(new UsableItemUseRequest
            {
                RequestId = CreateRequestId("use", Interlocked.Increment(ref nextRequestId)),
                ItemID = itemID,
            });
        }

        public bool SendChat(string text)
        {
            if (!IsConnected || !handshakeComplete) return false;
            string normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (normalized.Length > 200) normalized = normalized.Substring(0, 200);
            return Send(new ChatSendRequest { Text = normalized });
        }

        public bool RequestDebugGold()
        {
            if (IsGameplayActive == false || string.IsNullOrEmpty(pendingDebugGoldRequestId) == false)
                return false;

            string requestId = CreateRequestId(
                "debug-gold",
                Interlocked.Increment(ref nextRequestId));
            pendingDebugGoldRequestId = requestId;

            if (Send(new InventoryDebugGoldRequest { RequestId = requestId }))
                return true;

            pendingDebugGoldRequestId = null;

            return false;
        }

        public bool RequestDynamiteThrow(
            int itemID,
            Vector2 startPosition,
            Vector2 targetPosition)
        {
            if (IsGameplayActive == false)
            {
                Debug.LogWarning(
                    "[RoomClient] 서버 연결 또는 hello 완료 전이라 다이너마이트를 사용할 수 없습니다.",
                    this);
                return false;
            }

            Vector2 direction = targetPosition - startPosition;

            if (direction.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning(
                    "[RoomClient] 다이너마이트 투척 방향이 너무 짧습니다.",
                    this);
                return false;
            }

            direction.Normalize();

            string requestID =
                $"dynamite-{Interlocked.Increment(ref nextRequestId)}";

            return Send(new DynamiteThrowRequest
            {
                RequestId = requestID,
                ItemID = itemID,

                StartX = startPosition.x,
                StartY = startPosition.y,

                DirectionX = direction.x,
                DirectionY = direction.y,
            });
        }

        public bool RequestDynamiteExplosion(
            string projectileID,
            Vector2 explosionPosition)
        {
            if (IsGameplayActive == false || string.IsNullOrWhiteSpace(projectileID))
                return false;

            string requestId = $"dynamite-explode-{Interlocked.Increment(ref nextRequestId)}";

            pendingDynamiteExplosions[requestId] = projectileID;

            bool sent = Send(new DynamiteExplodeRequest
            {
                RequestId = requestId,
                ProjectileID = projectileID,
                X = explosionPosition.x,
                Y = explosionPosition.y,
            });

            if (sent == false) pendingDynamiteExplosions.Remove(requestId);

            return sent;
        }

        public bool RequestMinePlacement(int itemID, Vector2 placementPosition)
        {
            if (IsGameplayActive == false || itemID <= 0)
                return false;

            return Send(new DynamiteThrowRequest
            {
                RequestId = $"mine-{Interlocked.Increment(ref nextRequestId)}",
                ItemID = itemID,
                StartX = placementPosition.x,
                StartY = placementPosition.y,
                DirectionX = 0f,
                DirectionY = 0f,
            });
        }

        public bool RequestDeathLoot(Vector2Int targetCell)
        {
            if (IsGameplayActive == false ||
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

        public bool RequestPlayerDamage(
            DamageType damageType,
            int amount,
            Vector2 playerPosition)
        {
            if (IsGameplayActive == false || amount <= 0)
                return false;

            string typeName = damageType.ToString();
            float cooldown = damageType == DamageType.Burial ? 0.5f : 0.1f;
            if (Time.unscaledTime < nextAnyDamageRequestTime)
                return false;
            if (nextDamageRequestTimes.TryGetValue(typeName, out float nextTime) &&
                Time.unscaledTime < nextTime)
                return false;

            nextDamageRequestTimes[typeName] = Time.unscaledTime + cooldown;
            nextAnyDamageRequestTime = Time.unscaledTime + 0.1f;
            string requestId = CreateRequestId(
                "damage",
                Interlocked.Increment(ref nextRequestId));

            if (Send(new PlayerDamageRequest
                {
                    RequestId = requestId,
                    DamageType = typeName,
                    Amount = amount,
                    X = playerPosition.x,
                    Y = playerPosition.y,
                }))
                return true;

            nextDamageRequestTimes.Remove(typeName);
            nextAnyDamageRequestTime = 0f;
            return false;
        }

        public bool RequestPlayerHeal(int amount, Vector2 playerPosition)
        {
            if (IsGameplayActive == false || amount <= 0)
                return false;

            return Send(new PlayerHealRequest
            {
                RequestId = CreateRequestId(
                    "heal",
                    Interlocked.Increment(ref nextRequestId)),
                Amount = amount,
                X = playerPosition.x,
                Y = playerPosition.y,
            });
        }

        public bool RequestRespawn()
        {
            if (IsGameplayActive == false)
                return false;

            return Send(new PlayerRespawnRequest
            {
                RequestId = CreateRequestId(
                    "respawn",
                    Interlocked.Increment(ref nextRequestId)),
            });
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
                    case NetworkMessageTypes.InventoryGoldRanking:
                        EnqueueMessage<GoldRankingMessage>(json, HandleGoldRanking);
                        break;
                    case NetworkMessageTypes.UsableItemUsed:
                        EnqueueMessage<UsableItemUsedMessage>(json, message => UsableItemUsed?.Invoke(message));
                        break;
                    case NetworkMessageTypes.PlayerHealthSnapshot:
                        EnqueueMessage<PlayerHealthSnapshotMessage>(json, HandlePlayerHealthSnapshot);
                        break;
                    case NetworkMessageTypes.PlayerHealthChanged:
                        EnqueueMessage<PlayerHealthChangedMessage>(json, HandlePlayerHealthChanged);
                        break;
                    case NetworkMessageTypes.PlayerDied:
                        EnqueueMessage<PlayerDiedMessage>(json, HandlePlayerDied);
                        break;
                    case NetworkMessageTypes.PlayerRespawned:
                        EnqueueMessage<PlayerRespawnedMessage>(json, HandlePlayerRespawned);
                        break;
                    case NetworkMessageTypes.PlayerKicked:
                        EnqueueMessage<PlayerKickedMessage>(json, HandlePlayerKicked);
                        break;
                    case NetworkMessageTypes.DynamiteThrown:
                        EnqueueMessage<DynamiteThrownMessage>(json, HandleDynamiteThrown);
                        break;
                    case NetworkMessageTypes.DynamiteExploded:
                        EnqueueMessage<DynamiteExplodedMessage>(json, HandleDynamiteExploded);
                        break;
                    case NetworkMessageTypes.Error:
                        EnqueueMessage<ErrorMessage>(json, HandleError);
                        break;
                    case NetworkMessageTypes.Welcome:
                        EnqueueMessage<WelcomeMessage>(json, HandleWelcome);
                        break;
                    case NetworkMessageTypes.GameStarted:
                        EnqueueMessage<GameStartedMessage>(json, HandleGameStarted);
                        break;
                    case NetworkMessageTypes.GameEnded:
                        EnqueueMessage<GameEndedMessage>(json, HandleGameEnded);
                        break;
                    case NetworkMessageTypes.ChatMessage:
                        EnqueueMessage<ChatBroadcastMessage>(json, message => ChatMessageReceived?.Invoke(message));
                        break;
                    case NetworkMessageTypes.ChatSystem:
                        EnqueueMessage<ChatSystemMessage>(json, message => SystemMessageReceived?.Invoke(message));
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
                    case NetworkMessageTypes.PlayerAction:
                        EnqueueMessage<PlayerActionMessage>(json, HandlePlayerAction);
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

            if (message != null) pendingInitialSpawn = new Vector2(message.SpawnX, message.SpawnY);

            handshakeComplete = true;
            int localColorIndex = LobbySession.Instance != null
                ? LobbySession.Instance.GetPlayerColorIndex(message?.User?.ClientID)
                : 0;
            LocalPlayerColorIndex = localColorIndex;
            PlayerColorPalette.ApplyToSpriteRenderers(localPlayer?.gameObject, localColorIndex);
            localPlayer?.GetComponent<PlayerNameTag>()?.SetIdentity(
                message?.User?.NickName,
                localColorIndex);
            // 서버가 hello를 수락한 뒤에만 위치/게임플레이 패킷을 보낸다.
            nextPositionSendTime = Time.unscaledTime;

            if (message?.Users != null)
                foreach (NetworkUser user in message.Users)
                    SpawnRemotePlayer(user, Vector2.zero);

            Debug.Log("[RoomClient] Hello accepted.", this);
        }

        private void HandleGameStarted(GameStartedMessage message)
        {
            if (isGameStarted || message == null) return;

            StopGameEndPresentation();
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            serverClockOffsetMilliseconds = message.ServerNowUnixMilliseconds - localNow;
            isGameStarted = true;
            isGameEnded = false;
            GameCountdownSeconds = Mathf.Max(0, message.CountdownSeconds);
            GameStartedAtUnixMilliseconds = message.StartedAtUnixMilliseconds;
            GameEndsAtUnixMilliseconds = message.EndsAtUnixMilliseconds;
            GameEndedAtUnixMilliseconds = 0;
            GameDurationSeconds = message.GameDurationSeconds;
            VictoryGold = message.VictoryGold;
            localPlayer?.SetWaitingForTerrainSnapshot(false);
            nextPositionSendTime = Time.unscaledTime;
            GameStarted?.Invoke(message);
            Debug.Log(
                $"[RoomClient] All players are ready. Gameplay starts in " +
                $"{SecondsUntilGameplayStart:F1}s.",
                this);
        }

        private void HandleGameEnded(GameEndedMessage message)
        {
            if (isGameEnded) return;

            isGameEnded = true;
            GameEndedAtUnixMilliseconds = message?.EndedAtUnixMilliseconds ??
                                          DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pendingInventorySellRequestId = null;
            pendingDebugGoldRequestId = null;
            localPlayer?.SetWaitingForTerrainSnapshot(true);
            StartGameEndPresentation();
            GameEnded?.Invoke(message);
            string winners = message?.WinnerPlayerIDs == null
                ? string.Empty
                : string.Join(", ", message.WinnerPlayerIDs);
            Debug.Log($"[RoomClient] Game ended. reason={message?.Reason}, winners={winners}", this);
        }

        private void HandlePlayerKicked(PlayerKickedMessage message)
        {
            localPlayer?.SetWaitingForTerrainSnapshot(true);
            LobbySession session = LobbySession.Ensure();
            session.NotifyKicked();
            session.ClearRoom();
            // 서버가 강제 Close를 보낸 상태에서는 CloseAsync가 handshake를 기다릴 수 있다.
            // 화면 전환은 해당 정리를 기다리지 않고 즉시 수행한다.
            _ = DisconnectAsync();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private void StartGameEndPresentation()
        {
            StopGameEndPresentation();
            timeScaleBeforeGameEnd = Time.timeScale;
            Time.timeScale = Mathf.Clamp(gameEndTimeScale, 0.01f, 1f);
            gameEndPresentationCoroutine = StartCoroutine(RestoreTimeScaleAfterGameEnd());
        }

        private IEnumerator RestoreTimeScaleAfterGameEnd()
        {
            yield return new WaitForSecondsRealtime(gameEndSlowMotionSeconds);
            Time.timeScale = timeScaleBeforeGameEnd;
            gameEndPresentationCoroutine = null;
        }

        private void StopGameEndPresentation()
        {
            if (gameEndPresentationCoroutine == null) return;

            StopCoroutine(gameEndPresentationCoroutine);
            gameEndPresentationCoroutine = null;
            Time.timeScale = timeScaleBeforeGameEnd;
        }

        private void HandlePlayerJoined(PlayerJoinedMessage message)
        {
            SpawnRemotePlayer(message?.User, Vector2.zero);
        }

        private void HandlePlayerLeft(PlayerLeftMessage message)
        {
            RemoveRemotePlayer(message?.User?.Id ?? message?.Id);
        }

        private void HandlePlayerState(PlayerStateMessage message)
        {
            if (message?.States == null) return;

            foreach (PlayerStateDto state in message.States)
            {
                if (state == null || state.Id == localPlayerId) continue;

                Vector2 position = new(state.X, state.Y);
                RemotePlayerView view = GetOrCreateRemotePlayer(state.Id, position);
                view?.SetServerState(state);
            }
        }

        private void HandlePlayerAction(PlayerActionMessage message)
        {
            if (message == null || message.PlayerID == localPlayerId) return;
            if (message.Action == "mine" &&
                remotePlayers.TryGetValue(message.PlayerID, out RemotePlayerView remote))
                remote.PlayMine(message.DirectionX);
        }

        private void HandlePlayerHealthSnapshot(PlayerHealthSnapshotMessage message)
        {
            if (message?.Players == null) return;

            foreach (PlayerHealthStateDto player in message.Players)
            {
                if (player?.PlayerID == localPlayerId)
                    ApplyLocalPlayerHealth(player, false);
                else if (player != null &&
                         remotePlayers.TryGetValue(player.PlayerID, out RemotePlayerView remote))
                    remote.SetDead(player.IsDead);
            }
        }

        private void HandlePlayerHealthChanged(PlayerHealthChangedMessage message)
        {
            PlayerHealthStateDto player = message?.Player;
            if (player?.PlayerID == localPlayerId)
            {
                int previousHp = localPlayerHp != null ? localPlayerHp.CurrentHp : player.CurrentHealth;
                ApplyLocalPlayerHealth(player, player.IsDead);
                if (localPlayerHp != null && previousHp - localPlayerHp.CurrentHp >= 3)
                    GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerHurt);
            }
            else if (player != null &&
                     remotePlayers.TryGetValue(player.PlayerID, out RemotePlayerView remote))
                remote.SetDead(player.IsDead);
        }

        private void HandlePlayerDied(PlayerDiedMessage message)
        {
            PlayerHealthStateDto player = message?.Player;
            if (player?.PlayerID == localPlayerId)
            {
                ApplyLocalPlayerHealth(player, true);
            }
            else if (player != null &&
                     remotePlayers.TryGetValue(player.PlayerID, out RemotePlayerView remote))
                remote.SetDead(true);
        }

        private void HandlePlayerRespawned(PlayerRespawnedMessage message)
        {
            if (message?.Player?.PlayerID == localPlayerId)
            {
                localPlayer?.Respawn(new Vector2(message.X, message.Y));
                LocalPlayerRespawned?.Invoke();
            }
            else if (message?.Player != null &&
                     remotePlayers.TryGetValue(message.Player.PlayerID, out RemotePlayerView remote))
            {
                remote.Teleport(new Vector2(message.X, message.Y));
                remote.SetDead(false);
            }
            ApplyLocalPlayerHealth(message?.Player, false);
        }

        private void ApplyLocalPlayerHealth(PlayerHealthStateDto state, bool notifyDeath)
        {
            if (state?.PlayerID != localPlayerId || localPlayerHp == null) return;

            localPlayerHp.ApplyAuthoritativeState(
                state.CurrentHealth,
                state.MaxHealth,
                state.IsDead,
                notifyDeath);
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
            int colorIndex = LobbySession.Instance != null
                ? LobbySession.Instance.GetPlayerColorIndex(user.ClientID)
                : 0;
            if (remotePlayers.TryGetValue(user.Id, out RemotePlayerView existing))
            {
                existing.SetIdentity(user.NickName, colorIndex);
                PlayerColorPalette.ApplyToSpriteRenderers(existing.gameObject, colorIndex);
                return existing;
            }
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
            view.Initialize(user.Id, user.NickName, position, colorIndex);
            PlayerColorPalette.ApplyToSpriteRenderers(view.gameObject, colorIndex);
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

            if (pendingInitialSpawn.HasValue)
            {
                localPlayer?.Respawn(pendingInitialSpawn.Value);
                pendingInitialSpawn = null;
            }
            else RespawnLocalPlayerOnSnapshot(snapshot);

            CurrentTerrainSnapshot = snapshot;
            hasTerrainSnapshot = true;
            // Snapshot은 해당 Revision까지의 완전한 상태다. 이전에 쌓인 Batch는 이미 반영됐거나
            // 더 이상 이어 붙일 수 없으므로 버린다.
            bufferedTerrainBatches.Clear();
            ClearAllPendingExcavations();
            TerrainSnapshotLoaded?.Invoke(snapshot);
            SendPlayerReady();
            Debug.Log(
                $"[RoomClient] Terrain built locally: revision={snapshot.Revision}, " +
                $"size={snapshot.MapWidth}x{snapshot.MapHeight}, cells={snapshot.Cells.Count}",
                this);
        }

        private void SendPlayerReady()
        {
            if (hasSentPlayerReady || !IsConnected || !handshakeComplete || !hasTerrainSnapshot)
                return;

            if (Send(new PlayerReadyMessage()) == false)
            {
                Debug.LogWarning("[RoomClient] Failed to send player.ready.", this);
                return;
            }

            hasSentPlayerReady = true;
            Debug.Log("[RoomClient] Terrain ready; player.ready sent.", this);
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
            bool isLocalDynamiteExplosionResult =
                string.IsNullOrEmpty(message.RequestId) == false &&
                pendingDynamiteExplosions.ContainsKey(message.RequestId);
            batch.IsPlacementOwner =
                isLocalExcavationResult ||
                isLocalCollapsePlacementResult ||
                isLocalDynamiteExplosionResult;

            // 서버가 이 요청에 답을 했다. 로컬 적용의 성패와 상관없이 기다림은 끝난다.
            // 적용 뒤에 풀면 적용이 한 번 실패하는 순간 그 칸이 영영 잠긴다.
            if (isLocalExcavationResult) ClearPendingExcavation(message.RequestId);
            if (isLocalCollapsePlacementResult) ClearPendingCollapsePlacement();
            if (isLocalDynamiteExplosionResult) pendingDynamiteExplosions.Remove(message.RequestId);

            if (terrainManager.ApplyAuthoritativeBatch(batch, out string error) == false)
            {
                Debug.LogWarning($"[RoomClient] Terrain batch rejected. {error}", this);
                return false;
            }

            return true;
        }

        private void HandleError(ErrorMessage message)
        {
            bool isShopRequest = string.Equals(
                message?.RequestId,
                pendingShopRequestId,
                StringComparison.Ordinal);

            // 어느 요청의 답인지로만 푼다.
            //
            // 예전에는 코드가 "terrain." 으로 시작하기만 하면 굴착과 낙하를 한꺼번에 풀었다.
            // 그래서 낙하 시작 실패가 남의 굴착 대기를 잘못 풀어 주었다.
            // 반대로 굴착이 player.not_found 로 거절되면 아무것도 안 풀렸다.
            ReleasePendingFor(message?.RequestId);
            if (string.IsNullOrEmpty(message?.RequestId) == false &&
                pendingDynamiteExplosions.Remove(message.RequestId, out string projectileID) &&
                activeMines.TryGetValue(projectileID, out MineExplosive mine))
                mine.AllowExplosionRetry();

            if (string.Equals(message?.RequestId, pendingDeathLootRequestId, StringComparison.Ordinal))
                pendingDeathLootRequestId = null;
            if (string.IsNullOrEmpty(message?.RequestId) == false)
                pendingWorldItemDrops.Remove(message.RequestId);
            if (string.Equals(message?.RequestId, pendingInventorySellRequestId, StringComparison.Ordinal))
                pendingInventorySellRequestId = null;
            if (string.Equals(message?.RequestId, pendingShopRequestId, StringComparison.Ordinal))
                pendingShopRequestId = null;
            if (string.Equals(message?.RequestId, pendingDebugGoldRequestId, StringComparison.Ordinal))
                pendingDebugGoldRequestId = null;

            if (message?.Code?.StartsWith("item.", StringComparison.Ordinal) == true ||
                message?.Code?.StartsWith("inventory.", StringComparison.Ordinal) == true)
                pendingWorldItemPickups.Clear();

            if (message?.Code is "terrain.collapse_pending" or "terrain.collapse_conflict")
                return;

            string errorText = string.IsNullOrWhiteSpace(message?.Message)
                ? message?.Code ?? "알 수 없는 서버 오류"
                : message.Message;

            if (isShopRequest)
            {
                ShopNotificationReceived?.Invoke(errorText);
                Debug.LogWarning($"[RoomClient] Shop request failed: {message?.Code}: {errorText}", this);
                return;
            }

            bool isHealthFull = message?.Code == "player.health_full";
            SystemMessageReceived?.Invoke(new ChatSystemMessage
            {
                ElapsedSeconds = Mathf.FloorToInt((float)GameElapsedSeconds),
                Text = isHealthFull ? errorText : $"요청 처리에 실패했습니다: {errorText}",
            });
            if (isHealthFull)
                Debug.Log($"[RoomClient] {errorText}", this);
            else
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

        private void HandleDynamiteThrown(DynamiteThrownMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.ProjectileID))
                return;

            if (message.IsMine)
            {
                HandleMinePlaced(message);
                return;
            }

            if (activeDynamites.ContainsKey(message.ProjectileID))
                return;

            if (dynamiteExplosivePrefab == null)
            {
                Debug.LogError(
                    "[RoomClient] DynamiteExplosive prefab reference is not assigned.",
                    this);
                return;
            }

            Vector2 startPosition = new(message.StartX, message.StartY);

            DynamiteExplosive explosive = Instantiate(
                dynamiteExplosivePrefab,
                startPosition,
                Quaternion.identity,
                dynamiteRoot);

            bool isOwner = message.OwnerPlayerID == localPlayerId;

            explosive.LaunchFromServer(message, isOwner);

            activeDynamites.Add(message.ProjectileID, explosive);
        }

        private void HandleDynamiteExploded(
            DynamiteExplodedMessage message)
        {
            if (message == null)
                return;

            if (message.IsMine)
            {
                HandleMineExploded(message);
                return;
            }

            if (string.IsNullOrEmpty(message.RequestId) == false)
                pendingDynamiteExplosions.Remove(message.RequestId);

            if (string.IsNullOrWhiteSpace(message.ProjectileID)) return;

            if (activeDynamites.TryGetValue(
                    message.ProjectileID,
                    out DynamiteExplosive explosive) == false)
            {
                Debug.LogWarning($"[RoomClient] 폭발할 다이너마이트를 찾지 못했습니다: {message.ProjectileID}", this);

                return;
            }

            activeDynamites.Remove(message.ProjectileID);

            if (explosive == null)
                return;

            explosive.ExplodeFromServer(new Vector2(message.X, message.Y), message.ExplosionRadius);
        }

        private void ClearActiveDynamites()
        {
            foreach (DynamiteExplosive explosive in activeDynamites.Values)
                Destroy(explosive?.gameObject);

            activeDynamites.Clear();
        }

        private void HandleMinePlaced(DynamiteThrownMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.ProjectileID) ||
                activeMines.ContainsKey(message.ProjectileID))
                return;

            if (mineExplosivePrefab == null)
            {
                Debug.LogError("[RoomClient] MineExplosive prefab reference is not assigned.", this);
                return;
            }

            MineExplosive mine = Instantiate(
                mineExplosivePrefab,
                new Vector2(message.StartX, message.StartY),
                Quaternion.identity,
                mineRoot);
            mine.PlaceFromServer(message);
            activeMines.Add(message.ProjectileID, mine);
        }

        private void HandleMineExploded(DynamiteExplodedMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.ProjectileID) ||
                activeMines.Remove(message.ProjectileID, out MineExplosive mine) == false)
                return;

            mine?.ExplodeFromServer(
                new Vector2(message.X, message.Y),
                message.ExplosionRadius);
        }

        private void ClearActiveMines()
        {
            foreach (MineExplosive mine in activeMines.Values)
                Destroy(mine?.gameObject);

            activeMines.Clear();
        }

        private void HandleInventorySnapshot(InventorySnapshotMessage message)
        {
            if (playerInventory == null || message?.Items == null) return;

            if (string.Equals(message.RequestId, pendingDeathLootRequestId, StringComparison.Ordinal))
                pendingDeathLootRequestId = null;
            if (string.IsNullOrEmpty(message.RequestId) == false)
                pendingWorldItemDrops.Remove(message.RequestId);
            if (string.Equals(message.RequestId, pendingInventorySellRequestId, StringComparison.Ordinal))
                pendingInventorySellRequestId = null;
            if (string.Equals(message.RequestId, pendingShopRequestId, StringComparison.Ordinal))
                pendingShopRequestId = null;
            if (string.Equals(message.RequestId, pendingDebugGoldRequestId, StringComparison.Ordinal))
                pendingDebugGoldRequestId = null;

            List<KeyValuePair<int, int>> quantities = new(message.Items.Length);
            foreach (InventoryItemDto item in message.Items)
                if (item != null) quantities.Add(new KeyValuePair<int, int>(item.ItemID, item.Quantity));
            playerInventory.ReplaceWithSnapshot(
                quantities,
                message.CurrentWeight,
                message.MaxWeight);
            if (localPickaxeInventory == null)
            {
                Debug.LogError("[RoomClient] LocalPlayer의 PickaxeInventory를 찾지 못해 장착 곡괭이를 적용하지 못했습니다.", this);
                return;
            }

            localPickaxeInventory.ApplyAuthoritativePickaxe(message.EquippedPickaxeItemID);
            Debug.Log($"[RoomClient] 장착 곡괭이 Snapshot 적용: item={message.EquippedPickaxeItemID}", this);
        }

        private void HandleGoldRanking(GoldRankingMessage message)
        {
            CurrentGoldRanking = message;
            GoldRankingChanged?.Invoke(message);
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

        private void HandleCollapseStartRequested(IReadOnlyList<List<Vector2Int>> groups)
        {
            if (IsAuthoritativeTerrainMode == false || groups == null || groups.Count == 0)
                return;

            TerrainCollapseStartRequest request = new()
            {
                RequestId = CreateRequestId(
                    "collapse-start",
                    Interlocked.Increment(ref nextRequestId)),
            };
            foreach (List<Vector2Int> group in groups)
            {
                if (group == null || group.Count == 0) continue;

                TerrainCollapseStartGroupDto groupDto = new();
                foreach (Vector2Int sourceCell in group)
                    groupDto.SourceCells.Add(new GridCoord(sourceCell.x, sourceCell.y));
                request.Groups.Add(groupDto);
            }

            if (request.Groups.Count == 0) return;

            if (Send(request) == false)
                Debug.LogWarning("[RoomClient] 낙하 지형 시작 요청을 보내지 못했습니다.", this);
        }

        private void HandleCollapseStarted(TerrainCollapseStartedMessage message)
        {
            if (collapseManager == null || message?.Collapses == null)
                return;

            foreach (TerrainCollapseStartedDto collapse in message.Collapses)
            {
                if (collapse?.SourceCells == null ||
                    collapse.SourceCells.Count == 0 || collapse.CollapseID <= 0)
                    continue;

                List<Vector2Int> sourceCells = new(collapse.SourceCells.Count);
                foreach (GridCoord cell in collapse.SourceCells)
                    sourceCells.Add(new Vector2Int(cell.X, cell.Y));

                bool isPlacementOwner = string.Equals(
                    collapse.OwnerPlayerID,
                    localPlayerId,
                    StringComparison.Ordinal);
                collapseManager.CreateChunk(
                    sourceCells,
                    collapse.CollapseID,
                    isPlacementOwner);
            }
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
                bool isSpawnPlatform = terrainManager != null &&
                                       terrainManager.TryGetTileType(
                                           groundCell,
                                           out TerrainTileType groundType) &&
                                       groundType == TerrainTileType.SpawnPlatform;
                Vector2Int spawnCell = groundCell +
                                       Vector2Int.up * (isSpawnPlatform ? 2 : 1);
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

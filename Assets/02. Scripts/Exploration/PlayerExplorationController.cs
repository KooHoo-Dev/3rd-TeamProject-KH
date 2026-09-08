using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어 위치를 기준으로 탐험 상태와 현재 관측 셀 목록 갱신

    public sealed class PlayerExplorationController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private Transform localPlayer;

        [Header("Visual Settings")]
        [SerializeField, Min(0)] private int visionRadius;
        [SerializeField, Min(0)] private int currentVisionFogRadius = 6;

        private ExplorationMapData explorationData;
        private readonly HashSet<Vector2Int> currentObservedCells = new();
        private bool initialized;
        private bool hasLastExplorationCell;
        private bool needsVisionRefresh;
        private Vector2Int lastExplorationCell;

        public ExplorationMapData ExplorationData => explorationData;
        public IReadOnlyCollection<Vector2Int> CurrentObservedCells => currentObservedCells;

        public event Action<IReadOnlyList<Vector2Int>> CellsExplored;
        public event Action<IReadOnlyList<Vector2Int>> CellsObserved;
        public event Action ExplorationInitialized;
        public event Action ExplorationRestored;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
            hasLastExplorationCell = false;
            needsVisionRefresh = true;
        }

        private void Awake()
        {
            terrainManager ??= FindFirstObjectByType<TerrainManager>();
            roomClient ??= FindFirstObjectByType<RoomClient>();

            if (localPlayer == null)
            {
                PlayerController player = FindFirstObjectByType<PlayerController>();
                if (player != null)
                    localPlayer = player.transform;
                else
                {
                    TestPlayerController testPlayer = FindFirstObjectByType<TestPlayerController>();
                    if (testPlayer != null) localPlayer = testPlayer.transform;
                }
            }
        }

        private void OnEnable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged += HandleTerrainChanged;
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded += HandleTerrainSnapshotLoaded;

            needsVisionRefresh = true;
            TryInitialize();
        }

        private void OnDisable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged -= HandleTerrainChanged;
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded -= HandleTerrainSnapshotLoaded;
        }

        private bool CanInitialize()
        {
            if (terrainManager == null) return false;
            if (localPlayer == null) return false;
            if (terrainManager.IsReady == false) return false;

            return terrainManager.TryGetMapBounds(out _, out _, out _, out _);
        }

        private void InitializeExploration()
        {
            if (terrainManager.TryGetMapBounds(
                    out int mapWidth,
                    out int mapHeight,
                    out _,
                    out _) == false)
                return;

            explorationData = new ExplorationMapData(mapWidth, mapHeight);
            currentObservedCells.Clear();
            initialized = true;
            hasLastExplorationCell = false;
            needsVisionRefresh = true;

            ExplorationInitialized?.Invoke();
            RefreshPlayerVision();
        }

        private void TryInitialize()
        {
            if (initialized == false && CanInitialize())
                InitializeExploration();
        }

        private void HandleTerrainSnapshotLoaded(TerrainSnapshot snapshot)
        {
            if (snapshot == null || snapshot.MapWidth <= 0 || snapshot.MapHeight <= 0)
                return;

            // 같은 방의 재동기화는 이미 탐험한 영역을 보존한다.
            if (initialized && explorationData.Width == snapshot.MapWidth &&
                explorationData.Height == snapshot.MapHeight)
            {
                needsVisionRefresh = true;
                return;
            }

            initialized = false;
            TryInitialize();
        }

        private void Update()
        {
            if (initialized == false)
            {
                TryInitialize();
                return;
            }
            if (localPlayer == null) return;

            RefreshPlayerVision();
        }

        private void RefreshPlayerVision()
        {
            if (terrainManager.TryWorldToCell(
                    localPlayer.position,
                    out Vector2Int playerCell) == false) return;

            if (hasLastExplorationCell &&
                playerCell == lastExplorationCell &&
                needsVisionRefresh == false) return;

            lastExplorationCell = playerCell;
            hasLastExplorationCell = true;
            needsVisionRefresh = false;

            ExploreVisionRadius(playerCell);
        }

        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            needsVisionRefresh = true;
        }

        private void ExploreVisionRadius(Vector2Int centerCell)
        {
            List<Vector2Int> newlyExploredCells = new();
            List<Vector2Int> currentVisionCells = new();

            int explorationRadiusSquared = visionRadius * visionRadius;

            for (int y = -visionRadius; y <= visionRadius; y++)
            {
                for (int x = -visionRadius; x <= visionRadius; x++)
                {
                    int distanceSquared = x * x + y * y;
                    if (distanceSquared > explorationRadiusSquared) continue;

                    Vector2Int cell = centerCell + new Vector2Int(x, y);

                    if (explorationData.IsInside(cell) == false) continue;
                    if (HasLineOfSight(centerCell, cell) == false) continue;

                    if (explorationData.TryExplore(cell))
                    {
                        newlyExploredCells.Add(cell);
                    }
                }
            }

            CollectCurrentVisionCells(centerCell, currentVisionCells);

            if (newlyExploredCells.Count > 0)
                CellsExplored?.Invoke(newlyExploredCells);

            currentObservedCells.Clear();
            currentObservedCells.UnionWith(currentVisionCells);

            if (currentVisionCells.Count > 0)
                CellsObserved?.Invoke(currentVisionCells);
        }

        private void CollectCurrentVisionCells(
            Vector2Int centerCell,
            List<Vector2Int> cells)
        {
            HashSet<Vector2Int> visibleCells = new() { centerCell };

            AddCurrentVisionRay(centerCell, centerCell, Vector2Int.up, 2, visibleCells);
            AddCurrentVisionRay(centerCell, centerCell, Vector2Int.down, 2, visibleCells);
            AddCurrentVisionRay(centerCell, centerCell, Vector2Int.left, 2, visibleCells);
            AddCurrentVisionRay(centerCell, centerCell, Vector2Int.right, 2, visibleCells);

            cells.AddRange(visibleCells);
        }

        private void AddCurrentVisionRay(
            Vector2Int centerCell,
            Vector2Int origin,
            Vector2Int direction,
            int remainingTurns,
            HashSet<Vector2Int> visibleCells)
        {
            for (int distance = 1; distance <= currentVisionFogRadius; distance++)
            {
                Vector2Int cell = origin + direction * distance;
                if (explorationData.IsInside(cell) == false) return;
                if (Mathf.Abs(cell.x - centerCell.x) > currentVisionFogRadius ||
                    Mathf.Abs(cell.y - centerCell.y) > currentVisionFogRadius)
                {
                    return;
                }

                visibleCells.Add(cell);
                if (terrainManager.HasTerrain(cell)) return;
                if (remainingTurns == 0) continue;

                Vector2Int firstTurnDirection =
                    new Vector2Int(-direction.y, direction.x);
                Vector2Int secondTurnDirection =
                    new Vector2Int(direction.y, -direction.x);

                AddCurrentVisionRay(
                    centerCell, cell, firstTurnDirection,
                    remainingTurns - 1, visibleCells);
                AddCurrentVisionRay(
                    centerCell, cell, secondTurnDirection,
                    remainingTurns - 1, visibleCells);
            }
        }

        private bool HasLineOfSight(Vector2Int origin, Vector2Int target)
        {
            int x = origin.x;
            int y = origin.y;
            int deltaX = Mathf.Abs(target.x - origin.x);
            int deltaY = Mathf.Abs(target.y - origin.y);
            int stepX = origin.x < target.x ? 1 : -1;
            int stepY = origin.y < target.y ? 1 : -1;
            int error = deltaX - deltaY;

            while (x != target.x || y != target.y)
            {
                int doubledError = error * 2;
                bool movesX = doubledError > -deltaY;
                bool movesY = doubledError < deltaX;

                if (movesX && movesY &&
                    terrainManager.HasTerrain(new Vector2Int(x + stepX, y)) &&
                    terrainManager.HasTerrain(new Vector2Int(x, y + stepY)))
                    return false;

                if (movesX)
                {
                    error -= deltaY;
                    x += stepX;
                }

                if (movesY)
                {
                    error += deltaX;
                    y += stepY;
                }

                Vector2Int cell = new(x, y);

                // 시야를 막는 타일 자체는 관측하고, 그 뒤 셀만 제외
                if (cell == target) return true;
                if (terrainManager.HasTerrain(cell)) return false;
            }

            return true;
        }

        public ExplorationSnapshot CreateSnapshot()
        {
            return explorationData?.CreateSnapshot();
        }

        public bool TryApplySnapshot(ExplorationSnapshot snapshot)
        {
            if (initialized == false) return false;
            if (explorationData.TryApplySnapshot(snapshot) == false) return false;

            ExplorationRestored?.Invoke();
            needsVisionRefresh = true;
            return true;
        }
    }
}

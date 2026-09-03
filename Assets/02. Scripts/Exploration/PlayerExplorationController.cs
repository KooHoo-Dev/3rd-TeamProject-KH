using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어 위치를 기준으로 탐험 상태와 현재 관측 셀 목록 갱신

    public sealed class PlayerExplorationController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private TerrainMapLoader mapLoader;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private Transform localPlayer;

        [Header("Visual Settings")]
        [SerializeField, Min(0)] private int visionRadius;

        [Header("Initialization")]
        [SerializeField, Min(0.1f)] private float initializationTimeout = 5f;

        private ExplorationMapData explorationData;
        private bool initialized;
        private bool hasLastExplorationCell;
        private bool needsVisionRefresh;
        private Vector2Int lastExplorationCell;

        public ExplorationMapData ExplorationData => explorationData;

        public event Action<IReadOnlyList<Vector2Int>> CellsExplored;
        public event Action<IReadOnlyList<Vector2Int>> CellsObserved;
        public event Action ExplorationRestored;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
            hasLastExplorationCell = false;
            needsVisionRefresh = true;
        }

        private void Awake()
        {
            mapLoader ??= FindFirstObjectByType<TerrainMapLoader>();
            terrainManager ??= FindFirstObjectByType<TerrainManager>();

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

        private IEnumerator Start()
        {
            float elapsed = 0f;

            while (elapsed < initializationTimeout)
            {
                if (CanInitialize())
                {
                    InitializeExploration();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void OnEnable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged += HandleTerrainChanged;

            needsVisionRefresh = true;
        }

        private void OnDisable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged -= HandleTerrainChanged;
        }

        private bool CanInitialize()
        {
            if (mapLoader == null) return false;
            if (terrainManager == null) return false;
            if (localPlayer == null) return false;
            if (mapLoader.LoadedMap == null) return false;
            if (terrainManager.IsReady == false) return false;

            return true;
        }

        private void InitializeExploration()
        {
            TerrainMapFile map = mapLoader.LoadedMap;

            explorationData = new ExplorationMapData(map.Width, map.Height);
            initialized = true;
            hasLastExplorationCell = false;
            needsVisionRefresh = true;

            RefreshPlayerVision();
        }

        private void Update()
        {
            if (initialized == false || localPlayer == null) return;

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
            List<Vector2Int> observedCells = new();

            int radiusSquared = visionRadius * visionRadius;

            for (int y = -visionRadius; y <= visionRadius; y++)
            {
                for (int x = -visionRadius; x <= visionRadius; x++)
                {
                    if (x * x + y * y > radiusSquared) continue;

                    Vector2Int cell = centerCell + new Vector2Int(x, y);

                    if (explorationData.IsInside(cell) == false) continue;
                    if (HasLineOfSight(centerCell, cell) == false) continue;

                    observedCells.Add(cell);

                    if (explorationData.TryExplore(cell)) newlyExploredCells.Add(cell);
                }
            }

            if (newlyExploredCells.Count > 0)
                CellsExplored?.Invoke(newlyExploredCells);

            if (observedCells.Count > 0)
                CellsObserved?.Invoke(observedCells);
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

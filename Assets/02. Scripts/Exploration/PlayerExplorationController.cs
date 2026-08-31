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
        private Vector2Int lastExplorationCell;

        public ExplorationMapData ExplorationData => explorationData;

        public event Action<IReadOnlyList<Vector2Int>> CellsExplored;
        public event Action<IReadOnlyList<Vector2Int>> CellsObserved;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
            hasLastExplorationCell = false;
        }

        private void Awake()
        {
            mapLoader ??= FindFirstObjectByType<TerrainMapLoader>();
            terrainManager ??= FindFirstObjectByType<TerrainManager>();

            if (localPlayer == null)
            {
                TestPlayerController player = FindFirstObjectByType<TestPlayerController>();
                if (player != null)
                    localPlayer = player.transform;
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

            if (hasLastExplorationCell && playerCell == lastExplorationCell) return;

            lastExplorationCell = playerCell;
            hasLastExplorationCell = true;

            ExploreVisionRadius(playerCell);
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

                    observedCells.Add(cell);

                    if (explorationData.TryExplore(cell)) newlyExploredCells.Add(cell);
                }
            }

            if (newlyExploredCells.Count > 0)
                CellsExplored?.Invoke(newlyExploredCells);

            if (observedCells.Count > 0)
                CellsObserved?.Invoke(observedCells);
        }
    }
}

using System.Collections.Generic;
using TerrainCollapse;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 기존 지형 시스템의 공개 조회 API만 사용하여 미니맵을 갱신한다.
    /// 지형 생성, 채굴, 붕괴, 네트워크 상태를 소유하지 않는다.
    /// </summary>
    public sealed class TerrainMinimapPresenter : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private Transform localPlayer;
        [SerializeField] private PlayerExplorationController explorationController;

        [Header("View")]
        [SerializeField] private MinimapView minimapView;

        [Header("Tile Colors")]
        [SerializeField] private Color emptyColor = new Color(0.03f, 0.04f, 0.06f, 1f);
        [SerializeField] private Color groundColor = new Color(0.32f, 0.22f, 0.12f, 1f);
        [SerializeField] private Color dirtColor = new Color(0.43f, 0.27f, 0.14f, 1f);
        [SerializeField] private Color stoneColor = new Color(0.42f, 0.45f, 0.5f, 1f);
        [SerializeField] private Color bedrockColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        [SerializeField] private Color deepStoneColor = new Color(0.2f, 0.22f, 0.28f, 1f);
        [SerializeField] private Color coalColor = new Color(0.12f, 0.13f, 0.15f, 1f);
        [SerializeField] private Color ironColor = new Color(0.82f, 0.34f, 0.12f, 1f);
        [SerializeField] private Color unknownTileColor = Color.magenta;
        [SerializeField] private Color unexploredColor = Color.black;

        private Color32[] pixels;
        private int mapWidth;
        private int mapHeight;
        private float cellSize;
        private Vector2 mapOrigin;
        private bool initialized;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
            explorationController?.SetLocalPlayer(playerTransform);
        }

        private void Awake()
        {
            if (roomClient == null) roomClient = FindFirstObjectByType<RoomClient>();
            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
            if (explorationController == null)
                explorationController = FindFirstObjectByType<PlayerExplorationController>();
            if (minimapView == null) minimapView = GetComponent<MinimapView>();
        }

        private void OnEnable()
        {
            if (explorationController != null)
            {
                explorationController.CellsObserved += HandleCellsObserved;
                explorationController.ExplorationInitialized += HandleExplorationInitialized;
                explorationController.ExplorationRestored += RefreshMap;
            }
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded += HandleTerrainSnapshotLoaded;
            TryInitialize();
        }

        private void OnDisable()
        {
            if (explorationController != null)
            {
                explorationController.CellsObserved -= HandleCellsObserved;
                explorationController.ExplorationInitialized -= HandleExplorationInitialized;
                explorationController.ExplorationRestored -= RefreshMap;
            }
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded -= HandleTerrainSnapshotLoaded;
        }

        private void Update()
        {
            if (initialized == false)
            {
                TryInitialize();
                return;
            }

            UpdateLocalPlayerMarker();
        }

        private void TryInitialize()
        {
            if (initialized == false && CanInitialize())
                InitializeMinimap();
        }

        private bool CanInitialize()
        {
            if (terrainManager == null) return false;
            if (explorationController == null) return false;
            if (minimapView == null) return false;
            if (terrainManager.IsReady == false) return false;
            if (explorationController.ExplorationData == null) return false;

            return terrainManager.TryGetMapBounds(out _, out _, out _, out _);
        }

        private void InitializeMinimap()
        {
            if (terrainManager.TryGetMapBounds(
                    out mapWidth,
                    out mapHeight,
                    out cellSize,
                    out mapOrigin) == false)
                return;

            if (minimapView.Initialize(mapWidth, mapHeight) == false) return;

            pixels = new Color32[mapWidth * mapHeight];
            initialized = true;
            RefreshMap();
            UpdateLocalPlayerMarker();
        }

        private void HandleTerrainSnapshotLoaded(TerrainSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (CanInitialize() == false)
            {
                TryInitialize();
                return;
            }

            if (initialized == false || mapWidth != snapshot.MapWidth || mapHeight != snapshot.MapHeight)
            {
                initialized = false;
                TryInitialize();
                return;
            }

            cellSize = snapshot.CellSize;
            mapOrigin = snapshot.Origin;
            RefreshMap();
            UpdateLocalPlayerMarker();
        }

        private void HandleExplorationInitialized()
        {
            TryInitialize();
        }

        private void HandleCellsObserved(IReadOnlyList<Vector2Int> cells)
        {
            if (initialized == false) return;

            foreach (Vector2Int cell in cells)
                RefreshCell(cell);

            minimapView.ApplyPixels(pixels);
        }

        private void RefreshMap()
        {
            if (pixels == null) return;

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    RefreshCell(cell);
                }
            }

            minimapView.ApplyPixels(pixels);
        }

        private void RefreshCell(Vector2Int cell)
        {
            if (cell.x < 0 || cell.x >= mapWidth || cell.y < 0 || cell.y >= mapHeight)
                return;

            int pixelIndex = cell.y * mapWidth + cell.x;

            if (explorationController.ExplorationData.IsExplored(cell) == false)
            {
                pixels[pixelIndex] = unexploredColor;
                return;
            }

            if (terrainManager.TryGetTileType(cell, out TerrainTileType type) == false)
            {
                pixels[pixelIndex] = emptyColor;
                return;
            }

            terrainManager.TryGetResourceID(cell, out int resourceID);
            pixels[pixelIndex] = GetTileColor(type, resourceID);
        }

        private Color32 GetTileColor(TerrainTileType type, int resourceID)
        {
            if (resourceID == 1) return coalColor;
            if (resourceID == 2) return ironColor;

            return type switch
            {
                TerrainTileType.Empty => emptyColor,
                TerrainTileType.Ground => groundColor,
                TerrainTileType.Dirt => dirtColor,
                TerrainTileType.Stone => stoneColor,
                TerrainTileType.Bedrock => bedrockColor,
                TerrainTileType.DeepStone => deepStoneColor,
                TerrainTileType.DeathLoot => Color.red,
                _ => unknownTileColor
            };
        }

        private void UpdateLocalPlayerMarker()
        {
            if (localPlayer == null)
            {
                minimapView.SetPlayerVisible(false);
                return;
            }

            if (terrainManager.TryGetMapBounds(
                    out int width,
                    out int height,
                    out float size,
                    out Vector2 origin) == false)
            {
                minimapView.SetPlayerVisible(false);
                return;
            }

            float mapWorldWidth = width * size;
            float mapWorldHeight = height * size;

            float normalizedX = Mathf.InverseLerp(
                origin.x,
                origin.x + mapWorldWidth,
                localPlayer.position.x);
            float normalizedY = Mathf.InverseLerp(
                origin.y,
                origin.y + mapWorldHeight,
                localPlayer.position.y);

            minimapView.SetPlayerNormalizedPosition(new Vector2(normalizedX, normalizedY));
        }
    }
}

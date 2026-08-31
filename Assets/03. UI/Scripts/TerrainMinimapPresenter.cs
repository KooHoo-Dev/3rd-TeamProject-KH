using System.Collections;
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
        [SerializeField] private TerrainMapLoader mapLoader;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private Transform localPlayer;
        [SerializeField] private PlayerExplorationController explorationController;

        [Header("View")]
        [SerializeField] private MinimapView minimapView;

        [Header("Initialization")]
        [SerializeField, Min(0.1f)] private float initializationTimeout = 5f;

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
        private bool initialized;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
            explorationController?.SetLocalPlayer(playerTransform);
        }

        private void Awake()
        {
            if (mapLoader == null) mapLoader = FindFirstObjectByType<TerrainMapLoader>();
            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
            if (explorationController == null)
                explorationController = FindFirstObjectByType<PlayerExplorationController>();
            if (minimapView == null) minimapView = GetComponent<MinimapView>();
        }

        private void OnEnable()
        {
            if (explorationController != null)
                explorationController.CellsObserved += HandleCellsObserved;
        }

        private void OnDisable()
        {
            if (explorationController != null)
                explorationController.CellsObserved -= HandleCellsObserved;
        }

        private IEnumerator Start()
        {
            float elapsed = 0f;

            while (elapsed < initializationTimeout)
            {
                if (CanInitialize())
                {
                    InitializeMinimap();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.LogError(
                "[UI] 제한 시간 안에 지형 미니맵을 초기화하지 못했습니다. 참조와 맵 로드 상태를 확인하십시오.",
                this);
        }

        private void Update()
        {
            if (initialized == false) return;

            UpdateLocalPlayerMarker();
        }

        private bool CanInitialize()
        {
            if (mapLoader == null) return false;
            if (terrainManager == null) return false;
            if (explorationController == null) return false;
            if (minimapView == null) return false;
            if (mapLoader.LoadedMap == null) return false;
            if (terrainManager.IsReady == false) return false;
            if (explorationController.ExplorationData == null) return false;

            return true;
        }

        private void InitializeMinimap()
        {
            TerrainMapFile loadedMap = mapLoader.LoadedMap;
            mapWidth = loadedMap.Width;
            mapHeight = loadedMap.Height;

            if (minimapView.Initialize(mapWidth, mapHeight) == false) return;

            pixels = new Color32[mapWidth * mapHeight];
            initialized = true;
            RefreshMap();
            UpdateLocalPlayerMarker();
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

            TerrainMapFile loadedMap = mapLoader.LoadedMap;

            if (loadedMap == null)
            {
                minimapView.SetPlayerVisible(false);
                return;
            }

            float mapWorldWidth = loadedMap.Width * loadedMap.CellSize;
            float mapWorldHeight = loadedMap.Height * loadedMap.CellSize;

            float normalizedX = Mathf.InverseLerp(
                loadedMap.Origin.x,
                loadedMap.Origin.x + mapWorldWidth,
                localPlayer.position.x);
            float normalizedY = Mathf.InverseLerp(
                loadedMap.Origin.y,
                loadedMap.Origin.y + mapWorldHeight,
                localPlayer.position.y);

            minimapView.SetPlayerNormalizedPosition(new Vector2(normalizedX, normalizedY));
        }
    }
}

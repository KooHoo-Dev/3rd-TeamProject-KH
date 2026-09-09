using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

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
        [SerializeField] private TMP_Text coordText;

        [Header("Coordinate Display")]
        [SerializeField] private Vector2 fullMapCoordOffset = new(16f, -16f);

        [Header("Tile Colors")]
        [SerializeField] private Color emptyColor = new Color(0.03f, 0.04f, 0.06f, 1f);
        [SerializeField] private Color groundColor = new Color(0.32f, 0.22f, 0.12f, 1f);
        [SerializeField] private Color dirtColor = new Color(0.43f, 0.27f, 0.14f, 1f);
        [SerializeField] private Color stoneColor = new Color(0.42f, 0.45f, 0.5f, 1f);
        [SerializeField] private Color bedrockColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        [SerializeField] private Color topBedrockColor = new Color(0.29f, 0.18f, 0.11f, 1f);
        [SerializeField] private Color deepStoneColor = new Color(0.2f, 0.22f, 0.28f, 1f);
        [SerializeField] private Color spawnPlatformColor = new Color(0.84f, 0.7f, 0.48f, 1f);
        [SerializeField] private Color deathLootColor = Color.red;
        [SerializeField] private Color coalColor = new Color(0.12f, 0.13f, 0.15f, 1f);
        [FormerlySerializedAs("ironColor")]
        [SerializeField] private Color bronzeColor = new Color(0.82f, 0.34f, 0.12f, 1f);
        [SerializeField] private Color silverColor = new Color(0.72f, 0.75f, 0.82f, 1f);
        [SerializeField] private Color goldColor = new Color(0.93f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color sapphireColor = new Color(0.18f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color rubyColor = new Color(0.86f, 0.12f, 0.2f, 1f);
        [SerializeField] private Color diamondColor = new Color(0.55f, 0.92f, 1f, 1f);
        [SerializeField] private Color unknownTileColor = Color.magenta;
        [SerializeField] private Color unexploredColor = Color.black;

        private Color32[] pixels;
        private int mapWidth;
        private int mapHeight;
        private float cellSize;
        private Vector2 mapOrigin;
        private bool initialized;
        private RectTransform coordRect;
        private Vector2 normalCoordAnchoredPosition;
        private GameObject loadingOverlay;

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
            if (coordText == null)
            {
                foreach (TMP_Text candidate in GetComponentsInChildren<TMP_Text>(true))
                {
                    if (candidate.name != "CoordText") continue;
                    coordText = candidate;
                    break;
                }

                if (coordText == null)
                    coordText = transform.root.Find("CoordText")?.GetComponent<TMP_Text>();
            }

            loadingOverlay = transform.root.Find("LoadingOverlay")?.gameObject;

            if (coordText != null)
            {
                if (coordText.transform.parent == transform && transform.parent != null)
                    coordText.transform.SetParent(transform.parent, false);

                coordRect = coordText.rectTransform;
                normalCoordAnchoredPosition = coordRect.anchoredPosition;
                coordRect.SetAsLastSibling();
                coordText.gameObject.SetActive(
                    loadingOverlay == null || loadingOverlay.activeInHierarchy == false);
            }
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
            {
                roomClient.TerrainSnapshotLoaded += HandleTerrainSnapshotLoaded;
                roomClient.RemotePlayerRemoved += HandleRemotePlayerRemoved;
            }
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
            {
                roomClient.TerrainSnapshotLoaded -= HandleTerrainSnapshotLoaded;
                roomClient.RemotePlayerRemoved -= HandleRemotePlayerRemoved;
            }
        }

        private void Update()
        {
            if (coordText != null && coordText.gameObject.activeSelf == false &&
                (loadingOverlay == null || loadingOverlay.activeInHierarchy == false))
                coordText.gameObject.SetActive(true);

            if (initialized == false)
            {
                TryInitialize();
                return;
            }

            UpdateLocalPlayerMarker();
            UpdateRemotePlayerMarkers();
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
            UpdateRemotePlayerMarkers();
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

            // 월드 안개 View와 동일하게 지상/지하 최상단 영역(y > -1)은 개인 탐험 여부와
            // 관계없이 공개한다. 전체 맵도 이 텍스처를 공유하므로 같은 결과가 적용된다.
            if (IsFogFreeCell(cell) == false &&
                explorationController.ExplorationData.IsExplored(cell) == false)
            {
                pixels[pixelIndex] = unexploredColor;
                return;
            }

            if (terrainManager.TryGetTileType(cell, out TerrainTileType type) == false)
            {
                pixels[pixelIndex] = emptyColor;
                return;
            }

            if (type == TerrainTileType.Bedrock && IsSurfaceCell(cell))
            {
                pixels[pixelIndex] = emptyColor;
                return;
            }

            terrainManager.TryGetResourceID(cell, out int resourceID);
            pixels[pixelIndex] = GetTileColor(cell, type, resourceID);
        }

        private bool IsSurfaceCell(Vector2Int cell)
        {
            float cellCenterY = mapOrigin.y + (cell.y + 0.5f) * cellSize;
            return cellCenterY > 0f;
        }

        private bool IsFogFreeCell(Vector2Int cell)
        {
            float cellCenterY = mapOrigin.y + (cell.y + 0.5f) * cellSize;
            return cellCenterY > -1f;
        }

        private Color32 GetTileColor(Vector2Int cell, TerrainTileType type, int resourceID)
        {
            if (type == TerrainTileType.Bedrock && IsFogFreeCell(cell))
                return topBedrockColor;

            switch (resourceID)
            {
                case 1: return coalColor;
                case 2: return bronzeColor;
                case 3: return silverColor;
                case 4: return goldColor;
                case 1001: return sapphireColor;
                case 1002: return rubyColor;
                case 1003: return diamondColor;
            }

            return type switch
            {
                TerrainTileType.Empty => emptyColor,
                TerrainTileType.Ground => groundColor,
                TerrainTileType.Dirt => dirtColor,
                TerrainTileType.Stone => stoneColor,
                TerrainTileType.Bedrock => bedrockColor,
                TerrainTileType.DeepStone => deepStoneColor,
                TerrainTileType.SpawnPlatform => spawnPlatformColor,
                TerrainTileType.DeathLoot => deathLootColor,
                _ => unknownTileColor
            };
        }

        private void UpdateLocalPlayerMarker()
        {
            UpdateCoordinateTextPosition();

            if (localPlayer == null)
            {
                minimapView.SetPlayerVisible(false);
                if (coordText != null) coordText.text = string.Empty;
                return;
            }

            if (coordText != null && terrainManager.TryWorldToCell(
                    localPlayer.position,
                    out Vector2Int cell))
            {
                TerrainDataManager terrainData = TerrainDataManager.Instance;
                terrainData.EnsureLoaded();
                string profileID = roomClient?.MapSession?.ProfileID ?? "Default";
                if (terrainData.SeedProfiles.TryGet(profileID, out TerrainSeedProfileDef profile))
                    coordText.text = $"X: {cell.x - profile.Width / 2}\nY: {cell.y - profile.Height}";
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

            minimapView.SetPlayerMarkerColor(PlayerColorPalette.GetColor(
                roomClient != null ? roomClient.LocalPlayerColorIndex : 0));
            minimapView.SetPlayerNormalizedPosition(new Vector2(normalizedX, normalizedY));
        }

        private void UpdateCoordinateTextPosition()
        {
            if (coordRect == null) return;

            coordRect.SetAsLastSibling();
            coordRect.anchoredPosition = normalCoordAnchoredPosition +
                (minimapView != null && minimapView.IsFullMapOpen
                    ? fullMapCoordOffset
                    : Vector2.zero);
        }


        private void UpdateRemotePlayerMarkers()
        {
            if (roomClient == null || minimapView == null ||
                terrainManager.TryGetMapBounds(
                    out int width,
                    out int height,
                    out float size,
                    out Vector2 origin) == false)
                return;

            float mapWorldWidth = width * size;
            float mapWorldHeight = height * size;
            foreach (Networking.RemotePlayerView remotePlayer in roomClient.RemotePlayerViews)
            {
                if (remotePlayer == null || string.IsNullOrWhiteSpace(remotePlayer.PlayerId))
                    continue;

                Vector3 position = remotePlayer.transform.position;
                Vector2 normalizedPosition = new(
                    Mathf.InverseLerp(origin.x, origin.x + mapWorldWidth, position.x),
                    Mathf.InverseLerp(origin.y, origin.y + mapWorldHeight, position.y));
                minimapView.SetRemotePlayerNormalizedPosition(
                    remotePlayer.PlayerId,
                    normalizedPosition,
                    PlayerColorPalette.GetColor(remotePlayer.ColorIndex));
            }
        }

        private void HandleRemotePlayerRemoved(string playerId)
        {
            minimapView?.RemoveRemotePlayerMarker(playerId);
        }
    }
}

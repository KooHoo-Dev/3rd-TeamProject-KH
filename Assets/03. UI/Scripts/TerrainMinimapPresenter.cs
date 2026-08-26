using System.Collections;
using TerrainCollapse;
using UnityEngine;

namespace GameUI
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

        private Color32[] pixels;
        private int mapWidth;
        private int mapHeight;
        private bool initialized;
        private Coroutine refreshRoutine;

        public void SetLocalPlayer(Transform playerTransform)
        {
            localPlayer = playerTransform;
        }

        private void Awake()
        {
            if (mapLoader == null) mapLoader = FindFirstObjectByType<TerrainMapLoader>();
            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
            if (minimapView == null) minimapView = GetComponent<MinimapView>();
        }

        private void OnEnable()
        {
            if (terrainManager == null) return;

            terrainManager.TerrainChanged += HandleTerrainChanged;
        }

        private void OnDisable()
        {
            if (terrainManager == null)
            {
                ClearRefreshRoutine();
                return;
            }

            terrainManager.TerrainChanged -= HandleTerrainChanged;
            ClearRefreshRoutine();
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
            if (minimapView == null) return false;
            if (mapLoader.LoadedMap == null) return false;
            if (terrainManager.IsReady == false) return false;

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

        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            if (initialized == false) return;
            if (refreshRoutine == null)
                refreshRoutine = StartCoroutine(RefreshAtEndOfFrame());
        }

        private IEnumerator RefreshAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            RefreshMap();
            refreshRoutine = null;
        }

        private void RefreshMap()
        {
            if (pixels == null) return;

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    int pixelIndex = y * mapWidth + x;

                    if (terrainManager.TryGetTileType(cell, out TerrainTileType type) == false)
                    {
                        pixels[pixelIndex] = emptyColor;
                        continue;
                    }

                    pixels[pixelIndex] = GetTileColor(type);
                }
            }

            minimapView.ApplyPixels(pixels);
        }

        private Color32 GetTileColor(TerrainTileType type)
        {
            return type switch
            {
                TerrainTileType.Empty => emptyColor,
                TerrainTileType.Ground => groundColor,
                TerrainTileType.Dirt => dirtColor,
                TerrainTileType.Stone => stoneColor,
                TerrainTileType.Bedrock => bedrockColor,
                TerrainTileType.DeepStone => deepStoneColor,
                TerrainTileType.CoalOre => coalColor,
                TerrainTileType.IronOre => ironColor,
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

            if (terrainManager.TryWorldToCell(localPlayer.position, out Vector2Int cell) == false)
            {
                minimapView.SetPlayerVisible(false);
                return;
            }

            minimapView.SetPlayerCell(cell);
        }

        private void ClearRefreshRoutine()
        {
            if (refreshRoutine == null) return;

            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }
    }
}

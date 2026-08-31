using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 미탐험 Grid 셀을 월드 암흑 오버레이로 표현

    public sealed class ExplorationForWorldView : MonoBehaviour
    {
        private const int RENDER_CHUNK_SIZE = 32;

        [Header("Sources")]
        [SerializeField] private TerrainMapLoader mapLoader;
        [SerializeField] private PlayerExplorationController explorationController;

        [Header("Visual Settings")]
        [SerializeField] private Color fogColor =
            new Color(0f, 0f, 0f, 0.88f);
        [SerializeField] private int sortingOrder = 1;

        [Header("Initialization")]
        [SerializeField, Min(0.1f)] private float initializationTimeout = 5f;

        private readonly Dictionary<Vector2Int, MeshFilter> chunkViews = new();
        private readonly HashSet<Vector2Int> dirtyChunks = new();

        private Transform fogRoot;
        private Material fogMaterial;
        private float cellSize;
        private bool initialized;
        private int mapWidth;
        private int mapHeight;

        private void Awake()
        {
            mapLoader ??= FindFirstObjectByType<TerrainMapLoader>();
            explorationController ??= FindFirstObjectByType<PlayerExplorationController>();

            CreateFogMaterial();
        }

        private void OnEnable()
        {
            explorationController.CellsExplored += HandleCellsExplored;
            explorationController.ExplorationRestored += HandleExplorationRestored;
        }

        private void OnDisable()
        {
            explorationController.CellsExplored -= HandleCellsExplored;
            explorationController.ExplorationRestored -= HandleExplorationRestored;
        }

        private void CreateFogMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");

            fogMaterial = new Material(shader)
            {
                mainTexture = Texture2D.whiteTexture
            };
        }

        private void OnDestroy()
        {
            ClearChunks();

            if (fogMaterial != null) Destroy(fogMaterial);
        }

        private IEnumerator Start()
        {
            float elapsed = 0f;

            while (elapsed < initializationTimeout)
            {
                if (CanInitialize())
                {
                    InitializeFog();

                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;

                yield return null;
            }

            Debug.LogError(
                "[Exploration] 제한 시간 안에 월드 암흑 시야를 초기화하지 못했습니다.",
                this);
        }

        private bool CanInitialize()
        {
            if (mapLoader == null) return false;
            if (explorationController == null) return false;
            if (mapLoader.LoadedMap == null) return false;
            if (explorationController.ExplorationData == null) return false;

            return true;
        }

        private void InitializeFog()
        {
            TerrainMapFile map = mapLoader.LoadedMap;

            cellSize = map.CellSize;
            mapWidth = map.Width;
            mapHeight = map.Height;

            GameObject root = new("ExplorationFogRoot");
            fogRoot = root.transform;
            fogRoot.SetParent(transform, false);
            fogRoot.position = map.Origin;

            BuildAllChunks(map.Width, map.Height);
            initialized = true;
        }

        private void BuildAllChunks(int mapWidth, int mapHeight)
        {
            int chunkWidth = Mathf.CeilToInt((float)mapWidth / RENDER_CHUNK_SIZE);
            int chunkHeight = Mathf.CeilToInt((float)mapHeight / RENDER_CHUNK_SIZE);

            for (int y = 0; y < chunkHeight; y++)
                for (int x = 0; x < chunkWidth; x++)
                    RebuildChunk(new Vector2Int(x, y));
        }

        private void HandleCellsExplored(
            IReadOnlyList<Vector2Int> cells)
        {
            if (initialized == false) return;

            foreach (Vector2Int cell in cells)
                dirtyChunks.Add(ToChunkCoord(cell));
        }

        private void LateUpdate()
        {
            if (dirtyChunks.Count == 0) return;

            foreach (Vector2Int chunkCoord in dirtyChunks)
                RebuildChunk(chunkCoord);

            dirtyChunks.Clear();
        }

        private static Vector2Int ToChunkCoord(Vector2Int cell)
        {
            return new Vector2Int(
                Mathf.FloorToInt((float)cell.x / RENDER_CHUNK_SIZE),
                Mathf.FloorToInt((float)cell.y / RENDER_CHUNK_SIZE));
        }

        private void RebuildChunk(Vector2Int chunkCoord)
        {
            ExplorationMapData data = explorationController.ExplorationData;
            TerrainMapFile map = mapLoader.LoadedMap;
            Vector2Int chunkOrigin = chunkCoord * RENDER_CHUNK_SIZE;
            List<Vector2Int> unexploredCells = new();

            for (int y = 0; y < RENDER_CHUNK_SIZE; y++)
                for (int x = 0; x < RENDER_CHUNK_SIZE; x++)
                {
                    Vector2Int cell = chunkOrigin + new Vector2Int(x, y);

                    if (data.IsInside(cell) == false) continue;
                    if (map.GetCellCenterWorld(cell).y > 0f) continue;
                    if (data.IsExplored(cell)) continue;

                    unexploredCells.Add(cell);
                }

            if (unexploredCells.Count == 0)
            {
                RemoveChunk(chunkCoord);

                return;
            }

            MeshFilter filter = GetOrCreateChunk(chunkCoord);
            Mesh previousMesh = filter.sharedMesh;
            filter.sharedMesh = BuildChunkMesh(
                unexploredCells,
                chunkOrigin);

            if (previousMesh != null)
                Destroy(previousMesh);
        }

        private void RemoveChunk(Vector2Int chunkCoord)
        {
            if (chunkViews.Remove(chunkCoord, out MeshFilter filter) == false)
                return;

            if (filter != null)
            {
                if (filter.sharedMesh != null)
                    Destroy(filter.sharedMesh);

                Destroy(filter.gameObject);
            }
        }

        private MeshFilter GetOrCreateChunk(Vector2Int chunkCoord)
        {
            if (chunkViews.TryGetValue(chunkCoord, out MeshFilter filter) && filter != null)
                return filter;

            GameObject view = new(
                $"FogChunk_{chunkCoord.x}_{chunkCoord.y}",
                typeof(MeshFilter),
                typeof(MeshRenderer));

            view.transform.SetParent(fogRoot, false);
            view.transform.localPosition = new Vector3(
                chunkCoord.x * RENDER_CHUNK_SIZE * cellSize,
                chunkCoord.y * RENDER_CHUNK_SIZE * cellSize,
                0f);

            MeshRenderer renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = fogMaterial;
            renderer.sortingOrder = sortingOrder;

            filter = view.GetComponent<MeshFilter>();
            chunkViews[chunkCoord] = filter;

            return filter;
        }

        private Mesh BuildChunkMesh(
            IReadOnlyList<Vector2Int> cells,
            Vector2Int chunkOrigin)
        {
            Vector3[] vertices = new Vector3[cells.Count * 4];
            Vector2[] uv = new Vector2[cells.Count * 4];
            Color[] colors = new Color[cells.Count * 4];
            int[] triangles = new int[cells.Count * 6];

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int local = cells[i] - chunkOrigin;
                float x = local.x * cellSize;
                float y = local.y * cellSize;
                int vertex = i * 4;
                int triangle = i * 6;

                vertices[vertex] = new Vector3(x, y);
                vertices[vertex + 1] = new Vector3(x, y + cellSize);
                vertices[vertex + 2] = new Vector3(x + cellSize, y + cellSize);
                vertices[vertex + 3] = new Vector3(x + cellSize, y);

                uv[vertex] = new Vector2(0f, 0f);
                uv[vertex + 1] = new Vector2(0f, 1f);
                uv[vertex + 2] = new Vector2(1f, 1f);
                uv[vertex + 3] = new Vector2(1f, 0f);

                colors[vertex] = fogColor;
                colors[vertex + 1] = fogColor;
                colors[vertex + 2] = fogColor;
                colors[vertex + 3] = fogColor;

                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            Mesh mesh = new()
            {
                name = "Exploration Fog Mesh"
            };

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        private void ClearChunks()
        {
            foreach (MeshFilter filter in chunkViews.Values)
            {
                if (filter == null) continue;

                if (filter.sharedMesh != null)
                    Destroy(filter.sharedMesh);

                Destroy(filter.gameObject);
            }

            chunkViews.Clear();
            dirtyChunks.Clear();
        }

        private void HandleExplorationRestored()
        {
            if (initialized == false) return;

            ClearChunks();
            BuildAllChunks(mapWidth, mapHeight);
        }
    }
}

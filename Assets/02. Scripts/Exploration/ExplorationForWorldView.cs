using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 미탐험 Grid 셀을 월드 암흑 오버레이로 표현

    public sealed class ExplorationForWorldView : MonoBehaviour
    {
        private const int RENDER_CHUNK_SIZE = 32;

        [Header("Sources")]
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private PlayerExplorationController explorationController;

        [Header("Visual Settings")]
        [SerializeField] private Color fogColor =
            new Color(0f, 0f, 0f, 0.88f);
        [SerializeField] private Color currentVisionFogColor =
            new Color(0f, 0f, 0f, 0.65f);
        [SerializeField] private int sortingOrder = 1;

        private readonly Dictionary<Vector2Int, MeshFilter> chunkViews = new();
        private readonly Dictionary<Vector2Int, MeshFilter> currentVisionChunkViews = new();
        private readonly HashSet<Vector2Int> dirtyChunks = new();
        private readonly HashSet<Vector2Int> dirtyCurrentVisionChunks = new();
        private readonly HashSet<Vector2Int> observedCells = new();

        private Transform fogRoot;
        private Transform currentVisionFogRoot;
        private Material fogMaterial;
        private Material currentVisionFogMaterial;
        private float cellSize;
        private Vector2 mapOrigin;
        private bool initialized;
        private int mapWidth;
        private int mapHeight;

        private void Awake()
        {
            roomClient ??= FindFirstObjectByType<RoomClient>();
            terrainManager ??= FindFirstObjectByType<TerrainManager>();
            explorationController ??= FindFirstObjectByType<PlayerExplorationController>();

            CreateFogMaterial();
        }

        private void OnEnable()
        {
            if (explorationController != null)
            {
                explorationController.CellsExplored += HandleCellsExplored;
                explorationController.CellsObserved += HandleCellsObserved;
                explorationController.ExplorationInitialized += HandleExplorationInitialized;
                explorationController.ExplorationRestored += HandleExplorationRestored;
            }
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded += HandleTerrainSnapshotLoaded;
            TryInitialize();
        }

        private void OnDisable()
        {
            if (explorationController != null)
            {
                explorationController.CellsExplored -= HandleCellsExplored;
                explorationController.CellsObserved -= HandleCellsObserved;
                explorationController.ExplorationInitialized -= HandleExplorationInitialized;
                explorationController.ExplorationRestored -= HandleExplorationRestored;
            }
            if (roomClient != null)
                roomClient.TerrainSnapshotLoaded -= HandleTerrainSnapshotLoaded;
        }

        private void CreateFogMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");

            fogMaterial = new Material(shader)
            {
                mainTexture = Texture2D.whiteTexture
            };
            currentVisionFogMaterial = new Material(shader)
            {
                mainTexture = Texture2D.whiteTexture
            };
        }

        private void OnDestroy()
        {
            ClearChunks();

            if (fogMaterial != null) Destroy(fogMaterial);
            if (currentVisionFogMaterial != null) Destroy(currentVisionFogMaterial);
        }

        private bool CanInitialize()
        {
            if (terrainManager == null || explorationController == null) return false;
            if (explorationController.ExplorationData == null) return false;

            return terrainManager.TryGetMapBounds(out _, out _, out _, out _);
        }

        private void InitializeFog()
        {
            if (terrainManager.TryGetMapBounds(
                    out mapWidth,
                    out mapHeight,
                    out cellSize,
                    out mapOrigin) == false)
                return;

            GameObject root = new("ExplorationFogRoot");
            fogRoot = root.transform;
            fogRoot.SetParent(transform, false);
            fogRoot.position = mapOrigin;

            GameObject currentVisionRoot = new("CurrentVisionFogRoot");
            currentVisionFogRoot = currentVisionRoot.transform;
            currentVisionFogRoot.SetParent(transform, false);
            currentVisionFogRoot.position = mapOrigin;

            observedCells.Clear();
            observedCells.UnionWith(explorationController.CurrentObservedCells);

            BuildAllChunks(mapWidth, mapHeight);
            initialized = true;
        }

        private void TryInitialize()
        {
            if (initialized == false && CanInitialize())
                InitializeFog();
        }

        private void HandleTerrainSnapshotLoaded(TerrainSnapshot snapshot)
        {
            if (snapshot != null)
                TryInitialize();
        }

        private void HandleExplorationInitialized()
        {
            TryInitialize();
        }

        private void BuildAllChunks(int mapWidth, int mapHeight)
        {
            int chunkWidth = Mathf.CeilToInt((float)mapWidth / RENDER_CHUNK_SIZE);
            int chunkHeight = Mathf.CeilToInt((float)mapHeight / RENDER_CHUNK_SIZE);

            for (int y = 0; y < chunkHeight; y++)
                for (int x = 0; x < chunkWidth; x++)
                {
                    Vector2Int chunkCoord = new(x, y);
                    RebuildChunk(chunkCoord);
                    RebuildCurrentVisionChunk(chunkCoord);
                }
        }

        private void HandleCellsExplored(
            IReadOnlyList<Vector2Int> cells)
        {
            if (initialized == false) return;

            foreach (Vector2Int cell in cells)
                dirtyChunks.Add(ToChunkCoord(cell));
        }

        private void HandleCellsObserved(IReadOnlyList<Vector2Int> cells)
        {
            if (initialized == false) return;

            foreach (Vector2Int cell in observedCells)
            {
                dirtyCurrentVisionChunks.Add(ToChunkCoord(cell));
                dirtyChunks.Add(ToChunkCoord(cell));
            }
            foreach (Vector2Int cell in cells)
            {
                dirtyCurrentVisionChunks.Add(ToChunkCoord(cell));
                dirtyChunks.Add(ToChunkCoord(cell));
            }

            observedCells.Clear();
            observedCells.UnionWith(cells);
        }

        private void LateUpdate()
        {
            if (initialized == false)
            {
                TryInitialize();
                return;
            }
            foreach (Vector2Int chunkCoord in dirtyChunks)
                RebuildChunk(chunkCoord);

            dirtyChunks.Clear();

            foreach (Vector2Int chunkCoord in dirtyCurrentVisionChunks)
                RebuildCurrentVisionChunk(chunkCoord);

            dirtyCurrentVisionChunks.Clear();
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
            Vector2Int chunkOrigin = chunkCoord * RENDER_CHUNK_SIZE;
            List<Vector2Int> unexploredCells = new();

            for (int y = 0; y < RENDER_CHUNK_SIZE; y++)
                for (int x = 0; x < RENDER_CHUNK_SIZE; x++)
                {
                    Vector2Int cell = chunkOrigin + new Vector2Int(x, y);

                    if (data.IsInside(cell) == false) continue;
                    if (mapOrigin.y + (cell.y + 0.5f) * cellSize > 0f) continue;
                    // 현재 시야에서는 탐험 기록과 관계없이 일시적으로 공개한다.
                    if (data.IsExplored(cell) || observedCells.Contains(cell)) continue;

                    unexploredCells.Add(cell);
                }

            if (unexploredCells.Count == 0)
            {
                RemoveChunk(chunkCoord);

                return;
            }

            MeshFilter filter = GetOrCreateChunk(chunkCoord);
            Mesh previousMesh = filter.sharedMesh;
            filter.sharedMesh = BuildChunkMesh(unexploredCells, chunkOrigin, fogColor);

            if (previousMesh != null)
                Destroy(previousMesh);
        }

        private void RebuildCurrentVisionChunk(Vector2Int chunkCoord)
        {
            Vector2Int chunkOrigin = chunkCoord * RENDER_CHUNK_SIZE;
            List<Vector2Int> hiddenCells = new();

            for (int y = 0; y < RENDER_CHUNK_SIZE; y++)
                for (int x = 0; x < RENDER_CHUNK_SIZE; x++)
                {
                    Vector2Int cell = chunkOrigin + new Vector2Int(x, y);

                    if (cell.x < 0 || cell.x >= mapWidth || cell.y < 0 || cell.y >= mapHeight)
                        continue;
                    if (mapOrigin.y + (cell.y + 0.5f) * cellSize > 0f) continue;
                    if (observedCells.Contains(cell)) continue;

                    hiddenCells.Add(cell);
                }

            if (hiddenCells.Count == 0)
            {
                RemoveCurrentVisionChunk(chunkCoord);
                return;
            }

            MeshFilter filter = GetOrCreateCurrentVisionChunk(chunkCoord);
            Mesh previousMesh = filter.sharedMesh;
            filter.sharedMesh = BuildChunkMesh(
                hiddenCells, chunkOrigin, currentVisionFogColor);

            if (previousMesh != null) Destroy(previousMesh);
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

        private void RemoveCurrentVisionChunk(Vector2Int chunkCoord)
        {
            if (currentVisionChunkViews.Remove(chunkCoord, out MeshFilter filter) == false)
                return;

            if (filter == null) return;
            if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(filter.gameObject);
        }

        private MeshFilter GetOrCreateCurrentVisionChunk(Vector2Int chunkCoord)
        {
            if (currentVisionChunkViews.TryGetValue(chunkCoord, out MeshFilter filter) && filter != null)
                return filter;

            GameObject view = new(
                $"CurrentVisionFogChunk_{chunkCoord.x}_{chunkCoord.y}",
                typeof(MeshFilter),
                typeof(MeshRenderer));

            view.transform.SetParent(currentVisionFogRoot, false);
            view.transform.localPosition = new Vector3(
                chunkCoord.x * RENDER_CHUNK_SIZE * cellSize,
                chunkCoord.y * RENDER_CHUNK_SIZE * cellSize,
                0f);

            MeshRenderer renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = currentVisionFogMaterial;
            renderer.sortingOrder = sortingOrder + 1;

            filter = view.GetComponent<MeshFilter>();
            currentVisionChunkViews[chunkCoord] = filter;
            return filter;
        }

        private Mesh BuildChunkMesh(
            IReadOnlyList<Vector2Int> cells,
            Vector2Int chunkOrigin,
            Color color)
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

                colors[vertex] = color;
                colors[vertex + 1] = color;
                colors[vertex + 2] = color;
                colors[vertex + 3] = color;

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

            foreach (MeshFilter filter in currentVisionChunkViews.Values)
            {
                if (filter == null) continue;
                if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
                Destroy(filter.gameObject);
            }

            currentVisionChunkViews.Clear();
            dirtyChunks.Clear();
            dirtyCurrentVisionChunks.Clear();
        }

        private void HandleExplorationRestored()
        {
            if (initialized == false) return;

            ClearChunks();
            observedCells.Clear();
            observedCells.UnionWith(explorationController.CurrentObservedCells);
            BuildAllChunks(mapWidth, mapHeight);
        }
    }
}

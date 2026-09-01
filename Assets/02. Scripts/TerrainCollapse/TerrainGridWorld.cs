using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using TMPro;
#endif

namespace TerrainCollapse
{
    // TerrainGridData의 Chunk Mesh와 경계 Collider 표현 및 변경 영역 갱신

    /// <summary>
    /// 커스텀 Grid 데이터를 Chunk Mesh와 외곽선 Collider로 표현한다.
    /// 셀마다 GameObject를 만들지 않으며 변경된 Chunk만 지연 갱신한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(TerrainResourceViewManager))]
    public sealed class TerrainGridWorld : MonoBehaviour
    {
        private const int RenderChunkSize = 32;

        [SerializeField] private float cellSize = 1f;
        [SerializeField] private Sprite terrainSprite;
        [SerializeField] private Color terrainColor = Color.white;
        [SerializeField] private TerrainTileCatalog tileCatalog;
        [SerializeField] private bool isDebugTileColorEnabled;
#if UNITY_EDITOR
        [SerializeField] private bool isDurabilityTextEnabled;
#endif
        
        private static readonly Vector2Int[] NoCells = new Vector2Int[0];
        private readonly Dictionary<Vector2Int, MeshFilter> chunkViews = new();
        private readonly HashSet<Vector2Int> dirtyChunks = new();
        private readonly List<GameObject> colliderSegments = new();
#if UNITY_EDITOR
        private readonly Dictionary<Vector2Int, TextMeshPro> durabilityLabels = new();
#endif
        private TerrainGridData data;
        private TerrainResourceViewManager resourceViewManager;
        private Material terrainMaterial;
        private bool colliderDirty;

        internal TerrainGridData Data => data;
        public float CellSize => cellSize;
        public Sprite TerrainSprite => terrainSprite;
        public Color TerrainColor => terrainColor;
        public uint Revision => data?.Revision ?? 0;

        public void ConfigureVisual(Sprite sprite, Color color, TerrainTileCatalog catalog = null)
        {
            terrainSprite = sprite;
            terrainColor = color;
            tileCatalog = catalog;
        }

        public Sprite GetTileSprite(TerrainTileType type) => tileCatalog?.Get(type)?.Sprite ?? terrainSprite;

        public Color GetTileColor(TerrainTileType type)
        {
            if (type == TerrainTileType.DeathLoot) return Color.red;

            TerrainTileDefinition definition = tileCatalog?.Get(type);
            
            if (definition != null) return definition.Color;
            if (isDebugTileColorEnabled) return GetDebugTileColor(type);
            
            return type == TerrainTileType.Bedrock ? Color.black : terrainColor;
        }

        private static Color GetDebugTileColor(TerrainTileType type)
            => type switch
            {
                TerrainTileType.Ground => new Color(0.35f, 0.75f, 0.3f),
                TerrainTileType.Dirt => new Color(0.55f, 0.3f, 0.12f),
                TerrainTileType.Stone => new Color(0.55f, 0.58f, 0.62f),
                TerrainTileType.Bedrock => new Color(0.05f, 0.05f, 0.05f),
                TerrainTileType.DeepStone => new Color(0.28f, 0.3f, 0.38f),
                TerrainTileType.DeathLoot => Color.red,
                _ => Color.magenta
            };

        public bool TileCreatesCollider(TerrainTileType type)
            => tileCatalog?.Get(type)?.CreatesCollider ?? true;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Start()
        {
            if (data != null) return;

            // 서버 권위 씬에서는 RoomClient가 비동기로 Snapshot을 수신하기 전까지 비어 있는 것이 정상이다.
            if (FindFirstObjectByType<RoomClient>() != null) return;
            
            Debug.LogError("[Terrain Collapse] 초기 JSON 또는 Snapshot이 로드되지 않았습니다.", this);
        }

        private void EnsureInitialized()
        {
            EnsureResourceViewManager();
            gameObject.layer = TerrainPhysicsLayers.Terrain;

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            
            if (terrainMaterial != null) return;
            
            if (terrainSprite == null)
            {
                Debug.LogError("[Terrain Collapse] TerrainGridWorld에 Terrain Sprite가 지정되지 않았습니다.", this);
                
                return;
            }
            
            Shader spriteShader = Shader.Find("Sprites/Default");
            terrainMaterial = new Material(spriteShader) { mainTexture = terrainSprite.texture };
        }

        private void EnsureResourceViewManager()
        {
            if (resourceViewManager != null) return;

            resourceViewManager = GetComponent<TerrainResourceViewManager>();
            if (resourceViewManager == null)
                resourceViewManager = gameObject.AddComponent<TerrainResourceViewManager>();
        }

        /// <summary>검증된 MapFile을 런타임 Grid로 변환하고 표현 계층을 생성한다.</summary>
        public void LoadMap(TerrainMapFile map)
        {
            EnsureInitialized();
            ClearGeneratedObjects();
            cellSize = map.CellSize;
            transform.position = map.Origin;
            data = new TerrainGridData(cellSize, map.Origin, map.Width, map.Height);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int coord = new(x, y);
                    TerrainTileType type = map.GetTile(coord);
                    
                    if (type != TerrainTileType.Empty)
                    {
                        int resourceID = map.GetResource(coord);
                        data.SetCell(
                            coord,
                            type,
                            GetMaxDurability(type, resourceID),
                            resourceID);
                    }
                }
            }

            BuildAllChunkMeshes();
            RebuildBoundaryCollider();
            resourceViewManager.RebuildAll(this);
#if UNITY_EDITOR
            RebuildDurabilityLabels();
#endif
            Physics2D.SyncTransforms();
        }

        /// <summary>중간 참여자가 받은 런타임 Snapshot으로 월드를 교체한다.</summary>
        public void LoadSnapshot(TerrainSnapshot snapshot)
        {
            EnsureInitialized();
            ClearGeneratedObjects();
            data = TerrainGridData.FromSnapshot(snapshot);
            cellSize = data.CellSize;
            transform.position = data.Origin;
            BuildAllChunkMeshes();
            RebuildBoundaryCollider();
            resourceViewManager.RebuildAll(this);
#if UNITY_EDITOR
            RebuildDurabilityLabels();
#endif
            Physics2D.SyncTransforms();
        }

        public TerrainSnapshot CreateSnapshot() => data?.CreateSnapshot();

        /// <summary>서버 확정 변경만 이 API로 일괄 반영한다.</summary>
        public bool ApplyAuthoritativeBatch(TerrainChangeBatch batch, out string error)
        {
            if (data == null)
            {
                error = "TerrainGridData가 초기화되지 않았습니다."; 
                
                return false;
            }
            
            if (data.TryApplyAuthoritativeBatch(batch, out error) == false) return false;
            
            foreach (TerrainCellChange change in batch.Changes)
            {
                MarkCellDirty(change.Coord);
                resourceViewManager.RefreshCell(this, change.Coord);
#if UNITY_EDITOR
                RefreshDurabilityLabel(change.Coord);
#endif
            }
            
            return true;
        }

        private void ClearGeneratedObjects()
        {
            resourceViewManager?.Clear();

            foreach (MeshFilter filter in chunkViews.Values)
            {
                if (filter == null) continue;
                if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
                
                Destroy(filter.gameObject);
            }
            
            chunkViews.Clear();
            dirtyChunks.Clear();

            foreach (GameObject segment in colliderSegments)
            {
                if (segment == null) continue;
                
                segment.GetComponent<EdgeCollider2D>().enabled = false;
                Destroy(segment);
            }
            
            colliderSegments.Clear();
            colliderDirty = false;
#if UNITY_EDITOR
            ClearDurabilityLabels();
#endif
        }

        private void LateUpdate()
        {
            if (dirtyChunks.Count > 0)
            {
                foreach (Vector2Int chunkCoord in dirtyChunks) RebuildChunkMesh(chunkCoord);
                
                dirtyChunks.Clear();
            }

            if (colliderDirty == false) return;
            
            RebuildBoundaryCollider();
            colliderDirty = false;
        }

        private void OnDestroy()
        {
            foreach (MeshFilter filter in chunkViews.Values)
                if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            
            if (terrainMaterial != null) Destroy(terrainMaterial);
        }

        public bool IsOccupied(Vector2Int coord) => data != null && data.IsOccupied(coord);
        public IEnumerable<Vector2Int> GetOccupiedCells()
            => data != null ? data.OccupiedCoords : NoCells;
        public Vector2Int WorldToCell(Vector3 world) => data.WorldToCell(world);
        public Vector3 CellToWorldCenter(Vector2Int coord) => data.CellToWorldCenter(coord);
        public Vector3 CellToWorldCorner(Vector2Int coord) => data.CellToWorldCorner(coord);

        public bool SetOccupied(Vector2Int coord) => SetCell(coord, TerrainTileType.Ground);

        public bool SetCell(Vector2Int coord, TerrainTileType type)
            => SetCell(coord, type, GetMaxDurability(type));

        public bool SetCell(
            Vector2Int coord,
            TerrainTileType type,
            int durability,
            int resourceID = 0,
            IReadOnlyList<TerrainCellLootEntry> lootEntries = null)
        {
            if (data == null) return false;
            if (data.SetCell(
                    coord,
                    type,
                    durability,
                    resourceID,
                    lootEntries) == false) return false;
            
            MarkCellDirty(coord);
            resourceViewManager.RefreshCell(this, coord);
#if UNITY_EDITOR
            RefreshDurabilityLabel(coord);
#endif
            
            return true;
        }

        public bool TryDamageCell(
            Vector2Int coord,
            int amount,
            out int remainingDurability,
            out bool isDestroyed)
        {
            remainingDurability = 0;
            isDestroyed = false;
            
            if (data == null || data.TryDamage(
                    coord,
                    amount,
                    out remainingDurability,
                    out isDestroyed) == false)
                return false;

            if (isDestroyed)
            {
                MarkCellDirty(coord);
                resourceViewManager.RefreshCell(this, coord);
            }
            
#if UNITY_EDITOR
            RefreshDurabilityLabel(coord);
#endif
            
            return true;
        }

        public bool RemoveCell(Vector2Int coord)
        {
            if (data == null) return false;
            if (data.Remove(coord) == false) return false;
            
            MarkCellDirty(coord);
            resourceViewManager.RefreshCell(this, coord);
#if UNITY_EDITOR
            RefreshDurabilityLabel(coord);
#endif
            
            return true;
        }

        private static int GetMaxDurability(TerrainTileType type, int resourceID = 0)
        {
            if (type == TerrainTileType.Empty) return 0;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            if (resourceID > 0 &&
                dataManager.Resources.TryGet(resourceID, out TerrainResourceDef resourceDef))
                return resourceDef.MaxDurability;
            
            return dataManager.TryGetTileDef(type, out TerrainTileDef tileDef)
                ? tileDef.MaxDurability
                : 1;
        }

#if UNITY_EDITOR
        private void RebuildDurabilityLabels()
        {
            ClearDurabilityLabels();
            
            if (isDurabilityTextEnabled == false || data == null) return;

            foreach (Vector2Int coord in data.OccupiedCoords)
                RefreshDurabilityLabel(coord);
        }

        private void RefreshDurabilityLabel(Vector2Int coord)
        {
            if (isDurabilityTextEnabled == false) return;

            TerrainCell cell = data?.GetCellOrNull(coord);
            
            if (cell == null)
            {
                if (durabilityLabels.Remove(coord, out TextMeshPro removedLabel) && removedLabel != null)
                    Destroy(removedLabel.gameObject);
                
                return;
                
            }

            if (durabilityLabels.TryGetValue(coord, out TextMeshPro label) == false || label == null)
            {
                GameObject labelObject = new($"Durability_{coord.x}_{coord.y}", typeof(TextMeshPro));
                labelObject.transform.SetParent(transform, false);
                label = labelObject.GetComponent<TextMeshPro>();
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.fontSizeMin = 0.1f;
                label.fontSizeMax = 4f;
                label.color = Color.white;
                label.renderer.sortingOrder = 3;
                durabilityLabels[coord] = label;
            }

            label.text = cell.CurrentDurability.ToString();
            label.rectTransform.sizeDelta = Vector2.one * (cellSize * 0.85f);
            label.transform.localPosition = new Vector3(
                (coord.x + 0.5f) * cellSize,
                (coord.y + 0.5f) * cellSize,
                -0.1f);
        }

        private void ClearDurabilityLabels()
        {
            foreach (TextMeshPro label in durabilityLabels.Values)
                if (label != null) Destroy(label.gameObject);

            durabilityLabels.Clear();
        }
#endif

        private void MarkCellDirty(Vector2Int coord)
        {
            dirtyChunks.Add(ToChunkCoord(coord));
            colliderDirty = true;
        }

        private void BuildAllChunkMeshes()
        {
            HashSet<Vector2Int> chunks = new();
            
            foreach (Vector2Int coord in data.OccupiedCoords) chunks.Add(ToChunkCoord(coord));
            foreach (Vector2Int chunkCoord in chunks) RebuildChunkMesh(chunkCoord);
        }

        private void RebuildChunkMesh(Vector2Int chunkCoord)
        {
            List<Vector2Int> cells = new();
            int minX = chunkCoord.x * RenderChunkSize;
            int minY = chunkCoord.y * RenderChunkSize;

            for (int y = minY; y < minY + RenderChunkSize; y++)
            {
                for (int x = minX; x < minX + RenderChunkSize; x++)
                {
                    Vector2Int coord = new(x, y);
                    
                    if (data.IsOccupied(coord)) cells.Add(coord);
                }
            }

            if (cells.Count == 0)
            {
                if (chunkViews.Remove(chunkCoord, out MeshFilter emptyFilter) && emptyFilter != null)
                {
                    if (emptyFilter.sharedMesh != null) Destroy(emptyFilter.sharedMesh);
                    
                    Destroy(emptyFilter.gameObject);
                }
                
                return;
            }

            if (chunkViews.TryGetValue(chunkCoord, out MeshFilter filter) == false || filter == null)
            {
                GameObject view = new($"RenderChunk_{chunkCoord.x}_{chunkCoord.y}",
                    typeof(MeshFilter), typeof(MeshRenderer));
                view.transform.SetParent(transform, false);
                
                filter = view.GetComponent<MeshFilter>();
                view.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
                chunkViews[chunkCoord] = filter;
            }

            Mesh previous = filter.sharedMesh;
            filter.sharedMesh = BuildMesh(cells, chunkCoord);
            
            if (previous != null) Destroy(previous);
        }

        private Mesh BuildMesh(IReadOnlyList<Vector2Int> cells, Vector2Int chunkCoord)
        {
            Vector3[] vertices = new Vector3[cells.Count * 4];
            Vector2[] uv = new Vector2[cells.Count * 4];
            Color[] colors = new Color[cells.Count * 4];
            int[] triangles = new int[cells.Count * 6];
            Vector2Int chunkOrigin = chunkCoord * RenderChunkSize;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int local = cells[i] - chunkOrigin;
                TerrainCell cell = data.GetCellOrNull(cells[i]);
                Sprite sprite = GetTileSprite(cell.Type);
                Color color = GetTileColor(cell.Type);
                Rect textureRect = sprite.textureRect;
                float uMin = textureRect.xMin / sprite.texture.width;
                float uMax = textureRect.xMax / sprite.texture.width;
                float vMin = textureRect.yMin / sprite.texture.height;
                float vMax = textureRect.yMax / sprite.texture.height;
                float x = local.x * cellSize;
                float y = local.y * cellSize;
                int vertex = i * 4;
                vertices[vertex] = new Vector3(x, y);
                vertices[vertex + 1] = new Vector3(x, y + cellSize);
                vertices[vertex + 2] = new Vector3(x + cellSize, y + cellSize);
                vertices[vertex + 3] = new Vector3(x + cellSize, y);
                uv[vertex] = new Vector2(uMin, vMin);
                uv[vertex + 1] = new Vector2(uMin, vMax);
                uv[vertex + 2] = new Vector2(uMax, vMax);
                uv[vertex + 3] = new Vector2(uMax, vMin);
                colors[vertex] = colors[vertex + 1] = colors[vertex + 2] = colors[vertex + 3] = color;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            Mesh mesh = new Mesh { name = $"TerrainChunk_{chunkCoord.x}_{chunkCoord.y}" };
            
            if (vertices.Length > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            MeshFilter filter = chunkViews.TryGetValue(chunkCoord, out MeshFilter existing) ? existing : null;
            
            if (filter != null) filter.transform.localPosition = new Vector3(
                chunkCoord.x * RenderChunkSize * cellSize,
                chunkCoord.y * RenderChunkSize * cellSize, 0f);
                return mesh;
        }

        /// <summary>점유 셀과 빈 셀 사이의 경계만 추출하고, 같은 선상의 연속 구간을 하나로 합친다.</summary>
        private void RebuildBoundaryCollider()
        {
            foreach (GameObject segment in colliderSegments)
            {
                if (segment == null) continue;
                
                segment.GetComponent<EdgeCollider2D>().enabled = false;
                Destroy(segment);
            }
            
            colliderSegments.Clear();

            Dictionary<int, SortedSet<int>> horizontal = new();
            Dictionary<int, SortedSet<int>> vertical = new();
            
            foreach (Vector2Int cell in data.OccupiedCoords)
            {
                TerrainTileType type = data.GetCellOrNull(cell).Type;
                
                if (TileCreatesCollider(type) == false) continue;
                if (HasCollidableCell(cell + Vector2Int.down) == false) AddEdge(horizontal, cell.y, cell.x);
                if (HasCollidableCell(cell + Vector2Int.up) == false) AddEdge(horizontal, cell.y + 1, cell.x);
                if (HasCollidableCell(cell + Vector2Int.left) == false) AddEdge(vertical, cell.x, cell.y);
                if (HasCollidableCell(cell + Vector2Int.right) == false) AddEdge(vertical, cell.x + 1, cell.y);
            }

            foreach ((int y, SortedSet<int> starts) in horizontal)
                CreateMergedSegments(starts, true, y);
            
            foreach ((int x, SortedSet<int> starts) in vertical)
                CreateMergedSegments(starts, false, x);
        }

        private bool HasCollidableCell(Vector2Int coord)
        {
            TerrainCell cell = data.GetCellOrNull(coord);
            
            return cell != null && TileCreatesCollider(cell.Type);
        }

        private static void AddEdge(Dictionary<int, SortedSet<int>> lines, int line, int start)
        {
            if (lines.TryGetValue(line, out SortedSet<int> starts) == false)
            {
                starts = new SortedSet<int>();
                lines.Add(line, starts);
            }
            
            starts.Add(start);
        }

        private void CreateMergedSegments(SortedSet<int> starts, bool horizontal, int fixedAxis)
        {
            int runStart = 0;
            int previous = 0;
            bool hasRun = false;
            
            foreach (int value in starts)
            {
                if (hasRun == false)
                {
                    runStart = previous = value;
                    hasRun = true;
                    continue;
                }

                if (value == previous + 1)
                {
                    previous = value; 
                    continue;
                }
                
                CreateColliderSegment(horizontal, fixedAxis, runStart, previous + 1);
                runStart = previous = value;
            }
            
            if (hasRun) CreateColliderSegment(horizontal, fixedAxis, runStart, previous + 1);
        }

        private void CreateColliderSegment(bool horizontal, int fixedAxis, int start, int end)
        {
            GameObject segment = new("Boundary", typeof(EdgeCollider2D));
            segment.layer = TerrainPhysicsLayers.Terrain;
            segment.transform.SetParent(transform, false);
            
            EdgeCollider2D edge = segment.GetComponent<EdgeCollider2D>();
            edge.points = horizontal
                ? new[] { new Vector2(start * cellSize, fixedAxis * cellSize), new Vector2(end * cellSize, fixedAxis * cellSize) }
                : new[] { new Vector2(fixedAxis * cellSize, start * cellSize), new Vector2(fixedAxis * cellSize, end * cellSize) };
            
            colliderSegments.Add(segment);
        }

        private static Vector2Int ToChunkCoord(Vector2Int cell)
            => new(Mathf.FloorToInt((float)cell.x / RenderChunkSize),
                Mathf.FloorToInt((float)cell.y / RenderChunkSize));
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 부유 지형의 Rigidbody2D Chunk 전환과 안정화 후 서버 확정용 배치 후보 생성
    
    /// <summary>Custom Grid → 물리 Chunk → Grid Sampling → Custom Grid 전환 총괄</summary>
    public sealed class CollapseManager : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld terrainGridWorld;
        [SerializeField] private TerrainSampler terrainSampler;
        [SerializeField] private float gravityScale = 2f;
        [SerializeField] private float chunkMass = 100f;
        [SerializeField] private float initialSeparationDistance = 0.05f;

        private readonly List<FallingChunk> activeChunks = new();
        private bool rebuilding;
        private long nextCollapseId = 1;

        public bool IsRebuilding => rebuilding;
        public event Action RebuildCompleted;
        public event Action<TerrainPlacementProposal> PlacementProposed;

        public void Configure(TerrainGridWorld world, TerrainSampler sampler)
        {
            terrainGridWorld = world;
            terrainSampler = sampler;
        }

        public void CreateChunk(IReadOnlyList<Vector2Int> cells)
        {
            CreateChunk(cells, nextCollapseId++);
        }

        public void CreateChunk(IReadOnlyList<Vector2Int> cells, long collapseId)
        {
            if (cells == null || cells.Count == 0) return;
            TerrainGridWorld source = terrainGridWorld;
            Vector2Int anchor = cells[0];

            GameObject root = new($"FallingChunk_{activeChunks.Count + 1}",
                typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(FallingChunk));
            // 최초 꼭짓점 접촉이 Box2D 제약으로 남지 않도록 물리 생성 위치를 소폭 아래로 분리
            root.transform.position = source.CellToWorldCorner(anchor) + Vector3.down * initialSeparationDistance;

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = gravityScale;
            body.mass = chunkMass;
            body.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CompositeCollider2D composite = root.GetComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            List<Vector2Int> localCells = new (cells.Count);
            List<TerrainTileType> cellTypes = new (cells.Count);
            
            foreach (Vector2Int worldCell in cells)
            {
                Vector2Int localCell = worldCell - anchor;
                TerrainTileType type = source.Data.GetCellOrNull(worldCell).Type;
                localCells.Add(localCell);
                cellTypes.Add(type);
                CreateChunkCell(root.transform, localCell, type, source);
                source.RemoveCell(worldCell);
            }

            Physics2D.SyncTransforms();
            FallingChunk chunk = root.GetComponent<FallingChunk>();
            chunk.Configure(localCells, cellTypes, source.CellSize, collapseId);
            
            activeChunks.Add(chunk);
            Debug.Log($"[Terrain Collapse] Chunk Created: {cells.Count} cells");
        }

        private static void CreateChunkCell(
            Transform parent,
            Vector2Int localCell,
            TerrainTileType type,
            TerrainGridWorld source)
        {
            GameObject cell = new($"Cell_{localCell.x}_{localCell.y}",
                typeof(SpriteRenderer), typeof(BoxCollider2D));
            cell.transform.SetParent(parent, false);
            cell.transform.localPosition = new Vector3((localCell.x + 0.5f) * source.CellSize,
                (localCell.y + 0.5f) * source.CellSize, 0f);
            
            SpriteRenderer renderer = cell.GetComponent<SpriteRenderer>();
            renderer.sprite = source.GetTileSprite(type);
            renderer.color = source.GetTileColor(type);
            renderer.sortingOrder = 1;
            
            BoxCollider2D collider = cell.GetComponent<BoxCollider2D>();
            collider.size = Vector2.one * source.CellSize;
            collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        private void Update()
        {
            if (rebuilding == false
                && activeChunks.Count > 0
                && activeChunks.TrueForAll(chunk => chunk.IsSettled))
                StartCoroutine(RebuildCycle());
        }

        private IEnumerator RebuildCycle()
        {
            rebuilding = true;
            Debug.Log("[Terrain Collapse] Sampling Started");

            IReadOnlyList<TerrainPlacementProposal> proposals = terrainSampler.CalculatePlacements(activeChunks);
            foreach (TerrainPlacementProposal proposal in proposals) PlacementProposed?.Invoke(proposal);
            Debug.Log("[Terrain Collapse] Sampling Finished");

            foreach (FallingChunk chunk in activeChunks)
                Destroy(chunk.gameObject);

            activeChunks.Clear();

            yield return new WaitForFixedUpdate();

            Physics2D.SyncTransforms();
            Debug.Log("[Terrain Collapse] Custom Grid Rebuilt");

            rebuilding = false;
            RebuildCompleted?.Invoke();
        }
    }
}

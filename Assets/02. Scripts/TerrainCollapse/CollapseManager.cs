using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 부유 지형의 개별 Rigidbody2D Chunk 전환과 완료 즉시 Grid 재배치 처리
    
    /// <summary>Custom Grid → 물리 Chunk → Grid Sampling → Custom Grid 전환 총괄</summary>
    public sealed class CollapseManager : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld terrainGridWorld;
        [SerializeField] private TerrainSampler terrainSampler;
        [SerializeField] private float gravityScale = 2f;
        [SerializeField] private float chunkMass = 100f;
        [SerializeField] private float initialSeparationDistance = 0.05f;
        [SerializeField, Range(0f, 0.25f)] private float colliderHorizontalInsetRatio = 0.01f;
        [SerializeField] private float outOfBoundsDistance = 20f;

        private readonly List<FallingChunk> activeChunks = new();
        private readonly HashSet<FallingChunk> rebuildingChunks = new();
        private long nextCollapseId = 1;

        public bool IsRebuilding => rebuildingChunks.Count > 0;
        public bool HasActiveChunks => activeChunks.Count > 0;
        public event Action<long> RebuildCompleted;
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

            GameObject root = new($"FallingChunk_{collapseId}",
                typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(FallingChunk));
            root.transform.position = source.CellToWorldCorner(anchor) + Vector3.down * initialSeparationDistance;

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = gravityScale;
            body.mass = chunkMass;
            body.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CompositeCollider2D composite = root.GetComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            List<Vector2Int> localCells = new(cells.Count);
            List<TerrainTileType> cellTypes = new(cells.Count);
            List<int> cellDurabilities = new(cells.Count);
            
            foreach (Vector2Int worldCell in cells)
            {
                Vector2Int localCell = worldCell - anchor;
                TerrainCell sourceCell = source.Data.GetCellOrNull(worldCell);
                TerrainTileType type = sourceCell.Type;
                localCells.Add(localCell);
                cellTypes.Add(type);
                cellDurabilities.Add(sourceCell.CurrentDurability);
                CreateChunkCell(root.transform, localCell, type, source);
                source.RemoveCell(worldCell);
            }

            Physics2D.SyncTransforms();
            FallingChunk chunk = root.GetComponent<FallingChunk>();
            float minimumWorldY = source.transform.position.y - outOfBoundsDistance;
            chunk.Configure(
                localCells,
                cellTypes,
                cellDurabilities,
                source.CellSize,
                collapseId,
                minimumWorldY);
            activeChunks.Add(chunk);
            
            Debug.Log($"[Terrain Collapse] Chunk Created: id={collapseId}, cells={cells.Count}");
        }

        private void CreateChunkCell(
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
            float horizontalInset = source.CellSize * colliderHorizontalInsetRatio;
            collider.size = new Vector2(
                Mathf.Max(0.001f, source.CellSize - horizontalInset * 2f),
                source.CellSize);
            collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        private void Update()
        {
            for (int i = activeChunks.Count - 1; i >= 0; i--)
            {
                FallingChunk chunk = activeChunks[i];
                if (chunk == null)
                {
                    activeChunks.RemoveAt(i);
                    continue;
                }

                if (chunk.IsComplete && rebuildingChunks.Add(chunk))
                    StartCoroutine(RebuildChunk(chunk));
            }
        }

        private IEnumerator RebuildChunk(FallingChunk chunk)
        {
            long collapseId = chunk.CollapseId;
            Debug.Log($"[Terrain Collapse] Chunk Sampling Started: id={collapseId}");

            if (chunk.IsExpired == false)
            {
                IReadOnlyList<TerrainPlacementProposal> proposals =
                    terrainSampler.CalculatePlacements(new[] { chunk });
                
                foreach (TerrainPlacementProposal proposal in proposals)
                    PlacementProposed?.Invoke(proposal);
            }
            else Debug.LogWarning($"[Terrain Collapse] Expired Chunk discarded: id={collapseId}");

            activeChunks.Remove(chunk);
            rebuildingChunks.Remove(chunk);
            Destroy(chunk.gameObject);

            yield return new WaitForFixedUpdate();

            Physics2D.SyncTransforms();
            Debug.Log($"[Terrain Collapse] Chunk Grid Rebuilt: id={collapseId}");
            RebuildCompleted?.Invoke(collapseId);
        }
    }
}

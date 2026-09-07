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

        public bool HasActiveChunks => activeChunks.Count > 0;
        public event Action<long> RebuildCompleted;
        public event Action<TerrainPlacementProposal> PlacementProposed;

        public void ClearActiveChunks()
        {
            foreach (FallingChunk chunk in activeChunks)
                if (chunk != null) Destroy(chunk.gameObject);

            activeChunks.Clear();
            rebuildingChunks.Clear();
        }

        // 이거 왜 있는지 쌤도 모르겠음. 클로드가 맹글었음
        /// <summary>서버가 무른 낙하를 되돌린다. 원래 칸을 되살리고 Chunk 를 지운다.</summary>
        public void CancelChunk(long collapseId)
        {
            for (int i = activeChunks.Count - 1; i >= 0; i--)
            {
                FallingChunk chunk = activeChunks[i];

                if (chunk == null)
                {
                    activeChunks.RemoveAt(i);

                    continue;
                }

                if (chunk.CollapseId != collapseId) continue;

                RestoreSourceCells(chunk);
                activeChunks.RemoveAt(i);
                rebuildingChunks.Remove(chunk);
                Destroy(chunk.gameObject);
                Debug.Log($"[Terrain Collapse] Chunk Cancelled: id={collapseId}");

                return;
            }
        }

        /// <summary>서버가 알려 준 칸을 Grid 에 그대로 되돌린다.</summary>
        public void RestoreCells(IEnumerable<TerrainCellChange> cells)
        {
            if (terrainGridWorld == null || cells == null) return;

            foreach (TerrainCellChange cell in cells)
                terrainGridWorld.SetCell(
                    cell.Coord,
                    cell.Type,
                    cell.Durability,
                    cell.ResourceID);
        }

        // 떨어지기 시작할 때 Grid 에서 뺐던 칸을 그대로 되돌린다.
        // 서버는 이 칸을 지운 적이 없다. 지운 것은 각자의 화면뿐이다.
        private void RestoreSourceCells(FallingChunk chunk)
        {
            if (terrainGridWorld == null) return;

            IReadOnlyList<Vector2Int> cells = chunk.SourceCells;
            IReadOnlyList<TerrainTileType> types = chunk.CellTypes;
            IReadOnlyList<int> durabilities = chunk.CellDurabilities;
            IReadOnlyList<int> resourceIDs = chunk.CellResourceIDs;

            for (int i = 0; i < cells.Count; i++)
                terrainGridWorld.SetCell(cells[i], types[i], durabilities[i], resourceIDs[i]);
        }

        public void Configure(TerrainGridWorld world, TerrainSampler sampler)
        {
            terrainGridWorld = world;
            terrainSampler = sampler;
        }

        public void CreateChunk(IReadOnlyList<Vector2Int> cells)
        {
            CreateChunk(cells, nextCollapseId++, true);
        }

        public void CreateChunk(IReadOnlyList<Vector2Int> cells, bool isPlacementOwner)
        {
            CreateChunk(cells, nextCollapseId++, isPlacementOwner);
        }

        public void CreateChunk(
            IReadOnlyList<Vector2Int> cells,
            long collapseId,
            bool isPlacementOwner)
        {
            if (cells == null || cells.Count == 0) return;
            
            TerrainGridWorld source = terrainGridWorld;
            if (source?.Data == null)
            {
                Debug.LogError("[Terrain Collapse] TerrainGridWorld가 준비되지 않아 Chunk를 만들 수 없습니다.", this);
                return;
            }

            List<TerrainCell> sourceCells = new(cells.Count);
            foreach (Vector2Int worldCell in cells)
            {
                TerrainCell sourceCell = source.Data.GetCellOrNull(worldCell);
                if (sourceCell == null || sourceCell.Type == TerrainTileType.Empty)
                {
                    Debug.LogWarning(
                        $"[Terrain Collapse] 원본 셀이 없어 Chunk 생성을 건너뜁니다: " +
                        $"id={collapseId}, cell={worldCell}",
                        this);
                    return;
                }

                sourceCells.Add(sourceCell);
            }

            Vector2Int anchor = cells[0];

            GameObject root = new($"FallingChunk_{collapseId}",
                typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(FallingChunk));
            root.layer = TerrainPhysicsLayers.Terrain;
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
            List<int> cellResourceIDs = new(cells.Count);
            
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int worldCell = cells[i];
                Vector2Int localCell = worldCell - anchor;
                TerrainCell sourceCell = sourceCells[i];
                TerrainTileType type = sourceCell.Type;
                localCells.Add(localCell);
                cellTypes.Add(type);
                cellDurabilities.Add(sourceCell.CurrentDurability);
                cellResourceIDs.Add(sourceCell.ResourceID);
                CreateChunkCell(root.transform, localCell, type, sourceCell.ResourceID, source);
                source.RemoveCell(worldCell);
            }

            Physics2D.SyncTransforms();
            FallingChunk chunk = root.GetComponent<FallingChunk>();
            float minimumWorldY = source.transform.position.y - outOfBoundsDistance;
            chunk.Configure(
                cells,
                localCells,
                cellTypes,
                cellDurabilities,
                cellResourceIDs,
                source.CellSize,
                collapseId,
                minimumWorldY,
                isPlacementOwner);
            activeChunks.Add(chunk);
            
            Debug.Log($"[Terrain Collapse] Chunk Created: id={collapseId}, cells={cells.Count}, " +
                      $"placementOwner={isPlacementOwner}");
        }

        private void CreateChunkCell(
            Transform parent,
            Vector2Int localCell,
            TerrainTileType type,
            int resourceID,
            TerrainGridWorld source)
        {
            GameObject cell = new($"Cell_{localCell.x}_{localCell.y}",
                typeof(SpriteRenderer), typeof(BoxCollider2D));
            cell.layer = TerrainPhysicsLayers.Terrain;
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

            if (resourceID > 0)
            {
                TerrainResourceViewFactory.Create(
                    resourceID,
                    cell.transform,
                    new Vector3(0f, 0f, -0.1f));
            }
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

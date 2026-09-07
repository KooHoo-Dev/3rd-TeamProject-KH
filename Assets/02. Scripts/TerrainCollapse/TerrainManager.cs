using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace TerrainCollapse
{
    // 지형 변경 진입점과 부유 그룹 탐색 및 서버 확정 Batch 적용 API 제공

    public enum TerrainRemoveResult
    {
        Removed,
        TerrainNotReady,
        EmptyCell,
        NotMineable,
        Damaged,
    }

    public enum TerrainChangeReason
    {
        Excavation,
        AuthoritativeBatch,
        CollapsePlacement,
        DeathLootPlacement,
    }
    
    /// <summary>
    /// 커스텀 TerrainGridData를 지형의 Source of Truth로 관리한다.
    /// 타일 제거 후 4방향 연결 그룹을 찾고 기반과 끊긴 그룹을 물리 Chunk로 넘긴다.
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld terrainWorld;
#if UNITY_EDITOR
        [SerializeField] private Camera inputCamera;
#endif
        [SerializeField] private bool logEvents = true;

        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        public bool IsReady => terrainWorld != null && terrainWorld.Data != null;
        public float CellSize => terrainWorld != null ? terrainWorld.CellSize : 0f;
        public uint Revision => terrainWorld != null ? terrainWorld.Revision : 0;
        public event Action<TerrainChangeReason> TerrainChanged;
        public event Action<Vector2Int, int> TerrainDurabilityChanged;
        public event Action<TerrainChangeBatch> AuthoritativeBatchApplied;
        public event Action<IReadOnlyList<long>> CollapseCancelled;
        public event Action<TerrainCellDestroyedInfo> CellDestroyed;

        /// <summary>서버가 무른 낙하 번호를 한 번에 알린다. 지형은 CollapseManager가 이미 되돌린 뒤다.</summary>
        public void NotifyCollapseCancelled(IReadOnlyList<long> collapseIds)
            => CollapseCancelled?.Invoke(collapseIds);

        /// <summary>현재 로드된 지형의 Grid 범위와 월드 변환 기준을 반환한다.</summary>
        public bool TryGetMapBounds(
            out int mapWidth,
            out int mapHeight,
            out float cellSize,
            out Vector2 origin)
        {
            mapWidth = 0;
            mapHeight = 0;
            cellSize = 0f;
            origin = default;

            if (IsReady == false) return false;

            TerrainGridData data = terrainWorld.Data;
            mapWidth = data.MapWidth;
            mapHeight = data.MapHeight;
            cellSize = data.CellSize;
            origin = data.Origin;
            return mapWidth > 0 && mapHeight > 0 && cellSize > 0f;
        }

        private void Awake()
        {
            if (TryGetComponent<TerrainDropSpawner>(out _) == false)
                gameObject.AddComponent<TerrainDropSpawner>();
        }

        public void Configure(TerrainGridWorld world, Camera camera)
        {
            terrainWorld = world;
#if UNITY_EDITOR
            inputCamera = camera;
#endif
        }
        
        // 지정 Grid 좌표의 지형 존재 여부 조회
        public bool HasTerrain(Vector2Int cell) => IsReady && terrainWorld.IsOccupied(cell);

        /// <summary>지정 Grid 좌표의 타일 종류 조회 시도</summary>
        public bool TryGetTileType(Vector2Int cell, out TerrainTileType type)
        {
            type = TerrainTileType.Empty;
            
            if (IsReady == false) return false;

            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);
            
            if (terrainCell == null) return false;

            type = terrainCell.Type;
            
            return true;
        }

        /// <summary>살아 있는 셀에 클라이언트 타격 피드백만 요청한다.</summary>
        public void PlayTileHit(Vector2Int cell)
        {
            if (IsReady) terrainWorld.PlayCellHit(cell);
        }

        public bool TryGetDurability(Vector2Int cell, out int durability)
        {
            durability = 0;
            
            if (IsReady == false) return false;

            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);
            
            if (terrainCell == null) return false;

            durability = terrainCell.CurrentDurability;
            
            return true;
        }

        /// <summary>지정 Grid 좌표에 부착된 자원 ID 조회 시도</summary>
        public bool TryGetResourceID(Vector2Int cell, out int resourceID)
        {
            resourceID = 0;

            if (IsReady == false) return false;

            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);

            if (terrainCell == null) return false;

            resourceID = terrainCell.ResourceID;

            return true;
        }

        /// <summary>월드 좌표의 Grid 좌표 변환 시도</summary>
        public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
        {
            cell = default;
            
            if (IsReady == false) return false;

            cell = terrainWorld.WorldToCell(worldPosition);
            
            return true;
        }

        /// <summary>Grid 좌표의 월드 중심 위치 변환 시도</summary>
        public bool TryCellToWorldCenter(Vector2Int cell, out Vector3 worldPosition)
        {
            worldPosition = default;

            if (IsReady == false) return false;

            worldPosition = terrainWorld.CellToWorldCenter(cell);

            return true;
        }

        public bool IsCellMineable(Vector2Int cell)
        {
            if (TryGetTileType(cell, out TerrainTileType type) == false) return false;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            return dataManager.TryGetTileDef(type, out TerrainTileDef tileDef) && tileDef.IsMineable;
        }

        public bool CanCreateDeathLootCell(Vector2Int cell)
        {
            if (IsReady == false) return false;
            if (TryGetTileType(cell, out TerrainTileType type) &&
                (type == TerrainTileType.Bedrock ||
                 type == TerrainTileType.SpawnPlatform ||
                 type == TerrainTileType.DeathLoot))
                return false;

            return true;
        }

        public bool TryCreateDeathLootCell(
            Vector2Int cell,
            IReadOnlyList<TerrainCellLootEntry> lootEntries)
        {
            if (CanCreateDeathLootCell(cell) == false) return false;
            if (lootEntries == null || lootEntries.Count == 0) return false;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            if (dataManager.TryGetTileDef(
                    TerrainTileType.DeathLoot,
                    out TerrainTileDef tileDef) == false)
                return false;

            if (terrainWorld.SetCell(
                    cell,
                    TerrainTileType.DeathLoot,
                    tileDef.MaxDurability,
                    0,
                    lootEntries) == false)
                return false;

            TerrainChanged?.Invoke(TerrainChangeReason.DeathLootPlacement);

            return true;
        }

        /// <summary>지정 Grid 좌표의 채굴 가능 지형 제거 요청</summary>
        public TerrainRemoveResult RequestRemoveCell(Vector2Int cell)
        {
            if (TryGetTileType(cell, out TerrainTileType type) == false)
                return IsReady ? TerrainRemoveResult.EmptyCell : TerrainRemoveResult.TerrainNotReady;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();
            
            if (dataManager.TryGetTileDef(type, out TerrainTileDef tileDef) == false ||
                tileDef.IsMineable == false)
                return TerrainRemoveResult.NotMineable;

            TerrainCell targetCell = terrainWorld.Data.GetCellOrNull(cell);
            TerrainCellDestroyedInfo destroyedInfo = new(
                cell,
                targetCell.Type,
                targetCell.ResourceID,
                terrainWorld.CellToWorldCenter(cell),
                targetCell.LootEntries);

            if (terrainWorld.TryDamageCell(
                    cell,
                    1,
                    out int remainingDurability,
                    out bool isDestroyed) == false)
                return TerrainRemoveResult.EmptyCell;

            TerrainDurabilityChanged?.Invoke(cell, remainingDurability);
            if (isDestroyed == false)
            {
                if (logEvents)
                    Debug.Log($"[Terrain Collapse] Cell Damaged: {cell}, durability={remainingDurability}");
                
                return TerrainRemoveResult.Damaged;
            }

            if (logEvents) Debug.Log($"[Terrain Collapse] Cell Destroy: {cell}");

            CellDestroyed?.Invoke(destroyedInfo);
            TerrainChanged?.Invoke(TerrainChangeReason.Excavation);
            
            return TerrainRemoveResult.Removed;
        }

        // 기존 bool 반환 호출부 호환용 안전 제거 요청
        public bool RemoveCell(Vector2Int cell)
        {
            TerrainRemoveResult result = RequestRemoveCell(cell);
            
            return result == TerrainRemoveResult.Damaged || result == TerrainRemoveResult.Removed;
        }

        public bool ApplyAuthoritativeBatch(TerrainChangeBatch batch, out string error)
            => ApplyAuthoritativeBatch(batch, TerrainChangeReason.AuthoritativeBatch, out error);

        public bool ApplyAuthoritativeBatch(
            TerrainChangeBatch batch,
            TerrainChangeReason reason,
            out string error)
        {
            if (terrainWorld == null)
            {
                error = "TerrainGridWorld 참조가 없습니다.";
                
                return false;
            }

            if (terrainWorld.ApplyAuthoritativeBatch(batch, out error) == false) return false;
            if (logEvents)
                Debug.Log($"[Terrain Collapse] Authoritative batch applied: collapse={batch.CollapseId}, " +
                          $"revision={batch.ResultRevision}, changes={batch.Changes.Count}");
            
            AuthoritativeBatchApplied?.Invoke(batch);
            TerrainChanged?.Invoke(reason);
            
            return true;
        }

        public TerrainSnapshot CreateSnapshot() => terrainWorld != null ? terrainWorld.CreateSnapshot() : null;

        /// <summary>기반암을 제외한 지형 그룹을 탐색하고 바닥에 놓이지 않은 그룹 분리</summary>
        public List<List<Vector2Int>> FindFloatingGroups()
        {
            if (IsReady == false) return new List<List<Vector2Int>>();

            List<Vector2Int> occupiedSnapshot = new(terrainWorld.GetOccupiedCells());
            occupiedSnapshot.Sort(TerrainCoordinateComparer.Compare);
            HashSet<Vector2Int> visited = new();
            List<List<Vector2Int>> floatingGroups = new();

            foreach (Vector2Int start in occupiedSnapshot)
            {
                if (TryGetTileType(start, out TerrainTileType startType) == false ||
                    startType == TerrainTileType.Bedrock ||
                    startType == TerrainTileType.SpawnPlatform ||
                    visited.Add(start) == false)
                    continue;

                List<Vector2Int> group = new();
                Queue<Vector2Int> queue = new();
                bool supported = false;

                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    group.Add(cell);

                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int next = cell + direction;
                        
                        if (TryGetTileType(next, out TerrainTileType nextType) == false)
                            continue;

                        if (nextType == TerrainTileType.Bedrock ||
                            nextType == TerrainTileType.SpawnPlatform)
                        {
                            supported = true;
                            continue;
                        }

                        if (visited.Add(next))
                            queue.Enqueue(next);
                    }
                }

                if (supported == false)
                {
                    group.Sort(TerrainCoordinateComparer.Compare);
                    floatingGroups.Add(group);
                }
            }

            floatingGroups.Sort((left, right) => TerrainCoordinateComparer.Compare(left[0], right[0]));

            return floatingGroups;
        }
    }
}

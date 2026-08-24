using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 실시간 Sparse Grid 상태와 Revision 관리 및 서버 확정 Batch와 Snapshot 처리

    /// <summary> 지형 원본 데이터, 빈 공간을 저장하지 않는 Sparse Grid</summary>
    public sealed class TerrainGridData
    {
        private readonly Dictionary<Vector2Int, TerrainCell> cells = new();

        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Count => cells.Count;
        public uint Revision { get; private set; }

        public TerrainGridData(float cellSize, Vector2 origin)
        {
            CellSize = cellSize;
            Origin = origin;
        }

        public bool IsOccupied(Vector2Int coord) => cells.ContainsKey(coord);

        public TerrainCell GetCellOrNull(Vector2Int coord)
            => cells.TryGetValue(coord, out TerrainCell cell) ? cell : null;

        public bool SetCell(Vector2Int coord, TerrainTileType type, int durability)
        {
            if (type == TerrainTileType.Empty) return Remove(coord);
            
            if (cells.TryGetValue(coord, out TerrainCell existing))
            {
                if (existing.Type == type && existing.CurrentDurability == durability) return false;
                
                existing.ChangeType(type, durability);
                return true;
            }
            
            cells.Add(coord, new TerrainCell(coord, type, durability));
            
            return true;
        }

        public bool SetOccupied(Vector2Int coord, int durability) => SetCell(coord, TerrainTileType.Ground, durability);

        public bool TryDamage(
            Vector2Int coord,
            int amount,
            out int remainingDurability,
            out bool isDestroyed)
        {
            remainingDurability = 0;
            isDestroyed = false;
            
            if (amount <= 0 || cells.TryGetValue(coord, out TerrainCell cell) == false)
                return false;

            remainingDurability = cell.Damage(amount);
            
            if (remainingDurability > 0) return true;

            cells.Remove(coord);
            isDestroyed = true;
            
            return true;
        }

        public bool Remove(Vector2Int coord) => cells.Remove(coord);

        public void SetRevision(uint revision) => Revision = revision;

        /// <summary>revision을 확인한 뒤 변경 전체를 원자적으로 적용한다.</summary>
        public bool TryApplyAuthoritativeBatch(TerrainChangeBatch batch, out string error)
        {
            if (batch == null)
            {
                error = "변경 Batch가 null입니다.";
                
                return false;
            }
            
            if (batch.BaseRevision != Revision)
            {
                error = $"Revision 불일치: local={Revision}, base={batch.BaseRevision}";
                
                return false;
            }
            
            if (batch.ResultRevision <= batch.BaseRevision)
            {
                error = $"ResultRevision은 BaseRevision보다 커야 합니다: {batch.ResultRevision}";
                
                return false;
            }

            if (batch.Changes == null) { error = "변경 목록이 null입니다."; return false; }
            
            HashSet<Vector2Int> seen = new();
            
            foreach (TerrainCellChange change in batch.Changes)
            {
                if (seen.Add(change.Coord) == false)
                {
                    error = $"중복 좌표가 있습니다: {change.Coord}";
                    
                    return false;
                }
            }

            foreach (TerrainCellChange change in batch.Changes)
                SetCell(change.Coord, change.Type, change.Durability);
            
            Revision = batch.ResultRevision;
            error = string.Empty;
            
            return true;
        }

        public TerrainSnapshot CreateSnapshot()
        {
            TerrainSnapshot snapshot = new TerrainSnapshot
            {
                Revision = Revision,
                CellSize = CellSize,
                Origin = Origin
            };
            
            foreach (TerrainCell cell in cells.Values)
                snapshot.Cells.Add(new TerrainCellChange(
                    cell.Coord,
                    cell.Type,
                    cell.CurrentDurability));
            
            snapshot.Cells.Sort(TerrainCoordinateComparer.CompareChanges);
            
            return snapshot;
        }

        public static TerrainGridData FromSnapshot(TerrainSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            if (snapshot.CellSize <= 0f) throw new System.ArgumentOutOfRangeException(nameof(snapshot.CellSize));
            
            TerrainGridData result = new TerrainGridData(snapshot.CellSize, snapshot.Origin);
            
            if (snapshot.Cells != null)
                foreach (TerrainCellChange cell in snapshot.Cells)
                    result.SetCell(cell.Coord, cell.Type, cell.Durability);
            
            result.Revision = snapshot.Revision;
            return result;
        }

        public IEnumerable<Vector2Int> OccupiedCoords => cells.Keys;

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x - Origin.x) / CellSize);
            int y = Mathf.FloorToInt((worldPosition.y - Origin.y) / CellSize);
            
            return new Vector2Int(x, y);
        }

        public Vector3 CellToWorldCenter(Vector2Int coord)
            => new(Origin.x + (coord.x + 0.5f) * CellSize,
                Origin.y + (coord.y + 0.5f) * CellSize, 0f);

        public Vector3 CellToWorldCorner(Vector2Int coord)
            => new(Origin.x + coord.x * CellSize, Origin.y + coord.y * CellSize, 0f);
    }
}

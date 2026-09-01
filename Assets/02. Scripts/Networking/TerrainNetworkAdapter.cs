using System.Collections.Generic;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>서버 전송 DTO를 Unity 런타임 지형 모델로 변환한다.</summary>
    public static class TerrainNetworkAdapter
    {
        public static TerrainSnapshot ToRuntime(TerrainSnapshotDto source)
        {
            if (source == null) return null;

            TerrainSnapshot result = new()
            {
                Revision = source.Revision,
                MapWidth = source.MapWidth,
                MapHeight = source.MapHeight,
                CellSize = source.CellSize,
                Origin = new Vector2(source.OriginX, source.OriginY),
            };

            AddCells(source.Cells, result.Cells);
            result.Cells.Sort(TerrainCoordinateComparer.CompareChanges);
            return result;
        }

        public static TerrainChangeBatch ToRuntime(TerrainChangeBatchDto source)
        {
            if (source == null) return null;

            TerrainChangeBatch result = new(
                source.CollapseID,
                source.BaseRevision,
                source.ResultRevision,
                ToRuntimeCells(source.Changes));
            return result;
        }

        private static void AddCells(IEnumerable<TerrainCellChangeDto> source, List<TerrainCellChange> target)
        {
            if (source == null) return;
            foreach (TerrainCellChangeDto cell in source)
                target.Add(ToRuntime(cell));
        }

        private static IEnumerable<TerrainCellChange> ToRuntimeCells(IEnumerable<TerrainCellChangeDto> source)
        {
            if (source == null) yield break;
            foreach (TerrainCellChangeDto cell in source)
                yield return ToRuntime(cell);
        }

        private static TerrainCellChange ToRuntime(TerrainCellChangeDto source)
        {
            List<TerrainCellLootEntry> loot = new();
            if (source.LootEntries != null)
                foreach (TerrainLootEntryDto entry in source.LootEntries)
                    loot.Add(new TerrainCellLootEntry(entry.ItemID, entry.Quantity));

            return new TerrainCellChange(
                new Vector2Int(source.Coord.X, source.Coord.Y),
                (TerrainTileType)source.TileTypeID,
                source.Durability,
                source.ResourceID,
                loot);
        }
    }
}

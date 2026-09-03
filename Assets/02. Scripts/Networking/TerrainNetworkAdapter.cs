using System.Collections.Generic;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>서버 전송 DTO를 Unity 런타임 지형 모델로 변환한다.</summary>
    public static class TerrainNetworkAdapter
    {
        /// 아래 함수는 서버가 준 시드로 여기서 만든 맵을 런타임 지형 모델로 바꿈
        /// 지형은 더 이상 서버에서 받지 않음.
        /// 서버와 클라이언트가 같은 내용으로 같은 지형을 만든다.
        /// 결과는 예전 스냅샷과 동일함. 서버 쪽 클래스는 ServerTerrainGenerator.
        public static TerrainSnapshot FromGeneratedMap(TerrainMapFile map)
        {
            if (map == null) return null;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            TerrainSnapshot result = new()
            {
                // 갓 만든 지형의 Revision 은 0이다. 서버도 0에서 시작.
                Revision = 0,
                MapWidth = map.Width,
                MapHeight = map.Height,
                CellSize = map.CellSize,
                Origin = map.Origin,
                SpawnArea = new RectInt(
                    map.RespawnAreaOrigin.x,
                    map.RespawnAreaOrigin.y,
                    map.RespawnAreaSize.x,
                    map.RespawnAreaSize.y),
            };

            // 아래에서 위로, 같은 행에서는 왼쪽에서 오른쪽으로 담는다.
            // 그것이 이 프로젝트의 표준 순서라 따로 정렬하지 않아도 된다.
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int coord = new(x, y);
                TerrainTileType type = map.GetTile(coord);

                if (type == TerrainTileType.Empty) continue;

                int resourceID = map.GetResource(coord);

                result.Cells.Add(new TerrainCellChange(
                    coord,
                    type,
                    GetMaxDurability(dataManager, type, resourceID),
                    resourceID,
                    new List<TerrainCellLootEntry>()));
            }

            return result;
        }

        // 자원이 붙은 칸은 자원의 내구도를 쓴다. 서버도 같은 규칙.
        private static int GetMaxDurability(
            TerrainDataManager dataManager,
            TerrainTileType type,
            int resourceID)
        {
            if (type == TerrainTileType.Empty) return 0;

            if (resourceID > 0 &&
                dataManager.Resources.TryGet(resourceID, out TerrainResourceDef resourceDef))
                return resourceDef.MaxDurability;

            return dataManager.TryGetTileDef(type, out TerrainTileDef tileDef)
                ? tileDef.MaxDurability
                : 0;
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

using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 파괴 직전 지형 셀이 보유한 자원 및 위치 정보

    public readonly struct TerrainCellDestroyedInfo
    {
        public Vector2Int Coord { get; }
        public TerrainTileType TileType { get; }
        public int ResourceID { get; }
        public Vector3 WorldPosition { get; }
        public IReadOnlyList<TerrainCellLootEntry> LootEntries { get; }
        public bool HasResource => ResourceID > 0;

        public TerrainCellDestroyedInfo(
            Vector2Int coord,
            TerrainTileType tileType,
            int resourceID,
            Vector3 worldPosition,
            IReadOnlyList<TerrainCellLootEntry> lootEntries = null)
        {
            Coord = coord;
            TileType = tileType;
            ResourceID = resourceID;
            WorldPosition = worldPosition;
            LootEntries = lootEntries == null
                ? new List<TerrainCellLootEntry>()
                : new List<TerrainCellLootEntry>(lootEntries);
        }
    }
}

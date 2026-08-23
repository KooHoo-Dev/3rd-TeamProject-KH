using System;

namespace TerrainCollapse
{
    // 데이터 테이블에서 관리하는 타일별 게임 규칙 정의

    [Serializable]
    public sealed class TerrainTileDTO
    {
        public TerrainTileType TileType;
        public string Name;
        public int MineHardness;
        public bool IsMineable;
        public bool CanCreateCollider;
        public bool CanSupportTerrain;
        public int DropItemID;
    }

    public sealed class TerrainTileDef
    {
        public TerrainTileType TileType { get; }
        public string Name { get; }
        public int MineHardness { get; }
        public bool IsMineable { get; }
        public bool CanCreateCollider { get; }
        public bool CanSupportTerrain { get; }
        public int DropItemID { get; }

        public TerrainTileDef(TerrainTileDTO dto)
        {
            TileType = dto.TileType;
            Name = dto.Name;
            MineHardness = dto.MineHardness;
            IsMineable = dto.IsMineable;
            CanCreateCollider = dto.CanCreateCollider;
            CanSupportTerrain = dto.CanSupportTerrain;
            DropItemID = dto.DropItemID;
        }
    }
}

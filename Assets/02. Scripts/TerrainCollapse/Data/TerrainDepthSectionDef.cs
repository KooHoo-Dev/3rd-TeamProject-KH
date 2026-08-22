using System;

namespace TerrainCollapse
{
    // 깊이 구간별 기본 블록과 적용 범위를 정의하는 지형 섹션 데이터

    [Serializable]
    public sealed class TerrainDepthSectionDTO
    {
        public string ProfileID;
        public string SectionID;
        public int SectionOrder;
        public int MinDepth;
        public int MaxDepth;
        public TerrainTileType BaseTileType;
    }

    public sealed class TerrainDepthSectionDef
    {
        public TerrainSeedProfileDef Profile { get; }
        public string SectionID { get; }
        public int SectionOrder { get; }
        public int MinDepth { get; }
        public int MaxDepth { get; }
        public TerrainTileDef BaseTile { get; }

        public TerrainDepthSectionDef(
            TerrainDepthSectionDTO dto,
            TerrainSeedProfileDef profile,
            TerrainTileDef baseTile)
        {
            Profile = profile;
            SectionID = dto.SectionID;
            SectionOrder = dto.SectionOrder;
            MinDepth = dto.MinDepth;
            MaxDepth = dto.MaxDepth;
            BaseTile = baseTile;
        }
    }
}

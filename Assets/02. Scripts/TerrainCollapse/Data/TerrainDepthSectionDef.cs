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
        public float MinDepthRatio;
        public float MaxDepthRatio;
        public TerrainTileType BaseTileType;
    }

    public sealed class TerrainDepthSectionDef
    {
        public TerrainSeedProfileDef Profile { get; }
        public string SectionID { get; }
        public int SectionOrder { get; }
        public float MinDepthRatio { get; }
        public float MaxDepthRatio { get; }
        public TerrainTileDef BaseTile { get; }

        public TerrainDepthSectionDef(
            TerrainDepthSectionDTO dto,
            TerrainSeedProfileDef profile,
            TerrainTileDef baseTile)
        {
            Profile = profile;
            SectionID = dto.SectionID;
            SectionOrder = dto.SectionOrder;
            MinDepthRatio = dto.MinDepthRatio;
            MaxDepthRatio = dto.MaxDepthRatio;
            BaseTile = baseTile;
        }

        public bool ContainsDepthRatio(float depthRatio)
        {
            if (depthRatio < MinDepthRatio) return false;
            return MaxDepthRatio >= 1f
                ? depthRatio <= MaxDepthRatio
                : depthRatio < MaxDepthRatio;
        }
    }
}

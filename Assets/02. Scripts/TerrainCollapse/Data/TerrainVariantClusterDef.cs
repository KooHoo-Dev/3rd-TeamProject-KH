using System;

namespace TerrainCollapse
{
    // 깊이 계층이 다른 일반 타일 군집을 생성하는 규칙 정의

    [Serializable]
    public sealed class TerrainVariantClusterDTO
    {
        public string ProfileID;
        public string SectionID;
        public int VariantOrder;
        public TerrainTileType VariantTileType;
        public int ClusterCount;
        public int MinWidth;
        public int MaxWidth;
        public int MinHeight;
        public int MaxHeight;
        public float FillRatio;
    }

    public sealed class TerrainVariantClusterDef
    {
        public TerrainDepthSectionDef Section { get; }
        public int VariantOrder { get; }
        public TerrainTileDef VariantTile { get; }
        public int ClusterCount { get; }
        public int MinWidth { get; }
        public int MaxWidth { get; }
        public int MinHeight { get; }
        public int MaxHeight { get; }
        public float FillRatio { get; }

        public TerrainVariantClusterDef(
            TerrainVariantClusterDTO dto,
            TerrainDepthSectionDef section,
            TerrainTileDef variantTile)
        {
            Section = section;
            VariantOrder = dto.VariantOrder;
            VariantTile = variantTile;
            ClusterCount = dto.ClusterCount;
            MinWidth = dto.MinWidth;
            MaxWidth = dto.MaxWidth;
            MinHeight = dto.MinHeight;
            MaxHeight = dto.MaxHeight;
            FillRatio = dto.FillRatio;
        }
    }
}

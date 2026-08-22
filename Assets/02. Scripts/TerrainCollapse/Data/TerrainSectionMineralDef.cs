using System;

namespace TerrainCollapse
{
    // 깊이 섹션에서 생성 가능한 광물과 군집 생성 수치를 정의하는 데이터

    [Serializable]
    public sealed class TerrainSectionMineralDTO
    {
        public string ProfileID;
        public string SectionID;
        public TerrainTileType MineralTileType;
        public float ClusterDensity; // 섹션 내 각 셀에서 군집 생성을 시도할 확률
        public int MinClusterSize;
        public int MaxClusterSize;
    }

    public sealed class TerrainSectionMineralDef
    {
        public TerrainSeedProfileDef Profile { get; }
        public TerrainDepthSectionDef Section { get; }
        public TerrainTileDef MineralTile { get; }
        public float ClusterDensity { get; }
        public int MinClusterSize { get; }
        public int MaxClusterSize { get; }

        public TerrainSectionMineralDef(
            TerrainSectionMineralDTO dto,
            TerrainSeedProfileDef profile,
            TerrainDepthSectionDef section,
            TerrainTileDef mineralTile)
        {
            Profile = profile;
            Section = section;
            MineralTile = mineralTile;
            ClusterDensity = dto.ClusterDensity;
            MinClusterSize = dto.MinClusterSize;
            MaxClusterSize = dto.MaxClusterSize;
        }
    }
}

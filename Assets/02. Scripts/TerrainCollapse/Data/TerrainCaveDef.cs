using System;

namespace TerrainCollapse
{
    // 동굴 생성 범위와 통로 형태 규칙 정의

    [Serializable]
    public sealed class TerrainCaveDTO
    {
        public string ProfileID;
        public int CaveOrder;
        public float MinDepthRatio;
        public float MaxDepthRatio;
        public int CaveCount;
        public int MinLength;
        public int MaxLength;
        public int MinRadius;
        public int MaxRadius;
        public int MaxAttempts;
    }

    public sealed class TerrainCaveDef
    {
        public TerrainSeedProfileDef Profile { get; }
        public int CaveOrder { get; }
        public float MinDepthRatio { get; }
        public float MaxDepthRatio { get; }
        public int CaveCount { get; }
        public int MinLength { get; }
        public int MaxLength { get; }
        public int MinRadius { get; }
        public int MaxRadius { get; }
        public int MaxAttempts { get; }

        public TerrainCaveDef(
            TerrainCaveDTO dto,
            TerrainSeedProfileDef profile)
        {
            Profile = profile;
            CaveOrder = dto.CaveOrder;
            MinDepthRatio = dto.MinDepthRatio;
            MaxDepthRatio = dto.MaxDepthRatio;
            CaveCount = dto.CaveCount;
            MinLength = dto.MinLength;
            MaxLength = dto.MaxLength;
            MinRadius = dto.MinRadius;
            MaxRadius = dto.MaxRadius;
            MaxAttempts = dto.MaxAttempts;
        }

        public bool ContainsDepthRatio(float depthRatio)
            => depthRatio >= MinDepthRatio && depthRatio <= MaxDepthRatio;
    }
}

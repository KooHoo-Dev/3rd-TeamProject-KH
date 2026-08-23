using System;

namespace TerrainCollapse
{
    // 맵 프로필별 크기와 표면 및 기반암 생성 설정 정의

    [Serializable]
    public sealed class TerrainSeedProfileDTO
    {
        public string ProfileID;
        public string Name;
        public int Width;
        public int Height;
        public float CellSize;
        public float OriginX;
        public float OriginY;
        public int SurfaceHeight;
        public int SurfaceAmplitude;
        public int BedrockDepth;
    }

    public sealed class TerrainSeedProfileDef
    {
        public string ProfileID { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public float OriginX { get; }
        public float OriginY { get; }
        public int SurfaceHeight { get; }
        public int SurfaceAmplitude { get; }
        public int BedrockDepth { get; }

        public TerrainSeedProfileDef(TerrainSeedProfileDTO dto)
        {
            ProfileID = dto.ProfileID;
            Name = dto.Name;
            Width = dto.Width;
            Height = dto.Height;
            CellSize = dto.CellSize;
            OriginX = dto.OriginX;
            OriginY = dto.OriginY;
            SurfaceHeight = dto.SurfaceHeight;
            SurfaceAmplitude = dto.SurfaceAmplitude;
            BedrockDepth = dto.BedrockDepth;
        }
    }
}

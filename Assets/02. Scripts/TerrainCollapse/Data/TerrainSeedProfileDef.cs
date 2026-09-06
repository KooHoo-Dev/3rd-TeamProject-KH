using System;

namespace TerrainCollapse
{
    // 맵 프로필별 크기와 타원형 경계 및 상단 개방 폭 설정 정의

    [Serializable]
    public sealed class TerrainSeedProfileDTO
    {
        public string ProfileID;
        public string Name;
        public int Width;
        public int Height;
        public float CellSize;
        public int TopOpeningWidth;
        public int BoundaryThickness;
        public int RespawnAreaHeight;
        public int RespawnPlatformWidth;
        public int RespawnExitWidth;
    }

    public sealed class TerrainSeedProfileDef
    {
        public string ProfileID { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public int TopOpeningWidth { get; }
        public int BottomFlatWidth => Math.Max(1, (int)Math.Round(Width * 0.25f, MidpointRounding.AwayFromZero));
        public int BoundaryThickness { get; }
        public int RespawnAreaHeight { get; }
        public int RespawnPlatformWidth { get; }
        public int RespawnExitWidth { get; }
        public float OriginX => -Width * CellSize * 0.5f;
        public float OriginY => -Height * CellSize;

        public TerrainSeedProfileDef(TerrainSeedProfileDTO dto)
        {
            ProfileID = dto.ProfileID;
            Name = dto.Name;
            Width = dto.Width;
            Height = dto.Height;
            CellSize = dto.CellSize;
            TopOpeningWidth = dto.TopOpeningWidth;
            BoundaryThickness = dto.BoundaryThickness;
            RespawnAreaHeight = dto.RespawnAreaHeight;
            RespawnPlatformWidth = dto.RespawnPlatformWidth;
            RespawnExitWidth = dto.RespawnExitWidth;
        }
    }
}

using System;

namespace TerrainCollapse
{
    // 맵 프로필별 크기와 표면 및 기반암 생성 설정 정의

    [Serializable]
    public sealed class TerrainSeedProfileDef
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
}

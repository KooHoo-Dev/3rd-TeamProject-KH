using System;
using UnityEngine;

namespace TerrainCollapse
{
    // 런타임 지형 생성에 필요한 맵 크기와 기본 지층 설정 정의

    [Serializable]
    public sealed class TerrainMapGenerationSettings
    {
        public string Name = "DebugTerrainMap";
        public int Width = 100;
        public int Height = 105;
        public int SolidDepth = 100;
        public float CellSize = 1f;
        public Vector2 Origin = new(-50f, -99f);
        public int BedrockDepth = 1;
        public TerrainTileType BaseTileType = TerrainTileType.Ground;
        public bool UseRandomSeed;
        public int Seed = 12345;
        public bool AddCollapseTestStructure = true;
    }
}

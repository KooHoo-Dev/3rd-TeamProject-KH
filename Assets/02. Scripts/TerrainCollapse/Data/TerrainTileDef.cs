using System;

namespace TerrainCollapse
{
    // 데이터 테이블에서 관리하는 타일별 게임 규칙 정의

    [Serializable]
    public sealed class TerrainTileDef
    {
        public TerrainTileType TileType;
        public string Name;
        public int MineHardness;
        public bool IsMineable;
        public bool CanCreateCollider;
        public bool CanSupportTerrain; // 현재 연결 그룹을 고정 지형으로 판정하는 타일 여부
        public int DropItemID;
    }
}

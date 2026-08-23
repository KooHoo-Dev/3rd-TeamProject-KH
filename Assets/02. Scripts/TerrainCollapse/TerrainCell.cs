using UnityEngine;

namespace TerrainCollapse
{
    // 지형 타일 타입과 TerrainGridData가 보관하는 단일 셀의 런타임 상태 정의

    public enum TerrainTileType
    {
        Empty = 0,
        Ground = 1,
        Dirt = 2,
        Stone = 3,
        Bedrock = 4,
        DeepStone = 5,
        CoalOre = 10,
        IronOre = 11
    }

    /// <summary>커스텀 지형 Grid의 한 칸이 보관하는 순수 런타임 데이터</summary>
    public sealed class TerrainCell
    {
        public Vector2Int Coord { get; }
        public TerrainTileType Type { get; private set; }

        public TerrainCell(Vector2Int coord, TerrainTileType type)
        {
            Coord = coord;
            Type = type;
        }

        public void ChangeType(TerrainTileType type) => Type = type;
    }
}

using System;
using System.Collections.Generic;

namespace TerrainCollapse.Networking
{
    // WebSocket JSON 메시지 분기에 사용하는 표준 Type 값

    public static class NetworkMessageTypes
    {
        public const string MapSession = "map_session";
        public const string TerrainExcavationRequest = "terrain_excavate";
        public const string TerrainChangeBatch = "terrain_batch";
        public const string TerrainSnapshot = "terrain_snapshot";
        public const string ExplorationSnapshot = "exploration_snapshot";
    }

    // JSON 메시지의 Type 필드만 먼저 읽기 위한 헤더 데이터

    [Serializable]
    public sealed class NetworkMessageHeader
    {
        public string Type { get; set; }
    }

    // 지형 요청과 서버 확정 상태 전송에 사용하는 공용 데이터 계약

    [Serializable]
    public sealed class TerrainExcavationRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainExcavationRequest;
        public long ClientRequestID { get; set; }
        public uint ExpectedTerrainRevision { get; set; }
        public GridCoord TargetCell { get; set; }
        public int DamageAmount { get; set; }

        public bool IsValid() => ClientRequestID > 0 && DamageAmount > 0;
    }

    [Serializable]
    public struct TerrainLootEntryDto
    {
        public int ItemID { get; set; }
        public int Quantity { get; set; }

        public TerrainLootEntryDto(int itemID, int quantity)
        {
            ItemID = itemID;
            Quantity = quantity;
        }
    }

    [Serializable]
    public struct TerrainCellChangeDto
    {
        public GridCoord Coord { get; set; }
        public int TileTypeID { get; set; }
        public int Durability { get; set; }
        public int ResourceID { get; set; }
        public List<TerrainLootEntryDto> LootEntries { get; set; }
    }

    [Serializable]
    public sealed class TerrainChangeBatchDto
    {
        public string MapSessionID { get; set; }
        public long CollapseID { get; set; }
        public uint BaseRevision { get; set; }
        public uint ResultRevision { get; set; }
        public List<TerrainCellChangeDto> Changes { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainSnapshotDto
    {
        public string MapSessionID { get; set; }
        public uint Revision { get; set; }
        public float CellSize { get; set; }
        public float OriginX { get; set; }
        public float OriginY { get; set; }
        public List<TerrainCellChangeDto> Cells { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainChangeBatchMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainChangeBatch;
        public TerrainChangeBatchDto Batch { get; set; }
    }

    [Serializable]
    public sealed class TerrainSnapshotMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainSnapshot;
        public TerrainSnapshotDto Snapshot { get; set; }
    }

    [Serializable]
    public sealed class ExplorationSnapshotDto
    {
        public string MapSessionID { get; set; }
        public string PlayerID { get; set; }
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        public List<int> ExploredCellIndices { get; set; } = new();
    }

    [Serializable]
    public sealed class ExplorationSnapshotMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.ExplorationSnapshot;
        public ExplorationSnapshotDto Snapshot { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace TerrainCollapse.Networking
{
    // WebSocket JSON 메시지 분기에 사용하는 표준 Type 값

    public static class NetworkMessageTypes
    {
        public const string Hello = "hello";
        public const string Welcome = "welcome";
        public const string Join = "join";
        public const string Leave = "leave";
        public const string PlayerMove = "player.move";
        public const string PlayerAction = "player.action";
        public const string PlayerReady = "player.ready";
        public const string GameStarted = "game.started";
        public const string GameEnded = "game.ended";
        public const string ChatSend = "chat.send";
        public const string ChatMessage = "chat.message";
        public const string ChatSystem = "chat.system";
        public const string PlayerHealthSnapshot = "player.health_snapshot";
        public const string PlayerHealthChanged = "player.health_changed";
        public const string PlayerDied = "player.died";
        public const string PlayerRespawned = "player.respawned";
        public const string PlayerKicked = "player.kicked";
        public const string PlayerDamage = "player.damage";
        public const string PlayerHeal = "player.heal";
        public const string PlayerRespawnRequest = "player.respawn_request";
        public const string State = "state";
        public const string Error = "error";
        public const string MapSession = "map_session";
        public const string TerrainExcavationRequest = "terrain_excavate";
        public const string TerrainCollapseStartRequest = "terrain_collapse_start";
        public const string TerrainCollapseStarted = "terrain_collapse_started";
        public const string TerrainCollapseCancelled = "terrain_collapse_cancelled";
        public const string TerrainCollapsePlacementRequest = "terrain_collapse_place";
        public const string TerrainDeathLootRequest = "terrain_death_loot";
        public const string TerrainChangeBatch = "terrain_batch";
        public const string WorldItemSnapshot = "world_item.snapshot";
        public const string WorldItemSpawned = "world_item.spawned";
        public const string WorldItemRemoved = "world_item.removed";
        public const string WorldItemPickup = "world_item.pickup";
        public const string WorldItemDrop = "world_item.drop";
        public const string DynamiteThrow = "dynamite.throw";
        public const string DynamiteThrown = "dynamite.thrown";
        public const string DynamiteExplodeRequest = "dynamite.explode_request";
        public const string DynamiteExploded = "dynamite.exploded";
        public const string UsableItemUse = "item.use";
        public const string UsableItemUsed = "item.used";
        public const string InventorySnapshot = "inventory.snapshot";
        public const string InventoryGoldRanking = "inventory.gold_ranking";
        public const string InventorySell = "inventory.sell";
        public const string InventoryDebugGold = "inventory.debug_gold";
        public const string ShopRequest = "shop.request";
    }

    // JSON 메시지의 Type 필드만 먼저 읽기 위한 헤더 데이터

    [Serializable]
    public sealed class NetworkMessageHeader
    {
        public string Type { get; set; }
    }

    [Serializable]
    public sealed class NetworkUser
    {
        public string Id { get; set; }
        public string ClientID { get; set; }
        public string NickName { get; set; }
    }

    [Serializable]
    public sealed class WelcomeMessage
    {
        public string Type { get; set; }
        public string RoomCode { get; set; }
        public NetworkUser User { get; set; }
        public NetworkUser[] Users { get; set; }
        public float SpawnX { get; set; }
        public float SpawnY { get; set; }
    }

    [Serializable]
    public sealed class PlayerReadyMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.PlayerReady;
    }

    [Serializable]
    public sealed class PlayerKickedMessage
    {
        public string Type { get; set; }
        public string Reason { get; set; }
    }

    [Serializable]
    public sealed class GameStartedMessage
    {
        public string Type { get; set; }
        public long ServerNowUnixMilliseconds { get; set; }
        public long StartedAtUnixMilliseconds { get; set; }
        public long EndsAtUnixMilliseconds { get; set; }
        public int GameDurationSeconds { get; set; }
        public int VictoryGold { get; set; }
        public int ReadyDelaySeconds { get; set; }
        public int CountdownSeconds { get; set; }
    }

    [Serializable]
    public sealed class GameResultPlayerDto
    {
        public string PlayerID { get; set; }
        public string NickName { get; set; }
        public int Gold { get; set; }
    }

    [Serializable]
    public sealed class GameEndedMessage
    {
        public string Type { get; set; }
        public string Reason { get; set; }
        public string[] WinnerPlayerIDs { get; set; } = Array.Empty<string>();
        public long EndedAtUnixMilliseconds { get; set; }
        public GameResultPlayerDto[] Players { get; set; } = Array.Empty<GameResultPlayerDto>();
    }

    [Serializable]
    public sealed class ChatSendRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.ChatSend;
        public string Text { get; set; }
    }

    [Serializable]
    public sealed class ChatBroadcastMessage
    {
        public string Type { get; set; }
        public string PlayerID { get; set; }
        public string ClientID { get; set; }
        public string NickName { get; set; }
        public int ElapsedSeconds { get; set; }
        public string Text { get; set; }
    }

    [Serializable]
    public sealed class ChatSystemMessage
    {
        public string Type { get; set; }
        public int ElapsedSeconds { get; set; }
        public string Text { get; set; }
    }

    // 지형 요청과 서버 확정 상태 전송에 사용하는 공용 데이터 계약

    [Serializable]
    public sealed class TerrainExcavationRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainExcavationRequest;
        // 서버 PacketHeader.RequestId와 이름을 맞춰 요청-응답을 연결한다.
        public string RequestId { get; set; }
        public GridCoord TargetCell { get; set; }
        public int ItemID { get; set; }

        public bool IsValid() => ItemID >= 0;
    }

    [Serializable]
    public sealed class TerrainCollapseStartRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainCollapseStartRequest;
        public string RequestId { get; set; }
        public List<TerrainCollapseStartGroupDto> Groups { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainCollapseStartGroupDto
    {
        public List<GridCoord> SourceCells { get; set; } = new();
    }

    // 서버가 낙하 예약을 취소했을 때 원본 셀 복구 정보를 전달한다.
    [Serializable]
    public sealed class TerrainCollapseCancelledMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainCollapseCancelled;
        public List<long> CollapseIDs { get; set; } = new();
        public List<TerrainCellChangeDto> RestoreCells { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainCollapseStartedMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainCollapseStarted;
        public string RequestId { get; set; }
        public List<TerrainCollapseStartedDto> Collapses { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainCollapseStartedDto
    {
        public long CollapseID { get; set; }
        public string OwnerPlayerID { get; set; }
        public uint StartedRevision { get; set; }
        public List<GridCoord> SourceCells { get; set; } = new();
    }

    [Serializable]
    public sealed class TerrainCollapsePlacementRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainCollapsePlacementRequest;
        public string RequestId { get; set; }
        public long CollapseID { get; set; }
        public List<GridCoord> SourceCells { get; set; } = new();
        public List<TerrainCellChangeDto> Changes { get; set; } = new();

        public bool IsValid()
            => CollapseID > 0 && SourceCells.Count > 0 && SourceCells.Count == Changes.Count;
    }

    [Serializable]
    public sealed class TerrainDeathLootRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainDeathLootRequest;
        public string RequestId { get; set; }
        public GridCoord TargetCell { get; set; }
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
    public sealed class TerrainChangeBatchMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.TerrainChangeBatch;
        // 서버가 요청의 RequestId를 그대로 되돌려준다.
        public string RequestId { get; set; }
        public TerrainChangeBatchDto Batch { get; set; }
    }

}

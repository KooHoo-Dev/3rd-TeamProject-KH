using System;
namespace TerrainCollapse.Networking
{
    [Serializable] public sealed class UsableItemUseRequest { public string Type { get; set; } = NetworkMessageTypes.UsableItemUse; public string RequestId { get; set; } public int ItemID { get; set; } }
    [Serializable] public sealed class UsableItemUsedMessage { public string Type { get; set; } public string RequestId { get; set; } public int ItemID { get; set; } public GridCoord[] DetectedCells { get; set; } = Array.Empty<GridCoord>(); }
}

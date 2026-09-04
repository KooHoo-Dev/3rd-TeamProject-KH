using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class InventoryItemDto
    {
        public int ItemID { get; set; }
        public int Quantity { get; set; }
    }

    [Serializable]
    public sealed class InventorySnapshotMessage
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public string PlayerID { get; set; }
        public InventoryItemDto[] Items { get; set; } = Array.Empty<InventoryItemDto>();
        public int CurrentWeight { get; set; } = -1;
        public int MaxWeight { get; set; } = -1;
    }

    [Serializable]
    public sealed class WorldItemDropDto
    {
        public string DropID { get; set; }
        public int ItemID { get; set; }
        public int Quantity { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public long PickupAvailableAtUnixMilliseconds { get; set; }
    }

    [Serializable]
    public sealed class WorldItemSnapshotMessage
    {
        public string Type { get; set; }
        public WorldItemDropDto[] Drops { get; set; } = Array.Empty<WorldItemDropDto>();
    }

    [Serializable]
    public sealed class WorldItemSpawnedMessage
    {
        public string Type { get; set; }
        public WorldItemDropDto Drop { get; set; }
    }

    [Serializable]
    public sealed class WorldItemRemovedMessage
    {
        public string Type { get; set; }
        public string DropID { get; set; }
        public string CollectedByPlayerID { get; set; }
    }

    [Serializable]
    public sealed class WorldItemPickupRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.WorldItemPickup;
        public string RequestId { get; set; }
        public string DropID { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class WorldItemDropRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.WorldItemDrop;
        public string RequestId { get; set; }
        public int ItemID { get; set; }
        public int Quantity { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }
}
